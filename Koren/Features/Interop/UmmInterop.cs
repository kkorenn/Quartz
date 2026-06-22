using System.Collections;
using System.Reflection;

namespace Koren.Features.Interop;

// Reflection bridge to Unity Mod Manager (UMM). ADOFAI runs UMM alongside
// MelonLoader, and UMM mods (e.g. xperfect) load into the SAME Unity/Mono
// domain — so once they're loaded we can read their state and reach into their
// public API at runtime.
//
// Everything here goes through reflection on purpose: Koren is a MelonLoader mod
// and must NOT hard-link UnityModManager.dll. If UMM isn't installed, every call
// degrades to "absent" (null/false) instead of throwing a missing-assembly load
// error. That keeps Koren loadable on a UMM-less install while still letting it
// detect and talk to UMM mods when they ARE present.
public static class UmmInterop {
    private static bool resolved;
    private static Type ummType;        // UnityModManagerNet.UnityModManager
    private static MethodInfo findMod;  // static ModEntry FindMod(string)
    private static FieldInfo modEntries; // static List<ModEntry>
    private static PropertyInfo modsPathProp; // static string modsPath { get; private set; }

    private static void Resolve() {
        if(resolved) {
            return;
        }
        resolved = true;
        try {
            // Assembly-qualified so it resolves the loaded UMM without a reference.
            ummType = Type.GetType("UnityModManagerNet.UnityModManager, UnityModManager");
            if(ummType == null) {
                return;
            }
            findMod = ummType.GetMethod("FindMod", BindingFlags.Public | BindingFlags.Static);
            modEntries = ummType.GetField("modEntries", BindingFlags.Public | BindingFlags.Static);
            modsPathProp = ummType.GetProperty("modsPath", BindingFlags.Public | BindingFlags.Static);
        } catch {
            ummType = null;
        }
    }

    // True when UMM itself is loaded in this process.
    public static bool IsPresent {
        get {
            Resolve();
            return ummType != null;
        }
    }

    // The raw UMM ModEntry for `id`, or null if UMM is absent or the mod isn't
    // installed. Returned as object so callers don't need the UMM type at compile
    // time; pair with the reflection helpers below.
    public static object FindMod(string id) {
        Resolve();
        if(findMod == null || string.IsNullOrEmpty(id)) {
            return null;
        }
        try {
            return findMod.Invoke(null, [id]);
        } catch {
            return null;
        }
    }

    // True only when the mod is installed, loaded, and currently enabled —
    // i.e. safe to reflect into. (UMM's ModEntry.Active gates exactly this.)
    public static bool IsModActive(string id) {
        object entry = FindMod(id);
        return entry != null && ReadMember(entry, "Active") is bool b && b;
    }

    // The mod's loaded assembly, for reflecting into its public types. Null if the
    // mod is absent or failed to load.
    public static Assembly GetModAssembly(string id) {
        object entry = FindMod(id);
        return entry == null ? null : ReadMember(entry, "Assembly") as Assembly;
    }

    // The mod's declared version string (from its Info.json), or null.
    public static string GetModVersion(string id) {
        object entry = FindMod(id);
        object info = entry == null ? null : ReadMember(entry, "Info");
        return info == null ? null : ReadMember(info, "Version") as string;
    }

    // Ids of every UMM mod currently active. Drives the "detect UMM-compat mods"
    // surface (and the startup log that reveals a mod's exact id).
    public static List<string> ActiveModIds() {
        Resolve();
        List<string> ids = [];
        try {
            if(modEntries?.GetValue(null) is IEnumerable entries) {
                foreach(object entry in entries) {
                    if(ReadMember(entry, "Active") is not bool active || !active) {
                        continue;
                    }
                    object info = ReadMember(entry, "Info");
                    if(info != null && ReadMember(info, "Id") is string id && !string.IsNullOrEmpty(id)) {
                        ids.Add(id);
                    }
                }
            }
        } catch {
        }
        return ids;
    }

    // The folder UMM scans for mods (the "UMMMods" directory). Lets callers walk
    // the mods folder on disk directly — e.g. to find an installed-but-unparsed
    // mod that never made it into modEntries. Null if UMM is absent.
    public static string ModsPath() {
        Resolve();
        try {
            return modsPathProp?.GetValue(null, null) as string;
        } catch {
            return null;
        }
    }

    // Ids of every UMM mod INSTALLED (a folder under modsPath with an Info.json),
    // whether or not it's currently enabled. UMM parses every such folder into
    // modEntries up front and only flips Active on the enabled ones, so this is
    // effectively the on-disk mods-folder scan — it surfaces a mod that's present
    // but toggled off in UMM, which an active-only list would miss. Reading
    // Info.Id/Path has no side effects (unlike the Active setter, which we never
    // touch), so this won't load a disabled mod.
    public static List<string> InstalledModIds() {
        Resolve();
        List<string> ids = [];
        try {
            if(modEntries?.GetValue(null) is IEnumerable entries) {
                foreach(object entry in entries) {
                    object info = ReadMember(entry, "Info");
                    if(info != null && ReadMember(info, "Id") is string id && !string.IsNullOrEmpty(id)) {
                        ids.Add(id);
                    }
                }
            }
        } catch {
        }
        return ids;
    }

    // Reads a public instance member by name, field OR property (UMM mixes both:
    // ModEntry.Active/Assembly are properties, ModInfo.Id/Version are fields).
    private static object ReadMember(object target, string name) {
        if(target == null) {
            return null;
        }
        try {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if(f != null) {
                return f.GetValue(target);
            }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(target);
        } catch {
            return null;
        }
    }
}
