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
// "Default" = the bundled SUIT font; any other name is built from an OS font.
public static class FontManager {
    public const string DefaultName = "Default (SUIT)";

    public static TMP_FontAsset Current { get; private set; }
    public static string CurrentName { get; private set; } = DefaultName;

    private static TMP_FontAsset defaultFont;
    private static readonly Dictionary<string, TMP_FontAsset> cache = [];
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

        var list = new List<string> { DefaultName };

        try {
            string[] os = Font.GetOSInstalledFontNames();
            if(os != null) {
                list.AddRange(os
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            }
        } catch(Exception e) {
            MainCore.Log.Wrn($"[FontManager] OS font list failed: {e.Message}");
        }

        available = [.. list];
        return available;
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

        try {
            Font os = Font.CreateDynamicFontFromOSFont(name, 32);
            if(os == null) {
                return null;
            }

            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(os);
            cache[name] = asset;
            return asset;
        } catch(Exception e) {
            MainCore.Log.Wrn($"[FontManager] build '{name}' failed: {e.Message}");
            return null;
        }
    }
}
