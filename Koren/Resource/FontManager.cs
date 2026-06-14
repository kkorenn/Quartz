using Koren.Core;
using UnityEngine;

using TMPro;

namespace Koren.Resource;

// Single global UI font. Every TMP text in the mod (panel, menu, pages, tooltip,
// Status HUD) draws with FontManager.Current. SetFont swaps it everywhere live.
// "Default" = the shipped Cookie Run Bold font. Other choices are .ttf/.otf/.ttc files from
// two folders: UserData/Koren/Fonts (shipped with the mod) and
// UserData/Koren/CustomFonts (imported by the user — these can be renamed and
// deleted from the settings font picker). The OS font list is never offered.
public static class FontManager {
    public const string DefaultName = "Default (Cookie Run Bold)";

    // Display name of the shipped font that backs DefaultName. It lives in the
    // Fonts folder and is built into the default TMP asset on startup; the
    // bundled SUIT asset is only a fallback if the file is missing.
    private const string DefaultFontFile = "Cookie Run Bold";

    // Sentinel value carried in the font dropdown for the "add a custom font"
    // action row. Picked up by the settings page, never used as a real font.
    public const string AddSentinel = "koren-add-custom-font";

    // Dropdown sentinel for "use the mod's overlay font" in the in-game-font
    // picker. Stored as an empty CoreSettings.GameOverlayFontName.
    public const string SameAsOverlay = "koren-overlay-font-same";

    public static TMP_FontAsset Current { get; private set; }
    public static string CurrentName { get; private set; } = DefaultName;

    // Font for the in-game overlay (the GameOverlayFont feature). Follows the
    // mod's overlay font when GameOverlayFontName is empty / "same as overlay",
    // otherwise the named font (falling back to the default if it's gone).
    public static TMP_FontAsset GameOverlayFontAsset {
        get {
            string name = MainCore.Conf?.GameOverlayFontName;
            return string.IsNullOrEmpty(name) || name == SameAsOverlay
                ? Current
                : GetFont(name);
        }
    }

    // Raised after the selected font changes (and after ApplyToAll re-points the
    // mod's UI). The GameOverlayFont feature listens so it can re-apply the font
    // to the game's own text when that option is on.
    public static event Action OnFontChanged;

    private static TMP_FontAsset defaultFont;
    // Source Font behind defaultFont when it was built from DefaultFontFile;
    // null when defaultFont fell back to the bundled (ResourceManager-owned)
    // SUIT asset, so Dispose knows whether it owns these.
    private static Font defaultSourceFont;
    private static readonly Dictionary<string, TMP_FontAsset> cache = [];
    // Source Font objects backing the dynamically-built cache assets, kept so
    // Dispose can destroy them (TMP_FontAsset.CreateFontAsset does not own them).
    private static readonly List<Font> sourceFonts = [];
    private static readonly Dictionary<string, Font> sourceByName = [];
    private static readonly Dictionary<string, string> fontFiles = [];
    // Subset of fontFiles that lives in CustomFontPath — the user-managed fonts.
    private static readonly HashSet<string> customNames = new(StringComparer.OrdinalIgnoreCase);
    private static string[] available;
    private static bool scanned;

    public static void Initialize() {
        defaultFont = BuildDefaultFont();
        Current = defaultFont;
        CurrentName = DefaultName;

        string saved = MainCore.Conf.FontName;
        // Treat a saved pick of the default file as "default" so it shows and
        // behaves as the default entry rather than a duplicate.
        if(!string.IsNullOrEmpty(saved) && saved != DefaultName && saved != DefaultFontFile) {
            SetFont(saved, false);
        }
    }

    // Builds the default TMP asset from the shipped Cookie Run Bold file, falling
    // back to the bundled SUIT asset if that file can't be loaded.
    private static TMP_FontAsset BuildDefaultFont() {
        EnsureScanned();

        if(fontFiles.TryGetValue(DefaultFontFile, out string path)) {
            try {
                Font font = new(path);
                TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);
                asset.isMultiAtlasTexturesEnabled = true;
                defaultSourceFont = font;
                return asset;
            } catch(Exception e) {
                MainCore.Log.Wrn($"[FontManager] default '{DefaultFontFile}' build failed: {e.Message}");
            }
        }

        defaultSourceFont = null;
        return MainCore.Res.Get<TMP_FontAsset>(Asset.SUIT_Medium);
    }

    public static IReadOnlyList<string> GetAvailableFonts() {
        if(available != null) {
            return available;
        }

        EnsureScanned();

        var list = new List<string> { DefaultName };
        // DefaultFontFile is represented by DefaultName, so don't list it twice.
        list.AddRange(fontFiles.Keys
            .Where(n => !string.Equals(n, DefaultFontFile, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        available = [.. list];
        return available;
    }

    public static bool IsCustomFont(string name) {
        if(string.IsNullOrEmpty(name) || name == DefaultName || name == AddSentinel) {
            return false;
        }
        EnsureScanned();
        return customNames.Contains(name);
    }

    private static void EnsureScanned() {
        if(!scanned) {
            ScanFontFiles();
        }
    }

    // Builds the display-name -> file-path map from both font folders. The
    // custom folder is scanned last so a user font shadows a shipped one of the
    // same name (and stays manageable).
    private static void ScanFontFiles() {
        fontFiles.Clear();
        customNames.Clear();

        ScanDir(MainCore.Paths.FontPath, false);
        ScanDir(MainCore.Paths.CustomFontPath, true);

        scanned = true;
    }

    private static void ScanDir(string dir, bool custom) {
        try {
            if(!Directory.Exists(dir)) {
                return;
            }

            foreach(string path in Directory.GetFiles(dir)) {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if(ext != ".ttf" && ext != ".otf" && ext != ".ttc") {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(path);
                if(string.IsNullOrWhiteSpace(name)) {
                    continue;
                }

                fontFiles[name] = path;
                if(custom) {
                    customNames.Add(name);
                } else {
                    customNames.Remove(name);
                }
            }
        } catch(Exception e) {
            MainCore.Log.Wrn($"[FontManager] font scan failed: {e.Message}");
        }
    }

    public static void SetFont(string name, bool save) {
        TMP_FontAsset asset = Resolve(name);
        if(asset == null) {
            asset = defaultFont;
            name = DefaultName;
        }

        Current = asset;
        CurrentName = name;

        ApplyToAll();

        if(save) {
            MainCore.Conf.FontName = name == DefaultName ? "" : name;
            MainCore.ConfMgr.RequestSave();
        }

        OnFontChanged?.Invoke();
    }

    // ===== custom font management =====

    // Copies a picked .ttf/.otf/.ttc into CustomFontPath under a unique name and
    // returns that display name (null on failure). Does not select it.
    public static string ImportFont(string srcPath) {
        if(string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) {
            return null;
        }

        string ext = Path.GetExtension(srcPath).ToLowerInvariant();
        if(ext != ".ttf" && ext != ".otf" && ext != ".ttc") {
            return null;
        }

        try {
            string dir = MainCore.Paths.CustomFontPath;
            Directory.CreateDirectory(dir);

            string baseName = Sanitize(Path.GetFileNameWithoutExtension(srcPath)) ?? "Font";
            string name = UniqueName(baseName);

            File.Copy(srcPath, Path.Combine(dir, name + ext), false);
            Invalidate();
            return name;
        } catch(Exception e) {
            MainCore.Log.Err($"[FontManager] import failed: {e.Message}");
            return null;
        }
    }

    // Renames a custom font's file. error carries a user-facing reason on false.
    public static bool RenameFont(string oldName, string newName, out string error) {
        error = null;
        EnsureScanned();

        if(!customNames.Contains(oldName) || !fontFiles.TryGetValue(oldName, out string oldPath)) {
            error = "Not a custom font.";
            return false;
        }

        string clean = Sanitize(newName);
        if(clean == null) {
            error = "Enter a valid name.";
            return false;
        }

        if(string.Equals(clean, oldName, StringComparison.Ordinal)) {
            return true;
        }

        if(fontFiles.ContainsKey(clean)) {
            error = "That name is already used.";
            return false;
        }

        string ext = Path.GetExtension(oldPath);
        string newPath = Path.Combine(MainCore.Paths.CustomFontPath, clean + ext);
        bool wasCurrent = CurrentName == oldName;

        // Release the built asset/source first so the file isn't locked.
        EvictCache(oldName);

        try {
            File.Move(oldPath, newPath);
        } catch(Exception e) {
            error = e.Message;
            MainCore.Log.Err($"[FontManager] rename failed: {e.Message}");
            Invalidate();
            if(wasCurrent) {
                SetFont(oldName, false); // file untouched on failure — rebuild it
            }
            return false;
        }

        Invalidate();
        if(wasCurrent) {
            SetFont(clean, true);
        }
        return true;
    }

    // Deletes a custom font's file. Reverts to the default font if it was active.
    public static bool DeleteFont(string name) {
        EnsureScanned();

        if(!customNames.Contains(name) || !fontFiles.TryGetValue(name, out string path)) {
            return false;
        }

        bool wasCurrent = CurrentName == name;
        EvictCache(name);

        try {
            File.Delete(path);
        } catch(Exception e) {
            MainCore.Log.Err($"[FontManager] delete failed: {e.Message}");
            Invalidate();
            if(wasCurrent) {
                SetFont(name, false); // file still there — rebuild it
            }
            return false;
        }

        Invalidate();
        if(wasCurrent) {
            SetFont(DefaultName, true);
        }
        return true;
    }

    private static string UniqueName(string baseName) {
        EnsureScanned();

        string name = baseName;
        int n = 2;
        while(fontFiles.ContainsKey(name)) {
            name = $"{baseName} ({n})";
            n++;
        }
        return name;
    }

    private static string Sanitize(string s) {
        if(string.IsNullOrWhiteSpace(s)) {
            return null;
        }

        foreach(char c in Path.GetInvalidFileNameChars()) {
            s = s.Replace(c, ' ');
        }

        s = s.Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    // Drops (and destroys) the cached asset + source font for a name so the
    // backing file can be moved or deleted, and so a later Resolve rebuilds it.
    private static void EvictCache(string name) {
        if(cache.TryGetValue(name, out TMP_FontAsset asset)) {
            DestroyFontAsset(asset);
            cache.Remove(name);
        }

        if(sourceByName.TryGetValue(name, out Font source)) {
            sourceFonts.Remove(source);
            if(source != null) {
                // Immediate (not deferred Destroy) so the backing file is
                // released now and the following File.Move/Delete won't hit a
                // lock on Windows.
                UnityEngine.Object.DestroyImmediate(source);
            }
            sourceByName.Remove(name);
        }
    }

    private static void Invalidate() {
        available = null;
        scanned = false;
        fontFiles.Clear();
        customNames.Clear();
    }

    // Builds (and caches) the TMP asset for a font by display name. Falls back
    // to the default font for unknown names. Used by the settings font picker to
    // render each option in its own face.
    public static TMP_FontAsset GetFont(string name) => Resolve(name) ?? defaultFont;

    // The UnityEngine.Font backing the current selection, or null for the
    // default (SUIT — a bundled TMP asset with no standalone font file) or a
    // font that failed to build. ADOFAI's in-game HUD draws with a legacy
    // UnityEngine.UI.Text, which needs a Font rather than the TMP_FontAsset that
    // Current exposes; the GameOverlayFont feature uses this to drive it.

    // Re-points every existing TMP text under the mod root at the current
    // font. Texts marked FontExempt manage their own font (font-picker rows).
    public static void ApplyToAll() {
        if(MainCore.Root == null || Current == null) {
            return;
        }

        TMP_Text[] texts = MainCore.Root.GetComponentsInChildren<TMP_Text>(true);
        for(int i = 0; i < texts.Length; i++) {
            if(texts[i] != null && texts[i].GetComponent<FontExempt>() == null) {
                texts[i].font = Current;
            }
        }
    }

    private static TMP_FontAsset Resolve(string name) {
        if(string.IsNullOrEmpty(name) || name == DefaultName || name == AddSentinel) {
            return defaultFont;
        }

        if(cache.TryGetValue(name, out TMP_FontAsset cached)) {
            return cached;
        }

        EnsureScanned();

        if(!fontFiles.TryGetValue(name, out string path)) {
            return null;
        }

        try {
            Font font = new(path);
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);
            asset.isMultiAtlasTexturesEnabled = true;
            cache[name] = asset;
            sourceFonts.Add(font);
            sourceByName[name] = font;
            return asset;
        } catch(Exception e) {
            MainCore.Log.Wrn($"[FontManager] build '{name}' failed: {e.Message}");
            return null;
        }
    }

    // Destroys every dynamically-built font asset (and its generated atlas
    // textures + material) and the source Font objects, then clears the caches.
    // The bundled SUIT fallback is owned by ResourceManager, so it is only freed
    // here when the default was built from DefaultFontFile instead.
    public static void Dispose() {
        foreach(TMP_FontAsset asset in cache.Values) {
            DestroyFontAsset(asset);
        }
        cache.Clear();

        foreach(Font font in sourceFonts) {
            if(font != null) {
                UnityEngine.Object.Destroy(font);
            }
        }
        sourceFonts.Clear();
        sourceByName.Clear();

        // We own the default only when it was built from a file here.
        if(defaultSourceFont != null) {
            DestroyFontAsset(defaultFont);
            UnityEngine.Object.Destroy(defaultSourceFont);
            defaultSourceFont = null;
        }
        defaultFont = null;

        Current = null;
        CurrentName = DefaultName;

        fontFiles.Clear();
        customNames.Clear();
        available = null;
        scanned = false;
    }

    private static void DestroyFontAsset(TMP_FontAsset asset) {
        if(asset == null) {
            return;
        }

        if(asset.material != null) {
            UnityEngine.Object.Destroy(asset.material);
        }

        Texture2D[] atlases = asset.atlasTextures;
        if(atlases != null) {
            foreach(Texture2D tex in atlases) {
                if(tex != null) {
                    UnityEngine.Object.Destroy(tex);
                }
            }
        }

        UnityEngine.Object.Destroy(asset);
    }
}

// Marks a TMP text that picks its own font (e.g. the font dropdown's option
// rows, each rendered in the face it names) so FontManager.ApplyToAll leaves
// it alone when the global font changes.
public sealed class FontExempt : MonoBehaviour { }
