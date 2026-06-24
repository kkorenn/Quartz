using GTweens.Contexts;
using Quartz.Async;
using Quartz.Compat;
using Quartz.Compat.Interface;
using Quartz.Core.Service;
using Quartz.Features.PlayCount;
using Quartz.Features.Combo;
using Quartz.Features.Editor;
using Quartz.Features.EffectRemover;
using Quartz.Features.GameOverlayFont;
using Quartz.Features.Optimizer;
using Quartz.Features.Judgement;
using Quartz.Features.KeyViewer;
using Quartz.Features.Nostalgia;
using Quartz.Features.OttoIcon;
using Quartz.Features.Panels;
using Quartz.Features.PlanetColors;
using Quartz.Features.ProgressBar;
using Quartz.Features.SongTitle;
using Quartz.Features.Status;
using Quartz.Features.Tweaks;
using Quartz.Features.UiHider;
using Quartz.IO;
using Quartz.Resource;
using Quartz.Update;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Quartz.Core;

// Owns the whole mod lifecycle. Service-based, ported from Overlayer's runtime
// but trimmed to the lean skeleton: no module discovery, tag engine, overlay
// canvas engine or Harmony patch controller — the only runtime feature is Status.
public sealed class QuartzRuntime {
    public Version Version { get; }
    public Assembly Assembly { get; }

    public QuartzLogger Logger { get; }

    public ModState State { get; }

    public event Action<bool, bool> OnModEnabledChanged;

    public PathService Paths { get; }

    public SettingsFile<CoreSettings> Config { get; }

    public LocalizationService Localization { get; private set; }

    public ResourceManager Resource { get; }
    public SpriteManager Sprite { get; }

    public GameObject RootObject { get; private set; }

    public GTweensContext TweensContext { get; }

    public readonly IQuartzHost Host;

    private readonly RuntimeServices services;
    private readonly RuntimeTicks ticks;

    private UIService uiService;
    private TweenService tweenService;
    private HarmonyService harmonyService;
    private PlayCount playCount;

    // Kept so Dispose can unsubscribe — otherwise a UnityModManager in-process
    // reload (Initialize → Dispose → Initialize) stacks a new live handler each
    // time, leaving stale copies firing on every scene load for the process life.
    private UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode> xperfectGuardHandler;

    public QuartzRuntime(IQuartzHost host) {
        Host = host;

        Version = new Version(Info.Version);
        Assembly = Assembly.GetExecutingAssembly();
        Logger = new QuartzLogger(
            host.QuartzLogger
        );
        State = new ModState();
        // QuartzFilePath is the data root verbatim (the host appends any
        // loader-specific suffix): <UserData>/Quartz on MelonLoader, the mod's
        // own self-contained folder on UnityModManager.
        Paths = new PathService(host.QuartzFilePath);
        Config = new SettingsFile<CoreSettings>(Paths.ConfigPath);
        Resource = new ResourceManager(
            Assembly,
            "Quartz.Resource.Embedded."
        );
        Sprite = new SpriteManager(Resource);
        services = new RuntimeServices();
        ticks = new RuntimeTicks();
        TweensContext = new GTweensContext();
    }

    // One-time migration for the KorenResourcePack v2 -> Quartz rename: bring the
    // user's settings over from the old UserData/Koren folder into UserData/Quartz.
    // Done per-entry so it never clobbers the freshly-installed shipped files
    // (Lang, native/) already sitting in the new folder. The legacy Koren folder
    // sits beside the new data root (both under UserData on MelonLoader); on
    // UnityModManager the sibling never exists, so this no-ops harmlessly.
    private void MigrateLegacyData() {
        try {
            string newRoot = Host.QuartzFilePath;          // <UserData>/Quartz (ML)
            string parent = Path.GetDirectoryName(newRoot); // <UserData>
            if(string.IsNullOrEmpty(parent)) {
                return;
            }
            string oldRoot = Path.Combine(parent, "Koren"); // <UserData>/Koren
            if(!Directory.Exists(oldRoot) ||
               string.Equals(Path.GetFullPath(oldRoot), Path.GetFullPath(newRoot), StringComparison.OrdinalIgnoreCase)) {
                return;
            }
            Directory.CreateDirectory(newRoot);
            int moved = 0;
            foreach(string entry in Directory.GetFileSystemEntries(oldRoot)) {
                string dest = Path.Combine(newRoot, Path.GetFileName(entry));
                if(File.Exists(dest) || Directory.Exists(dest)) {
                    continue; // keep the newer/shipped copy
                }
                try {
                    if(Directory.Exists(entry)) {
                        Directory.Move(entry, dest);
                    } else {
                        File.Move(entry, dest);
                    }
                    moved++;
                } catch(Exception e) {
                    Logger.Wrn($"[Startup] migrate '{Path.GetFileName(entry)}' failed: {e.Message}");
                }
            }
            if(moved > 0) {
                Logger.Msg($"[Startup] migrated {moved} item(s) from UserData/Koren to UserData/Quartz");
            }
        } catch(Exception e) {
            Logger.Wrn($"[Startup] legacy data migration failed: {e.Message}");
        }
    }

    // Self-heal for installs still carrying the pre-rename Mods/Koren.dll: the
    // mod can't rename its own loaded file in place, so it pulls the current
    // Quartz release (lays down Mods/Quartz.dll + shipped UserData/Quartz) and
    // retires Koren.dll. The download is async and applies next launch; the
    // UserData/Koren -> UserData/Quartz move already ran in MigrateLegacyData.
    // One-shot by nature: once Koren.dll is gone this no longer matches.
    private void TryLegacyRenameUpgrade() {
        try {
            // Assembly.Location is the path MelonLoader loaded us from
            // (.../Mods/Koren.dll when renamed). Fall back to probing Mods/ if a
            // loader handed us a byte[] image with no backing path.
            string dllPath = Assembly.Location;
            if(string.IsNullOrEmpty(dllPath) ||
               !string.Equals(Path.GetFileName(dllPath), "Koren.dll", StringComparison.OrdinalIgnoreCase)) {
                string probe = Path.Combine(Host.ModsPath, "Koren.dll");
                bool quartzPresent = File.Exists(Path.Combine(Host.ModsPath, "Quartz.dll"));
                // Only treat a stray Koren.dll as ours when there's no separate
                // Quartz.dll already loaded alongside it.
                if(string.IsNullOrEmpty(dllPath) && File.Exists(probe) && !quartzPresent) {
                    dllPath = probe;
                } else {
                    return;
                }
            }

            Logger.Msg("[Startup] running as Koren.dll — fetching Quartz release to migrate install");
            UpdateService.InstallLegacyRename(dllPath);
        } catch(Exception e) {
            Logger.Wrn($"[Startup] legacy rename upgrade failed: {e.Message}");
        }
    }

    public void Initialize() {
        // Per-phase timing so "the game took forever to start" reports can be
        // pinned to a phase from the log instead of guessed at.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var total = System.Diagnostics.Stopwatch.StartNew();

        // Carry settings over from the pre-rename UserData/Koren folder.
        MigrateLegacyData();

        Paths.Initialize();

        // The updater renames a running, mapped DLL aside as <name>.dll.old when
        // it can't overwrite it in place; this session loaded the new one, so the
        // leftovers are safe to delete now. Koren.dll.old comes from the
        // legacy-rename migration below (a pre-rename Koren.dll being retired).
        foreach(string stale in new[] { "Quartz.dll.old", "Koren.dll.old" }) {
            try {
                string oldDll = Path.Combine(Host.ModsPath, stale);
                if(File.Exists(oldDll)) {
                    File.Delete(oldDll);
                }
            } catch(Exception e) {
                Logger.Wrn($"[Startup] couldn't remove {stale}: {e.Message}");
            }
        }

        CreateRootObject();

        RootObject.AddComponent<MainThread>();

        Config.Load();

        // Needs the config loaded; first run captures the live settings as
        // the initial "Default" profile.
        ProfileManager.Initialize();

        Logger.Msg($"[Startup] paths + config took {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        FontManager.Initialize();
        // Re-apply the font to ADOFAI's own text whenever the selection changes
        // or a new scene loads.
        FontManager.OnFontChanged += GameOverlayFont.ApplyFontChange;
        GameOverlayFont.Initialize();
        Logger.Msg($"[Startup] FontManager took {sw.ElapsedMilliseconds} ms");

        Localization = new LocalizationService(Paths.LangPath, Config, Logger);

        uiService = new UIService();
        tweenService = new TweenService(TweensContext);
        harmonyService = new HarmonyService();
        playCount = new PlayCount();

        services.Add(Localization);
        services.Add(uiService);
        services.Add(tweenService);
        services.Add(playCount);
        services.Add(harmonyService);

        // Engine-level performance toggles (GC scheduling, process priority,
        // background execution). Static feature, so it just registers its tick.
        Optimizer.Initialize();

        ticks.Add(playCount);

        ticks.Add(uiService);
        ticks.Add(tweenService);
        ticks.Add(Optimizer.Ticker);

        // Keeps the editor's property-row template in sync with the
        // "Horizontal Properties" toggle (and reverts it when the mod is off).
        ticks.Add(EditorFeature.Ticker);

        services.Initialize(Logger);

        // Install the XPerfect reentry guard if XPerfect is already loaded, and
        // retry on each scene load in case it loads after us (load-order safe).
        Quartz.Features.Interop.XPerfectRecursionGuard.TryApply(harmonyService.Harmony);
        xperfectGuardHandler = (_, _) => Quartz.Features.Interop.XPerfectRecursionGuard.TryApply(harmonyService.Harmony);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += xperfectGuardHandler;

        sw.Restart();
        SetModEnabled(Config.Data.Active, false);
        Logger.Msg($"[Startup] SetModEnabled took {sw.ElapsedMilliseconds} ms");

        // A pre-rename install whose mod file is still Mods/Koren.dll self-heals
        // to the proper Quartz layout (fetches the release, retires Koren.dll).
        // Runs here, after Config.Load, because the fetch reads the update
        // channel + skipped-version from config. It sets the updater to
        // Installing, so the Check() below no-ops in that case (intended — the
        // migration is the update). Skipped on the common Quartz.dll install.
        // Gated on a host that self-updates. Both loaders do (each pulls its own
        // asset — Quartz.zip / QuartzUmm.zip — via Host.UpdateAssetName). The
        // Koren.dll legacy rename is MelonLoader-only history; it self-no-ops on
        // UnityModManager (no Koren.dll under the mod folder), so it's safe to
        // run on both.
        if(Host.SupportsSelfUpdate) {
            TryLegacyRenameUpgrade();

            // Background check so the Settings page can show any available update.
            UpdateService.Check();
        }

        Logger.Msg($"[Startup] total {total.ElapsedMilliseconds} ms");

        // Detect Unity Mod Manager + its loaded mods (e.g. xperfect). Logs the
        // exact mod ids so interop can target them by id.
        if(Quartz.Features.Interop.UmmInterop.IsPresent) {
            Logger.Msg($"[Umm] active mods: [{string.Join(", ", Quartz.Features.Interop.UmmInterop.ActiveModIds())}]");
        } else {
            Logger.Msg("[Umm] not present");
        }

        Logger.Msg("Hello");
    }

    public void Tick() => ticks.Tick();

    public void Dispose() {
        SetModEnabled(false, true);

        // Drop the persistent subscriptions this runtime added in Initialize.
        // Their targets are static, so without this a UnityModManager reload
        // would leave every prior session's handler live (see field comment).
        FontManager.OnFontChanged -= GameOverlayFont.ApplyFontChange;
        if(xperfectGuardHandler != null) {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= xperfectGuardHandler;
            xperfectGuardHandler = null;
        }

        Config.Save();

        // Keep the active profile in sync with the final on-disk settings so
        // switching profiles next session starts from what the user last saw.
        ProfileManager.CaptureActive();

        services.Dispose();

        Sprite.Dispose();
        // Destroy the dynamically-built font assets (atlas + material + source
        // Font) before ResourceManager tears down the default font they fall
        // back to.
        Quartz.Resource.FontManager.Dispose();
        Resource.Dispose();

        if(RootObject != null) {
            Object.Destroy(RootObject);

            RootObject = null;
        }

        Logger.Msg("Bye");
    }

    public void SetModEnabled(bool enabled, bool isDispose) {
        if(State.IsEnabled == enabled) {
            return;
        }

        State.IsEnabled = enabled;

        if(!isDispose) {
            Config.Data.Active = enabled;
            Config.RequestSave();
        }

        if(enabled) {
            PanelsOverlay.Initialize(RootObject);
            ComboOverlay.Initialize(RootObject);
            ProgressBarOverlay.Initialize(RootObject);
            JudgementOverlay.Initialize(RootObject);
            KeyViewerOverlay.Initialize(RootObject);
            SongTitleOverlay.Initialize(RootObject);

            // Re-disable editor Save buttons if the Effect Remover is on.
            EffectRemover.RefreshEditorSaveButtons();

            // Re-apply the visual tweaks and planet colors to whatever scene
            // is live.
            Tweaks.RefreshAll();
            PlanetColors.Refresh();
            OttoIcon.Refresh();
            Optimizer.Apply();
            GameOverlayFont.Refresh();
            Nostalgia.Refresh();

            OnModEnabledChanged?.Invoke(true, isDispose);

            Logger.Msg("Mod Enabled");
        } else {
            OnModEnabledChanged?.Invoke(false, isDispose);

            SongTitleOverlay.Dispose();
            KeyViewerOverlay.Dispose();
            JudgementOverlay.Dispose();
            ProgressBarOverlay.Dispose();
            ComboOverlay.Dispose();
            PanelsOverlay.Dispose();

            // The remover's editor-save lock shouldn't outlive the mod.
            EffectRemover.RestoreEditorSaveButtons();

            // Put back the particles/glows/colors/UI the features changed.
            Tweaks.RestoreAll();
            PlanetColors.Restore();
            OttoIcon.Restore();
            UiHider.Restore();
            Optimizer.Restore();
            GameOverlayFont.Restore();
            EditorFeature.Restore();
            Features.Recorder.Recorder.Restore();
            Nostalgia.Restore();
            Features.AutoDeafen.AutoDeafen.Stop();

            Logger.Msg("Mod Disabled");
        }
    }

    private void CreateRootObject() {
        RootObject = new GameObject(
            "Quartz"
        );

        Object.DontDestroyOnLoad(
            RootObject
        );
    }
}
