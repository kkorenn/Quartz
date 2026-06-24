using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Quartz.Async;
using Quartz.Core;
using Newtonsoft.Json.Linq;

namespace Quartz.Update;

public enum UpdateStatus {
    Idle,
    Checking,
    UpToDate,
    Available,
    Installing,
    Installed, // downloaded + placed; needs a restart to take effect
    Skipped,   // user dismissed the offered version; undoable
    Failed,
}

// Why the last operation failed. The UI maps these to localized strings;
// Message keeps the raw detail for logs and the Developer page.
public enum UpdateFailure {
    None,
    Network,      // couldn't reach GitHub at all (DNS, offline, timeout)
    NotFound,     // releases feed returned 404 (repo private/renamed)
    RateLimited,  // GitHub API quota hit (403/429)
    CheckError,   // anything else during the check
    InstallError, // download or file copy failed
}

// A single available release found on GitHub.
public sealed class UpdateInfo {
    public string Tag;          // e.g. "v2.0.0-alpha-2"
    public SemVer Version;
    public string Url;          // release page
    public string AssetUrl;     // download URL for the release asset (null = none)
    public bool AssetIsZip;     // true: full Quartz.zip; false: legacy bare Quartz.dll
}

// Checks GitHub Releases for a newer build on the user's chosen channel and,
// on request, downloads it over the installed DLLs (applied next launch).
//
// NOTE: this needs the releases repo to be PUBLIC. While it's private the
// GitHub API returns 404 and the check just reports a failure — that's
// expected for now. (No token is baked in; auth would leak the repo to anyone
// holding the DLL.)
//
// In-place DLL replacement works on macOS/Linux (the running image is
// independent of the file). On Windows the loaded file is locked, so a swap
// there would need an external bootstrapper.
public static class UpdateService {
    public static UpdateStatus Status { get; private set; } = UpdateStatus.Idle;
    public static UpdateInfo Available { get; private set; }
    public static string Message { get; private set; } = "";
    public static UpdateFailure Failure { get; private set; } = UpdateFailure.None;

    // Download progress while Installing: 0..1, or -1 when unknown
    // (server sent no Content-Length).
    public static float Progress { get; private set; } = -1f;

    // The version most recently dismissed via Skip, so the UI can offer undo.
    public static string SkippedTag => lastSkipped?.Tag ?? MainCore.Conf.SkippedVersion;
    private static UpdateInfo lastSkipped;

    // The version downloaded this session (pending a restart). The running build
    // is still the old one until then, so a re-check would otherwise find this
    // same release "newer than running" and re-offer it; we keep reporting
    // Installed for it instead. Cleared naturally on restart (it's not persisted,
    // and after restart Info.Current already excludes it).
    private static SemVer? installedVersion;

    // Dev-only: when on, a fake update (same version, no real assets) is
    // offered so the update flow can be exercised without a real release.
    public static bool DevSimulate { get; private set; }

    // Raised on the main thread whenever Status / Available changes.
    public static event System.Action OnChanged;

    private static readonly HttpClient Http = CreateClient();

    private sealed class CheckException : System.Exception {
        public UpdateFailure Kind { get; }
        public CheckException(UpdateFailure kind, string message) : base(message) => Kind = kind;
    }

    private static HttpClient CreateClient() {
        try {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        } catch {
            // Some runtimes forbid changing this; the default may still work.
        }

        HttpClient client = new() { Timeout = System.TimeSpan.FromSeconds(20) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Quartz-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static void Set(UpdateStatus status, string message = "") {
        Status = status;
        Message = message ?? "";

        if(status != UpdateStatus.Failed) {
            Failure = UpdateFailure.None;
        }
        if(status != UpdateStatus.Installing) {
            Progress = -1f;
        }

        MainThread.Enqueue(() => OnChanged?.Invoke());
    }

    private static void Fail(UpdateFailure kind, string detail) {
        Failure = kind;
        Set(UpdateStatus.Failed, detail);
    }

    // Kicks off a background check. Safe to call from the main thread.
    public static async void Check() {
        // On hosts that don't self-update (UnityModManager owns updates via its
        // Repository mechanism), never check or offer in-mod updates — the UI
        // hides the update surface, and a stray call here stays a no-op.
        if(!MainCore.Host.SupportsSelfUpdate) {
            return;
        }

        if(Status is UpdateStatus.Checking or UpdateStatus.Installing) {
            return;
        }

        // An update downloaded this session is just waiting for a restart — keep
        // reporting that rather than re-checking and re-offering it.
        if(installedVersion.HasValue) {
            Set(UpdateStatus.Installed);
            return;
        }

        // While simulating, a real check would clear the fake update; keep
        // offering it instead so the flow stays testable.
        if(DevSimulate) {
            Available = Simulated();
            Set(UpdateStatus.Available);
            return;
        }

        Set(UpdateStatus.Checking);

        try {
            UpdateInfo found = await Task.Run(() => FetchLatest());
            Available = found;
            Set(found == null ? UpdateStatus.UpToDate : UpdateStatus.Available);
        } catch(System.Exception ex) {
            Available = null;
            Fail(Classify(ex), ex.Message);
            MainCore.Log.Wrn($"[Update] check failed: {ex.Message}");
        }
    }

    private static UpdateFailure Classify(System.Exception ex) => ex switch {
        CheckException ce => ce.Kind,
        HttpRequestException => UpdateFailure.Network,
        TaskCanceledException => UpdateFailure.Network, // HttpClient timeout
        _ => UpdateFailure.CheckError,
    };

    private static async Task<UpdateInfo> FetchLatest(bool forceLatest = false) {
        string url = $"https://api.github.com/repos/{Info.RepoOwner}/{Info.RepoName}/releases?per_page=30";

        string json;
        using(HttpResponseMessage resp = await Http.GetAsync(url)) {
            if(resp.StatusCode == HttpStatusCode.NotFound) {
                throw new CheckException(UpdateFailure.NotFound, "releases feed returned 404");
            }
            if((int)resp.StatusCode is 403 or 429) {
                throw new CheckException(UpdateFailure.RateLimited, $"GitHub returned {(int)resp.StatusCode}");
            }
            resp.EnsureSuccessStatusCode();
            json = await resp.Content.ReadAsStringAsync();
        }

        JArray releases = JArray.Parse(json);

        SemVer current = Info.Current;
        string skipped = MainCore.Conf.SkippedVersion ?? string.Empty;

        UpdateInfo best = null;
        foreach(JToken rel in releases) {
            if((bool?)rel["draft"] == true) {
                continue;
            }

            string tag = (string)rel["tag_name"];
            if(string.IsNullOrEmpty(tag) || (!forceLatest && tag == skipped)) {
                continue;
            }
            if(!SemVer.TryParse(tag, out SemVer v)) {
                continue;
            }
            // Only builds on the chosen channel (or more stable) and strictly
            // newer than what's running. The legacy-rename path (forceLatest)
            // drops the "strictly newer" gate: the running build may already BE
            // the newest version, and its job is the filename/layout fix, not a
            // version bump.
            if(!MainCore.Conf.AcceptsChannel(v.Channel) || (!forceLatest && v.CompareTo(current) <= 0)) {
                continue;
            }

            // Prefer the loader's full zip (Quartz.zip on MelonLoader,
            // QuartzUmm.zip on UnityModManager — DLL + lang + fonts). The bare
            // Quartz.dll fallback only fits the MelonLoader layout, so it's
            // offered only when that's the loader's asset.
            string zipName = MainCore.Host.UpdateAssetName;
            bool allowDllFallback = zipName == "Quartz.zip";
            string zipUrl = null;
            string dllUrl = null;
            if(rel["assets"] is JArray assets) {
                foreach(JToken a in assets) {
                    string name = (string)a["name"];
                    if(name == zipName) {
                        zipUrl = (string)a["browser_download_url"];
                    } else if(allowDllFallback && name == "Quartz.dll") {
                        dllUrl = (string)a["browser_download_url"];
                    }
                }
            }
            string assetUrl = zipUrl ?? dllUrl;
            // A release without an installable asset can't be applied; skip it.
            if(assetUrl == null) {
                continue;
            }

            if(best == null || v.CompareTo(best.Version) > 0) {
                best = new UpdateInfo {
                    Tag = tag,
                    Version = v,
                    Url = (string)rel["html_url"],
                    AssetUrl = assetUrl,
                    AssetIsZip = zipUrl != null,
                };
            }
        }

        return best;
    }

    // Downloads the given release and writes it over the installed DLLs.
    public static async void Install(UpdateInfo info) {
        // The in-mod installer assumes the MelonLoader file layout (Mods/Quartz.dll
        // + UserData/Quartz). On UnityModManager it would corrupt the self-contained
        // mod folder, so refuse — UMM updates through its own Repository mechanism.
        if(!MainCore.Host.SupportsSelfUpdate) {
            return;
        }

        if(info == null || Status == UpdateStatus.Installing) {
            return;
        }

        // A dev-simulated update has no real assets — play a fake download
        // through the same Installing/Progress states the real path uses, so
        // the progress UI can be exercised, but don't touch any files.
        if(info.AssetUrl == null) {
            lastPercent = -1;
            Progress = 0f;
            Set(UpdateStatus.Installing);

            await SimulateDownload();

            Available = null;
            installedVersion = info.Version;
            Set(UpdateStatus.Installed, "DEV: simulated install — no files changed.");
            return;
        }

        lastPercent = -1;
        Progress = 0f;
        Set(UpdateStatus.Installing);

        try {
            await Task.Run(() => Download(info));
            Available = null;
            installedVersion = info.Version;
            Set(UpdateStatus.Installed);
        } catch(System.Exception ex) {
            Fail(UpdateFailure.InstallError, ex.Message);
            MainCore.Log.Wrn($"[Update] install failed: {ex.Message}");
        }
    }

    // A legacy install whose mod file is still named Koren.dll (the pre-rename
    // name) can't fix its own filename in place. We pull the current Quartz
    // release — which lays down Mods/Quartz.dll plus the shipped UserData/Quartz
    // files — then retire the stale Koren.dll so the next launch loads
    // Quartz.dll and this never fires again. Auto-triggered from startup; runs
    // through the same Installing/Installed states the manual update uses, so
    // the Settings page shows progress and the "restart to finish" prompt.
    public static async void InstallLegacyRename(string legacyDllPath) {
        if(Status == UpdateStatus.Installing) {
            return;
        }

        lastPercent = -1;
        Progress = 0f;
        Set(UpdateStatus.Installing);

        try {
            UpdateInfo info = await Task.Run(() => FetchLatest(forceLatest: true));
            if(info == null) {
                throw new System.Exception("no installable Quartz release found");
            }

            await Task.Run(() => Download(info));
            RetireLegacyDll(legacyDllPath);

            Available = null;
            installedVersion = info.Version;
            Set(UpdateStatus.Installed);
            MainCore.Log.Msg($"[Update] migrated Koren.dll install to Quartz {info.Tag} — restart to finish");
        } catch(System.Exception ex) {
            Fail(UpdateFailure.InstallError, ex.Message);
            MainCore.Log.Wrn($"[Update] legacy rename install failed: {ex.Message}");
        }
    }

    private static async Task Download(UpdateInfo info) {
        string staging = Path.Combine(MainCore.Paths.TempPath, "Update");

        // Clear any half-finished prior attempt.
        if(Directory.Exists(staging)) {
            Directory.Delete(staging, true);
        }
        Directory.CreateDirectory(staging);

        // Download to staging first so a failure can't leave half-written files
        // in the live folders.
        if(info.AssetIsZip) {
            string stagedZip = Path.Combine(staging, "Quartz.zip");
            await DownloadFile(info.AssetUrl, stagedZip, 0f, 1f);
            ExtractOverInstall(stagedZip);
        } else {
            string stagedQuartz = Path.Combine(staging, "Quartz.dll");
            await DownloadFile(info.AssetUrl, stagedQuartz, 0f, 1f);
            ReplaceFile(stagedQuartz, Path.Combine(MainCore.Host.ModsPath, "Quartz.dll"));
        }

        // Pre-merge installs shipped a separate loader in Mods and the core DLL
        // in UserLibs; leaving either behind would double-load the mod.
        DeleteIfExists(Path.Combine(MainCore.Host.ModsPath, "Quartz.Loader.ML.dll"));
        DeleteIfExists(Path.Combine(MainCore.Host.UserLibsPath, "Quartz.dll"));
    }

    // Extracts the release zip over the live install. Entry paths are relative to
    // the loader's extract root:
    //   MelonLoader: the game root (Mods/Quartz.dll, UserData/Quartz/Lang/*, ...).
    //   UnityModManager: the UMM mods dir (Quartz/Quartz.dll, Quartz/Info.json, ...).
    // Either way they land exactly where that loader's dist zip placed them.
    // Shipped files are overwritten; the user's settings (Settings.json, profiles)
    // and their own custom fonts aren't in the zip, so they're left untouched.
    private static void ExtractOverInstall(string zipPath) {
        string gameRoot = MainCore.Host.UpdateExtractRoot;
        if(string.IsNullOrEmpty(gameRoot)) {
            throw new System.Exception("couldn't resolve update extract root");
        }

        string rootFull = Path.GetFullPath(gameRoot);
        string rootPrefix = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach(ZipArchiveEntry entry in archive.Entries) {
            // Directory entries carry an empty Name.
            if(string.IsNullOrEmpty(entry.Name)) {
                continue;
            }

            string dest = Path.GetFullPath(Path.Combine(gameRoot, entry.FullName));

            // Guard against zip-slip (entries escaping the game root).
            if(!dest.StartsWith(rootPrefix, System.StringComparison.Ordinal)) {
                MainCore.Log.Wrn($"[Update] skipped suspicious zip entry: {entry.FullName}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest));

            // Extract to a temp beside the target, then swap it in. The swap
            // handles the running Quartz.dll, which is memory-mapped by
            // MelonLoader and can't be overwritten directly (Win32 1224).
            string tmp = dest + ".krnew";
            try {
                if(File.Exists(tmp)) {
                    File.Delete(tmp);
                }
            } catch {
            }
            entry.ExtractToFile(tmp, true);
            ReplaceFile(tmp, dest);
        }
    }

    // Puts `src` at `dest`, replacing whatever's there. A plain overwrite of a
    // loaded DLL fails on Windows (ERROR_USER_MAPPED_FILE / 1224) because the
    // file is memory-mapped, but renaming it aside is allowed: the new build
    // takes the path and the old mapping keeps working until the game exits.
    // The leftover ".old" is cleaned up on the next launch (see QuartzRuntime).
    private static void ReplaceFile(string src, string dest) {
        Directory.CreateDirectory(Path.GetDirectoryName(dest));

        if(File.Exists(dest)) {
            try {
                File.Delete(dest);
            } catch {
                string old = dest + ".old";
                try {
                    if(File.Exists(old)) {
                        File.Delete(old);
                    }
                } catch {
                }
                File.Move(dest, old);
            }
        }

        File.Move(src, dest);
    }

    // Removes the old Mods/Koren.dll after the Quartz release is laid down. It's
    // the running image, so on Windows it's memory-mapped and can't be deleted;
    // rename it aside instead (cleaned up next launch by QuartzRuntime, same as
    // Quartz.dll.old). On macOS/Linux the delete just succeeds.
    private static void RetireLegacyDll(string path) {
        if(string.IsNullOrEmpty(path) || !File.Exists(path)) {
            return;
        }
        try {
            File.Delete(path);
        } catch {
            string old = path + ".old";
            try {
                if(File.Exists(old)) {
                    File.Delete(old);
                }
            } catch {
            }
            try {
                File.Move(path, old);
            } catch(System.Exception ex) {
                MainCore.Log.Wrn($"[Update] couldn't retire {path}: {ex.Message}");
            }
        }
    }

    private static void DeleteIfExists(string path) {
        try {
            if(File.Exists(path)) {
                File.Delete(path);
            }
        } catch(System.Exception ex) {
            MainCore.Log.Wrn($"[Update] couldn't remove stale file {path}: {ex.Message}");
        }
    }

    // Dev-only: advances Progress in uneven chunks on a delay, mimicking a
    // real network download (~2-4s total) for the simulated install.
    private static async Task SimulateDownload() {
        System.Random rng = new();
        float p = 0f;

        while(p < 1f) {
            await Task.Delay(rng.Next(40, 140));
            p = System.Math.Min(1f, p + ((float)rng.NextDouble() * 0.05f) + 0.01f);
            ReportProgress(p);
        }
    }

    private static int lastPercent = -1;

    private static async Task DownloadFile(string url, string path, float from, float to) {
        using HttpResponseMessage resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        long total = resp.Content.Headers.ContentLength ?? -1;
        using Stream src = await resp.Content.ReadAsStreamAsync();
        using FileStream dst = new(path, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[64 * 1024];
        long done = 0;
        int n;
        while((n = await src.ReadAsync(buffer, 0, buffer.Length)) > 0) {
            dst.Write(buffer, 0, n);
            done += n;
            if(total > 0) {
                ReportProgress(from + ((to - from) * done / total));
            }
        }
    }

    // Pushes Progress to the UI, throttled to whole-percent changes so the
    // main thread isn't flooded with redraws.
    private static void ReportProgress(float value) {
        int percent = (int)(value * 100f);
        if(percent == lastPercent) {
            return;
        }

        lastPercent = percent;
        Progress = value;
        MainThread.Enqueue(() => OnChanged?.Invoke());
    }

    // Remembers this version as skipped so it's no longer offered.
    public static void Skip(UpdateInfo info) {
        if(info == null) {
            return;
        }

        MainCore.Conf.SkippedVersion = info.Tag;
        MainCore.ConfMgr.RequestSave();
        lastSkipped = info;
        Available = null;
        Set(UpdateStatus.Skipped);
    }

    // Re-offers the version dismissed by Skip (or re-checks if it's no longer
    // held in memory, e.g. the skip happened in a previous session).
    public static void UndoSkip() {
        MainCore.Conf.SkippedVersion = "";
        MainCore.ConfMgr.RequestSave();

        if(lastSkipped != null) {
            Available = lastSkipped;
            lastSkipped = null;
            Set(UpdateStatus.Available);
        } else {
            Check();
        }
    }

    private static UpdateInfo Simulated() => new() {
        Tag = "v" + Info.DisplayVersion,
        Version = Info.Current,
        Url = Info.GithubLink,
        AssetUrl = null,
    };

    // Dev-only: toggle a fake available update (current version, no assets) to
    // exercise the prompt + install flow without a real release.
    public static void SetDevSimulate(bool on) {
        DevSimulate = on;

        if(on) {
            Available = Simulated();
            Set(UpdateStatus.Available);
        } else {
            Available = null;
            Set(UpdateStatus.Idle);
        }
    }
}
