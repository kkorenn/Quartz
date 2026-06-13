using Newtonsoft.Json.Linq;
using Koren.Core;
using Koren.Localization;
using Koren.Resource;

namespace Koren.IO;

// Settings profiles. A profile is a snapshot of every settings json in
// UserData/Koren, stored under UserData/Koren/Profiles/<name>/. The live
// files in the root stay authoritative; the active profile is kept in sync
// by capturing on quit and right before switching, so selecting another
// profile never loses the current one. Export/import bundles a profile into
// a single .krprofile file so settings survive manual updates that replace
// the UserData folder.
public static class ProfileManager {
    public const string DEFAULT_NAME = "Default";
    public const string EXPORT_EXTENSION = "krprofile";

    private const string BUNDLE_TYPE = "KorenProfile";

    // Root json files that are not profile-switchable settings: play
    // statistics shouldn't rewind when switching, and the pointer file
    // tracks profiles rather than belonging to one.
    private static readonly string[] excluded = ["PlayCount.json", "Profiles.json"];

    public static string Active { get; private set; } = DEFAULT_NAME;

    public static string ProfilesPath => Path.Combine(MainCore.Paths.RootPath, "Profiles");
    private static string PointerPath => Path.Combine(MainCore.Paths.RootPath, "Profiles.json");

    private static string DirOf(string name) => Path.Combine(ProfilesPath, name);

    public static void Initialize() {
        try {
            Directory.CreateDirectory(ProfilesPath);

            if(File.Exists(PointerPath)) {
                JToken token = JToken.Parse(File.ReadAllText(PointerPath));
                string name = token["Active"]?.Value<string>();

                if(!string.IsNullOrWhiteSpace(name) && Directory.Exists(DirOf(name))) {
                    Active = name;
                }
            }

            // First run, or the pointer aimed at a deleted directory: capture
            // the live settings as the initial profile.
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
            CaptureActive();
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
    public static void CaptureActive() {
        try {
            SettingsRegistry.SaveAll();
            CaptureTo(Active);
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] Capture '{Active}' failed: {e}");
        }
    }

    // Switches the live settings to `name`. The caller owns any UI rebuild —
    // this only swaps files, reloads them and re-applies the runtime side
    // (mod enable state, font, language).
    public static bool Apply(string name) {
        if(name == Active || !Exists(name)) {
            return false;
        }

        try {
            CaptureActive();

            Active = name;
            SavePointer();

            // A debounced save firing mid-switch would overwrite the freshly
            // copied files with pre-switch data.
            SettingsRegistry.CancelPendingSaves();

            // Tear the overlays down with the old settings, swap the files,
            // then bring everything back up reading the new ones.
            MainCore.Runtime.SetModEnabled(false, true);

            foreach(string file in Directory.GetFiles(DirOf(name), "*.json")) {
                string fileName = Path.GetFileName(file);

                if(excluded.Contains(fileName)) {
                    continue;
                }

                File.Copy(file, Path.Combine(MainCore.Paths.RootPath, fileName), true);
            }

            SettingsRegistry.ReloadAll();

            FontManager.SetFont(
                string.IsNullOrEmpty(MainCore.Conf.FontName)
                    ? FontManager.DefaultName
                    : MainCore.Conf.FontName,
                false
            );
            MainCore.Tr.Language = string.IsNullOrWhiteSpace(MainCore.Conf.Language)
                ? Translator.FALLBACK_LANGUAGE
                : MainCore.Conf.Language;

            MainCore.Runtime.SetModEnabled(MainCore.Conf.Active, true);

            return true;
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] Apply '{name}' failed: {e}");

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
                CaptureActive();
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

            File.WriteAllText(destPath, bundle.ToString());

            return true;
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(ProfileManager)}] Export '{name}' failed: {e}");

            return false;
        }
    }

    // Creates a profile from a bundle file; returns its (uniquified) name,
    // or null when the file isn't a Koren profile. Does not auto-apply.
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
            Directory.CreateDirectory(dir);

            foreach(JProperty prop in files.Properties()) {
                // GetFileName guards against path traversal in bundle keys.
                string fileName = Path.GetFileName(prop.Name);

                if(!fileName.EndsWith(".json") || excluded.Contains(fileName)) {
                    continue;
                }

                File.WriteAllText(Path.Combine(dir, fileName), prop.Value.ToString());
            }

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

    private static void CaptureTo(string name) {
        string dir = DirOf(name);
        Directory.CreateDirectory(dir);

        // Clear stale snapshots first so a settings file deleted from the
        // root doesn't linger in the profile forever.
        foreach(string file in Directory.GetFiles(dir, "*.json")) {
            File.Delete(file);
        }

        foreach(string file in Directory.GetFiles(MainCore.Paths.RootPath, "*.json")) {
            string fileName = Path.GetFileName(file);

            if(excluded.Contains(fileName)) {
                continue;
            }

            File.Copy(file, Path.Combine(dir, fileName), true);
        }
    }

    private static void SavePointer() {
        File.WriteAllText(
            PointerPath,
            new JObject { ["Active"] = Active }.ToString()
        );
    }
}
