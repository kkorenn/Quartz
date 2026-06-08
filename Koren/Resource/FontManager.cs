using Koren.Core;
using UnityEngine;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.Resource;

// Single global UI font. Every TMP text in the mod (panel, menu, pages, tooltip,
// Status HUD) draws with FontManager.Current. SetFont swaps it everywhere live.
// "Default" = the bundled SUIT font; every other choice is a .ttf/.otf shipped in
// UserData/Koren/Fonts. Only those bundled fonts are offered — never the whole
// OS font list.
public static class FontManager {
    public const string DefaultName = "Default (SUIT)";

    public static TMP_FontAsset Current { get; private set; }
    public static string CurrentName { get; private set; } = DefaultName;

    private static TMP_FontAsset defaultFont;
    private static readonly Dictionary<string, TMP_FontAsset> cache = [];
    private static readonly Dictionary<string, string> fontFiles = [];
    private static string[] available;

    public static void Initialize() {
        defaultFont = MainCore.Res.Get<TMP_FontAsset>(Asset.SUIT_Medium);
        Current = defaultFont;
        CurrentName = DefaultName;

        string saved = MainCore.Conf.FontName;
        if(!string.IsNullOrEmpty(saved) && saved != DefaultName) {
            SetFont(saved, false);
        }
    }

    public static IReadOnlyList<string> GetAvailableFonts() {
        if(available != null) {
            return available;
        }

        ScanFontFiles();

        var list = new List<string> { DefaultName };
        list.AddRange(fontFiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        available = [.. list];
        return available;
    }

    // Builds the display-name -> file-path map from the bundled Fonts folder.
    private static void ScanFontFiles() {
        fontFiles.Clear();

        try {
            string dir = MainCore.Paths.FontPath;
            if(!Directory.Exists(dir)) {
                return;
            }

            foreach(string path in Directory.GetFiles(dir)) {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if(ext != ".ttf" && ext != ".otf" && ext != ".ttc") {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(path);
                if(!string.IsNullOrWhiteSpace(name) && !fontFiles.ContainsKey(name)) {
                    fontFiles[name] = path;
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
    }

    // Re-points every existing TMP text under the mod root at the current font.
    public static void ApplyToAll() {
        if(MainCore.Root == null || Current == null) {
            return;
        }

        TMP_Text[] texts = MainCore.Root.GetComponentsInChildren<TMP_Text>(true);
        for(int i = 0; i < texts.Length; i++) {
            if(texts[i] != null) {
                texts[i].font = Current;
            }
        }
    }

    private static TMP_FontAsset Resolve(string name) {
        if(string.IsNullOrEmpty(name) || name == DefaultName) {
            return defaultFont;
        }

        if(cache.TryGetValue(name, out TMP_FontAsset cached)) {
            return cached;
        }

        if(fontFiles.Count == 0) {
            ScanFontFiles();
        }

        if(!fontFiles.TryGetValue(name, out string path)) {
            return null;
        }

        try {
            Font font = new(path);
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);
            cache[name] = asset;
            return asset;
        } catch(Exception e) {
            MainCore.Log.Wrn($"[FontManager] build '{name}' failed: {e.Message}");
            return null;
        }
    }
}
