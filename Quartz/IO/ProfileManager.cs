using Newtonsoft.Json.Linq;
using Quartz.Core;
using Quartz.Localization;
using Quartz.Resource;

namespace Quartz.IO;

// Settings profiles. A profile is a snapshot of every settings json in
// UserData/Quartz, stored under UserData/Quartz/Profiles/<name>/. The live
// files in the root stay authoritative; the active profile is kept in sync
// by capturing on quit and right before switching, so selecting another
// profile never loses the current one. Export/import bundles a profile into
// a single .krprofile file so settings survive manual updates that replace
// the UserData folder.
public static class ProfileManager {
    public const string DEFAULT_NAME = "Default";
    public const string EXPORT_EXTENSION = "krprofile";

    private const string BUNDLE_TYPE = "QuartzProfile";

    // Root json files that are not profile-switchable settings: play
    // statistics shouldn't rewind when switching, and the pointer file
    // tracks profiles rather than belonging to one.
    private static readonly HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase) {
        "PlayCount.json",
        "Profiles.json",
    };

    public static string Active { get; private set; } = DEFAULT_NAME;

    public static string ProfilesPath => Path.Combine(MainCore.Paths.RootPath, "Profiles");
    private static string PointerPath => Path.Combine(MainCore.Paths.RootPath, "Profiles.json");
    private static string SwitchMarkerPath => Path.Combine(ProfilesPath, ".switch.json");
    private static string SwitchRollbackPath => Path.Combine(ProfilesPath, ".switch-rollback");

    private static string DirOf(string name) => Path.Combine(ProfilesPath, name);

    public static void Initialize() {
        try {
            Directory.CreateDirectory(ProfilesPath);
            RecoverProfileDirectories();
            RecoverInterruptedSwitch();

            if(File.Exists(PointerPath)) {
                JToken token = JToken.Parse(File.ReadAllText(PointerPath));
                string name = token["Active"]?.Value<string>();

                if(!string.IsNullOrWhiteSpace(name) && Directory.Exists(DirOf(name))) {
                    Active = name;
                }
            }

            // First run, or the pointer aimed at a deleted directory: capture
            // the live (code-default) settings as the initial profile.
            if(!Directory.Exists(DirOf(Active))) {
                Active = DEFAULT_NAME;
                CaptureTo(Active);
                SavePointer();
            }
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] Initialize failed: {e}");
        }
    }

    public static string[] List() {
        try {
            return [..
                Directory.GetDirectories(ProfilesPath)
                    .Where(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
                    .Select(Path.GetFileName)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            ];
        } catch {
            return [Active];
        }
    }

    public static bool Exists(string name)
        => !string.IsNullOrEmpty(name) && Directory.Exists(DirOf(name));

    // Strips path-hostile characters; returns null when nothing usable remains.
    public static string Sanitize(string name) {
        if(string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new([.. name.Trim().Where(c => !invalid.Contains(c))]);
        clean = clean.Trim().Trim('.');

        if(clean.Length > 32) {
            clean = clean[..32].Trim();
        }

        return clean.Length == 0 ? null : clean;
    }

    // Creates a new profile from the current live settings and makes it the
    // active one (the old active profile is captured first, so both hold the
    // same state until the user diverges them).
    public static bool Create(string name) {
        name = Sanitize(name);

        if(name == null || Exists(name)) {
            return false;
        }

        try {
            if(!CaptureActive()) {
                return false;
            }
            CaptureTo(name);
            Active = name;
            SavePointer();

            return true;
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] Create '{name}' failed: {e}");

            return false;
        }
    }

    public static bool Delete(string name) {
        if(name == Active || !Exists(name)) {
            return false;
        }

        try {
            Directory.Delete(DirOf(name), true);

            return true;
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] Delete '{name}' failed: {e}");

            return false;
        }
    }

    // Snapshot the live settings into the active profile. Called on quit and
    // before any switch, so edits always belong to the profile that was
    // active while they were made.
    public static bool CaptureActive() {
        try {
            if(!SettingsRegistry.SaveAll()) {
                return false;
            }
            CaptureTo(Active);
            return true;
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] Capture '{Active}' failed: {e}");
            return false;
        }
    }

    // Switches the live settings to `name`. The caller owns any UI rebuild —
    // this only swaps files, reloads them and re-applies the runtime side
    // (mod enable state, font, language).
    public static bool Apply(string name) {
        if(name == Active || !Exists(name)) {
            return false;
        }

        string previous = Active;
        Dictionary<string, string> previousFiles = null;
        bool runtimeStopped = false;
        bool switchStarted = false;

        try {
            if(!CaptureActive()) {
                throw new IOException("could not capture the active profile before switching");
            }

            // Read and validate both sides before changing live files. This catches
            // a corrupt/inaccessible profile without disabling the mod or leaving a
            // half-applied mixture on disk.
            Dictionary<string, string> targetFiles = ReadSettingsDirectory(DirOf(name), validateJson: true);
            previousFiles = ReadSettingsDirectory(MainCore.Paths.RootPath, validateJson: false);
            BeginSwitch(previous, previousFiles);
            switchStarted = true;

            // A debounced save firing mid-switch would overwrite the freshly
            // copied files with pre-switch data.
            SettingsRegistry.CancelPendingSaves();

            // Tear the overlays down with the old settings, swap the files,
            // then bring everything back up reading the new ones.
            MainCore.Runtime.SetModEnabled(false, true);
            runtimeStopped = true;

            ReplaceLiveSettings(targetFiles);

            SettingsRegistry.ReloadAll();

            ApplyRuntimeSettings();

            Active = name;
            SavePointer();
            MainCore.Runtime.SetModEnabled(MainCore.Conf.Active, true);
            CompleteSwitch();

            return true;
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] Apply '{name}' failed: {e}");

            // Restore the exact pre-switch file set and runtime state. Older or
            // imported profiles often omit newer feature files; exact replacement
            // also guarantees those omissions reset to defaults instead of leaking.
            if(previousFiles != null) {
                try {
                    ReplaceLiveSettings(previousFiles);
                    SettingsRegistry.ReloadAll();
                    ApplyRuntimeSettings();
                    Active = previous;
                    SavePointer();
                    if(runtimeStopped) {
                        MainCore.Runtime.SetModEnabled(MainCore.Conf.Active, true);
                    }
                    if(switchStarted) {
                        CompleteSwitch();
                    }
                } catch(Exception rollbackError) {
                    MainCore.Log.Err($"[{nameof(ProfileManager)}] Rollback '{previous}' failed: {rollbackError}");
                }
            }

            return false;
        }
    }

    // Writes the profile as a single-file bundle: every settings json keyed
    // by filename, plus enough metadata to validate on import.
    public static bool Export(string name, string destPath) {
        if(!Exists(name) || string.IsNullOrEmpty(destPath)) {
            return false;
        }

        try {
            if(name == Active) {
                if(!CaptureActive()) {
                    return false;
                }
            }

            JObject files = [];

            foreach(string file in Directory.GetFiles(DirOf(name), "*.json")) {
                try {
                    files[Path.GetFileName(file)] = JToken.Parse(File.ReadAllText(file));
                } catch {
                    // A corrupt settings file shouldn't block exporting the rest.
                }
            }

            JObject bundle = new() {
                ["Type"] = BUNDLE_TYPE,
                ["Version"] = Info.Version,
                ["Name"] = name,
                ["Files"] = files,
            };

            AtomicFile.WriteAllText(destPath, bundle.ToString());

            return true;
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] Export '{name}' failed: {e}");

            return false;
        }
    }

    // Creates a profile from a bundle file; returns its (uniquified) name,
    // or null when the file isn't a Quartz profile. Does not auto-apply.
    public static string Import(string srcPath) {
        try {
            JToken bundle = JToken.Parse(File.ReadAllText(srcPath));

            if(bundle["Type"]?.Value<string>() != BUNDLE_TYPE
                || bundle["Files"] is not JObject files) {
                return null;
            }

            string name = Sanitize(bundle["Name"]?.Value<string>())
                ?? Sanitize(Path.GetFileNameWithoutExtension(srcPath))
                ?? "Imported";
            name = Uniquify(name);

            string dir = DirOf(name);
            Dictionary<string, byte[]> imported = new(StringComparer.OrdinalIgnoreCase);

            foreach(JProperty prop in files.Properties()) {
                // GetFileName guards against path traversal in bundle keys.
                string fileName = Path.GetFileName(prop.Name);

                if(!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || excluded.Contains(fileName)) {
                    continue;
                }

                imported[fileName] = System.Text.Encoding.UTF8.GetBytes(prop.Value.ToString());
            }

            WriteProfileDirectory(dir, imported);

            return name;
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] Import '{srcPath}' failed: {e}");

            return null;
        }
    }

    private static string Uniquify(string name) {
        if(!Exists(name)) {
            return name;
        }

        for(int i = 2; ; i++) {
            string candidate = $"{name} ({i})";

            if(!Exists(candidate)) {
                return candidate;
            }
        }
    }

    // Built-in presets live under Presets/ (shipped via Resource/Export/Presets,
    // installed to UserData/Quartz/Presets).
    public static string PresetsPath => Path.Combine(MainCore.Paths.RootPath, "Presets");

    public readonly struct PresetInfo {
        public readonly string Path;
        public readonly string Name;
        public PresetInfo(string path, string name) {
            Path = path;
            Name = name;
        }
    }

    // The shipped presets, each a .krprofile under Presets/ (name read from the
    // bundle, falling back to the file name).
    public static List<PresetInfo> ListPresets() {
        List<PresetInfo> list = [];
        try {
            if(!Directory.Exists(PresetsPath)) {
                return list;
            }
            foreach(string file in Directory.GetFiles(PresetsPath, "*." + EXPORT_EXTENSION)) {
                string name = null;
                try {
                    JToken b = JToken.Parse(File.ReadAllText(file));
                    if(b["Type"]?.Value<string>() == BUNDLE_TYPE) {
                        name = b["Name"]?.Value<string>();
                    }
                } catch {
                }
                name = Sanitize(name) ?? Sanitize(Path.GetFileNameWithoutExtension(file));
                if(name != null) {
                    list.Add(new PresetInfo(file, name));
                }
            }
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] ListPresets failed: {e}");
        }
        return list;
    }

    // Applies a built-in preset: the first time, imports it as a profile named
    // after the preset; afterwards reuses that profile. Then switches to it.
    // Returns the profile name, or null on failure.
    public static string ApplyPreset(string presetPath) {
        try {
            JToken bundle = JToken.Parse(File.ReadAllText(presetPath));
            string name = Sanitize(bundle["Name"]?.Value<string>())
                ?? Sanitize(Path.GetFileNameWithoutExtension(presetPath));
            if(name == null) {
                return null;
            }

            if(!Exists(name)) {
                name = Import(presetPath);
                if(name == null) {
                    return null;
                }
            }

            if(name != Active) {
                Apply(name);
            }
            return name;
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] ApplyPreset '{presetPath}' failed: {e}");
            return null;
        }
    }

    private static void CaptureTo(string name) {
        string dir = DirOf(name);
        Dictionary<string, byte[]> snapshot = new(StringComparer.OrdinalIgnoreCase);

        foreach(string file in Directory.GetFiles(MainCore.Paths.RootPath, "*.json")) {
            string fileName = Path.GetFileName(file);

            if(excluded.Contains(fileName)) {
                continue;
            }

            snapshot[fileName] = File.ReadAllBytes(file);
        }

        WriteProfileDirectory(dir, snapshot);
    }

    private static void WriteProfileDirectory(string directory, IReadOnlyDictionary<string, byte[]> files) {
        string parent = Path.GetDirectoryName(directory);
        if(string.IsNullOrEmpty(parent)) {
            throw new IOException("profile directory has no parent");
        }
        Directory.CreateDirectory(parent);

        string leaf = Path.GetFileName(directory);
        string staging = Path.Combine(parent, "." + leaf + ".stage-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(parent, "." + leaf + ".old-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try {
            foreach(KeyValuePair<string, byte[]> file in files) {
                AtomicFile.WriteAllBytes(Path.Combine(staging, file.Key), file.Value);
            }

            bool hadPrevious = Directory.Exists(directory);
            if(hadPrevious) {
                Directory.Move(directory, backup);
            }

            try {
                Directory.Move(staging, directory);
            } catch {
                if(hadPrevious && !Directory.Exists(directory) && Directory.Exists(backup)) {
                    Directory.Move(backup, directory);
                }
                throw;
            }

            if(Directory.Exists(backup)) {
                try { Directory.Delete(backup, true); } catch { }
            }
        } finally {
            if(Directory.Exists(staging)) {
                try { Directory.Delete(staging, true); } catch { }
            }
        }
    }

    private static void RecoverProfileDirectories() {
        foreach(string directory in Directory.GetDirectories(ProfilesPath, ".*")) {
            string name = Path.GetFileName(directory);
            int oldMarker = name.LastIndexOf(".old-", StringComparison.Ordinal);
            int stageMarker = name.LastIndexOf(".stage-", StringComparison.Ordinal);

            if(oldMarker > 1 && IsSwapSuffix(name, oldMarker + 5)) {
                string target = DirOf(name[1..oldMarker]);
                if(!Directory.Exists(target)) {
                    Directory.Move(directory, target);
                } else {
                    Directory.Delete(directory, true);
                }
            } else if(stageMarker > 1 && IsSwapSuffix(name, stageMarker + 7)) {
                Directory.Delete(directory, true);
            }
        }
    }

    private static bool IsSwapSuffix(string name, int start) {
        return start < name.Length
            && Guid.TryParseExact(name[start..], "N", out _);
    }

    private static void BeginSwitch(string previous, IReadOnlyDictionary<string, string> previousFiles) {
        Dictionary<string, byte[]> rollback = previousFiles.ToDictionary(
            file => file.Key,
            file => System.Text.Encoding.UTF8.GetBytes(file.Value),
            StringComparer.OrdinalIgnoreCase
        );
        WriteProfileDirectory(SwitchRollbackPath, rollback);
        AtomicFile.WriteAllText(
            SwitchMarkerPath,
            new JObject { ["Previous"] = previous }.ToString()
        );
    }

    private static void CompleteSwitch() {
        if(File.Exists(SwitchMarkerPath)) {
            File.Delete(SwitchMarkerPath);
        }
        if(Directory.Exists(SwitchRollbackPath)) {
            try { Directory.Delete(SwitchRollbackPath, true); } catch { }
        }
    }

    private static void RecoverInterruptedSwitch() {
        if(!File.Exists(SwitchMarkerPath)) {
            if(Directory.Exists(SwitchRollbackPath)) {
                Directory.Delete(SwitchRollbackPath, true);
            }
            return;
        }

        JObject marker = JObject.Parse(File.ReadAllText(SwitchMarkerPath));
        string previous = marker.Value<string>("Previous");
        if(string.IsNullOrWhiteSpace(previous) || !Directory.Exists(SwitchRollbackPath)) {
            throw new IOException("profile switch rollback data is incomplete");
        }

        Dictionary<string, string> rollback = ReadSettingsDirectory(SwitchRollbackPath, validateJson: true);
        ReplaceLiveSettings(rollback);
        SettingsRegistry.ReloadAll();
        Active = previous;
        SavePointer();
        CompleteSwitch();
    }

    private static Dictionary<string, string> ReadSettingsDirectory(string directory, bool validateJson) {
        Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
        foreach(string file in Directory.GetFiles(directory, "*.json")) {
            string fileName = Path.GetFileName(file);
            if(excluded.Contains(fileName)) {
                continue;
            }

            string contents = File.ReadAllText(file);
            if(validateJson) {
                JToken.Parse(contents);
            }
            files[fileName] = contents;
        }
        return files;
    }

    private static void ReplaceLiveSettings(IReadOnlyDictionary<string, string> files) {
        foreach(string live in Directory.GetFiles(MainCore.Paths.RootPath, "*.json")) {
            string fileName = Path.GetFileName(live);
            if(!excluded.Contains(fileName) && !files.ContainsKey(fileName)) {
                File.Delete(live);
            }
        }

        foreach(KeyValuePair<string, string> file in files) {
            AtomicFile.WriteAllText(Path.Combine(MainCore.Paths.RootPath, file.Key), file.Value);
        }
    }

    private static void ApplyRuntimeSettings() {
        FontManager.SetFont(
            string.IsNullOrEmpty(MainCore.Conf.FontName)
                ? FontManager.DefaultName
                : MainCore.Conf.FontName,
            false
        );
        MainCore.Tr.Language = string.IsNullOrWhiteSpace(MainCore.Conf.Language)
            ? Translator.FALLBACK_LANGUAGE
            : MainCore.Conf.Language;
    }

    private static void SavePointer() {
        AtomicFile.WriteAllText(
            PointerPath,
            new JObject { ["Active"] = Active }.ToString()
        );
    }
}
