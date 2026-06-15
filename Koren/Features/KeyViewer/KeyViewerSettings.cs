using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.KeyViewer;

// Persisted config for the key viewer overlay. Simple-mode defaults match
// v1's Settings.cs: style 2 (16 keys), the KeyViewerSimpleKey* key codes,
// and the SKv* colors.
// Lives in UserData/Koren/KeyViewer.json.
//
// Not ported yet (v1 simple-mode features): foot keys.
public sealed class KeyViewerSettings : ISettingsFile {
    public const string ModeSimple = "simple";
    public const string ModeDmNote = "dmnote";

    public bool Enabled = true;

    // Keep the viewer on screen in menus and outside of gameplay, not just
    // while a level is playing. Default on so it behaves like a static HUD.
    public bool ShowOutsideGame = true;

    // Renderer mode (v1 KeyViewerMode): "simple" = the key grid, "dmnote" =
    // the DM-note preset renderer.
    public string Mode = ModeSimple;

    // 0 = 10 keys, 1 = 12, 2 = 16, 3 = 20 (v1 KeyViewerSimpleStyle).
    public int Style = 2;

    public bool IsSimpleMode => string.Equals(Mode, ModeSimple, StringComparison.OrdinalIgnoreCase);
    public bool IsDmNoteMode => string.Equals(Mode, ModeDmNote, StringComparison.OrdinalIgnoreCase);

    public float Size = 0.8f;
    public float OffsetX = -713.51886f;
    public float OffsetY = 24.76001f;

    // Keep the Key Limiter's allowed list matched to the keys on the viewer
    // (v1 KeyViewerSimpleSyncToKeyLimiter, default on).
    public bool SyncToKeyLimiter = true;

    // Rain effect (v1 KeyViewerSimpleUseRain + SKvRain* defaults). Width 0
    // means "use the key's width"; rain 2 covers the second/third row groups.
    public bool RainEnabled = true;
    public float RainSpeed = 450f;
    public float RainHeight = 300f;
    public float RainFade = 60f;
    // Rain streak width per row group; 0 = match the key's width. A positive
    // value is per key-column, so a 2-wide key (e.g. the 10-key's bottom row)
    // gets 2x that width.
    public float RainWidth = 0f;
    public float Rain2Width = 40f;
    public float RainOffsetY = 0f;
    public float Rain2OffsetY = 0f;

    // KPS/Total placement on the back row: false = far apart (one on each
    // side, the v1 default), true = side by side in the centre.
    public bool StatsTogether = true;

    public float RainR = 1f, RainG = 0f, RainB = 0f, RainA = 1f;
    public float Rain2R = 1f, Rain2G = 1f, Rain2B = 1f, Rain2A = 1f;
    public float Rain3R = 1f, Rain3G = 0f, Rain3B = 1f, Rain3A = 1f;

    // DM Note renderer settings from KorenResourcePack's KeyViewerMode =
    // "dmnote". PresetJson is the exported preset payload; SelectedTab picks
    // the key type inside it (for example "4key").
    public string DmPresetJson = "";
    public string DmSelectedTab = "4key";
    public float DmOffsetX = 0f;
    public float DmOffsetY = 240f;
    public float DmScale = 1f;
    public bool DmNoteEffect = true;
    public float DmNoteSpeed = 1000f;
    public float DmTrackHeight = 200f;
    public bool DmNoteReverse = false;
    public bool DmShowCounter = true;
    // KRP v2's single edge-fade value. The split fade fields are kept only so
    // configs saved by earlier builds can migrate without losing the value.
    public float DmFadePx = 60f;
    public float DmFadeTopPx = 60f;
    public float DmFadeBottomPx = 0f;
    public float DmReverseFadeTopPx = 0f;
    public float DmReverseFadeBottomPx = 60f;
    public bool DmDelayedNoteEnabled = false;
    public float DmShortNoteThresholdMs = 50f;
    public float DmShortNoteMinLengthPx = 30f;
    public float DmKeyDisplayDelayMs = 0f;
    // 0 = hide blocked keys, 1 = rain only, 2 = full press.
    public int DmOutOfLimiterMode = 1;

    // Key codes per style, stored as KeyCode ints like v1.
    public int[] Key10 = [113, 51, 52, 116, 111, 45, 61, 92, 32, 104];
    public int[] Key12 = [113, 51, 52, 116, 111, 45, 61, 92, 32, 98, 104, 46];
    public int[] Key16 = [113, 51, 52, 116, 111, 45, 61, 92, 32, 98, 104, 46, 97, 304, 273, 13];
    public int[] Key20 = [113, 51, 52, 116, 111, 45, 61, 92, 32, 98, 104, 44, 97, 304, 303, 13, 110, 103, 109, 107];

    // Per-slot label overrides (v1 KeyViewerSimpleKey*Text); empty = derive
    // the caption from the key code.
    public string[] Key10Text = new string[10];
    public string[] Key12Text = new string[12];
    public string[] Key16Text = new string[16];
    public string[] Key20Text = new string[20];

    // Box colors, idle and pressed (v1 SKvBg/SKvBgc/SKvOut/SKvOutc/SKvTxt/SKvTxtc).
    public float BgR = 1f, BgG = 0.2352941f, BgB = 0.2352941f, BgA = 0.1960784f;
    public float BgPressedR = 1f, BgPressedG = 1f, BgPressedB = 1f, BgPressedA = 1f;
    public float OutlineR = 1f, OutlineG = 0f, OutlineB = 0f, OutlineA = 1f;
    public float OutlinePressedR = 1f, OutlinePressedG = 1f, OutlinePressedB = 1f, OutlinePressedA = 1f;
    public float TextR = 1f, TextG = 1f, TextB = 1f, TextA = 1f;
    public float TextPressedR = 0f, TextPressedG = 0f, TextPressedB = 0f, TextPressedA = 1f;

    // Per-key press counts, keyed by upper-case KeyCode name. v1 kept these
    // in PlayerPrefs ("kvkey_*"); v2 keeps them with the rest of the config.
    public Dictionary<string, int> Counts = new(StringComparer.OrdinalIgnoreCase);

    public int[] KeysForStyle(int style) => style switch {
        0 => Key10,
        1 => Key12,
        3 => Key20,
        _ => Key16,
    };

    public string[] LabelsForStyle(int style) => style switch {
        0 => Key10Text,
        1 => Key12Text,
        3 => Key20Text,
        _ => Key16Text,
    };

    public Color GetBg() => new(BgR, BgG, BgB, BgA);
    public void SetBg(Color c) { BgR = c.r; BgG = c.g; BgB = c.b; BgA = c.a; }

    public Color GetBgPressed() => new(BgPressedR, BgPressedG, BgPressedB, BgPressedA);
    public void SetBgPressed(Color c) { BgPressedR = c.r; BgPressedG = c.g; BgPressedB = c.b; BgPressedA = c.a; }

    public Color GetOutline() => new(OutlineR, OutlineG, OutlineB, OutlineA);
    public void SetOutline(Color c) { OutlineR = c.r; OutlineG = c.g; OutlineB = c.b; OutlineA = c.a; }

    public Color GetOutlinePressed() => new(OutlinePressedR, OutlinePressedG, OutlinePressedB, OutlinePressedA);
    public void SetOutlinePressed(Color c) { OutlinePressedR = c.r; OutlinePressedG = c.g; OutlinePressedB = c.b; OutlinePressedA = c.a; }

    public Color GetText() => new(TextR, TextG, TextB, TextA);
    public void SetText(Color c) { TextR = c.r; TextG = c.g; TextB = c.b; TextA = c.a; }

    public Color GetTextPressed() => new(TextPressedR, TextPressedG, TextPressedB, TextPressedA);
    public void SetTextPressed(Color c) { TextPressedR = c.r; TextPressedG = c.g; TextPressedB = c.b; TextPressedA = c.a; }

    public Color GetRain() => new(RainR, RainG, RainB, RainA);
    public void SetRain(Color c) { RainR = c.r; RainG = c.g; RainB = c.b; RainA = c.a; }

    public Color GetRain2() => new(Rain2R, Rain2G, Rain2B, Rain2A);
    public void SetRain2(Color c) { Rain2R = c.r; Rain2G = c.g; Rain2B = c.b; Rain2A = c.a; }

    public Color GetRain3() => new(Rain3R, Rain3G, Rain3B, Rain3A);
    public void SetRain3(Color c) { Rain3R = c.r; Rain3G = c.g; Rain3B = c.b; Rain3A = c.a; }

    public int GetCount(string key) =>
        key != null && Counts.TryGetValue(key, out int v) ? v : 0;

    public void SetCount(string key, int value) {
        if(!string.IsNullOrEmpty(key)) {
            Counts[key] = value;
        }
    }

    public JToken Serialize() {
        JObject counts = [];
        foreach((string key, int value) in Counts) {
            counts[key] = value;
        }

        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(ShowOutsideGame)] = ShowOutsideGame,
            [nameof(Mode)] = NormalizeMode(Mode),
            [nameof(Style)] = Style,
            [nameof(Size)] = Size,
            [nameof(OffsetX)] = OffsetX,
            [nameof(OffsetY)] = OffsetY,
            [nameof(SyncToKeyLimiter)] = SyncToKeyLimiter,
            [nameof(RainEnabled)] = RainEnabled,
            [nameof(RainSpeed)] = RainSpeed,
            [nameof(RainHeight)] = RainHeight,
            [nameof(RainFade)] = RainFade,
            [nameof(RainWidth)] = RainWidth,
            [nameof(Rain2Width)] = Rain2Width,
            [nameof(RainOffsetY)] = RainOffsetY,
            [nameof(Rain2OffsetY)] = Rain2OffsetY,
            [nameof(StatsTogether)] = StatsTogether,
            [nameof(RainR)] = RainR, [nameof(RainG)] = RainG, [nameof(RainB)] = RainB, [nameof(RainA)] = RainA,
            [nameof(Rain2R)] = Rain2R, [nameof(Rain2G)] = Rain2G, [nameof(Rain2B)] = Rain2B, [nameof(Rain2A)] = Rain2A,
            [nameof(Rain3R)] = Rain3R, [nameof(Rain3G)] = Rain3G, [nameof(Rain3B)] = Rain3B, [nameof(Rain3A)] = Rain3A,
            [nameof(DmPresetJson)] = DmPresetJson,
            [nameof(DmSelectedTab)] = DmSelectedTab,
            [nameof(DmOffsetX)] = DmOffsetX,
            [nameof(DmOffsetY)] = DmOffsetY,
            [nameof(DmScale)] = DmScale,
            [nameof(DmNoteEffect)] = DmNoteEffect,
            [nameof(DmNoteSpeed)] = DmNoteSpeed,
            [nameof(DmTrackHeight)] = DmTrackHeight,
            [nameof(DmNoteReverse)] = DmNoteReverse,
            [nameof(DmShowCounter)] = DmShowCounter,
            [nameof(DmFadePx)] = DmFadePx,
            [nameof(DmFadeTopPx)] = DmFadeTopPx,
            [nameof(DmFadeBottomPx)] = DmFadeBottomPx,
            [nameof(DmReverseFadeTopPx)] = DmReverseFadeTopPx,
            [nameof(DmReverseFadeBottomPx)] = DmReverseFadeBottomPx,
            [nameof(DmDelayedNoteEnabled)] = DmDelayedNoteEnabled,
            [nameof(DmShortNoteThresholdMs)] = DmShortNoteThresholdMs,
            [nameof(DmShortNoteMinLengthPx)] = DmShortNoteMinLengthPx,
            [nameof(DmKeyDisplayDelayMs)] = DmKeyDisplayDelayMs,
            [nameof(DmOutOfLimiterMode)] = DmOutOfLimiterMode,
            [nameof(Key10)] = new JArray(Key10),
            [nameof(Key12)] = new JArray(Key12),
            [nameof(Key16)] = new JArray(Key16),
            [nameof(Key20)] = new JArray(Key20),
            [nameof(Key10Text)] = WriteLabels(Key10Text),
            [nameof(Key12Text)] = WriteLabels(Key12Text),
            [nameof(Key16Text)] = WriteLabels(Key16Text),
            [nameof(Key20Text)] = WriteLabels(Key20Text),
            [nameof(BgR)] = BgR, [nameof(BgG)] = BgG, [nameof(BgB)] = BgB, [nameof(BgA)] = BgA,
            [nameof(BgPressedR)] = BgPressedR, [nameof(BgPressedG)] = BgPressedG, [nameof(BgPressedB)] = BgPressedB, [nameof(BgPressedA)] = BgPressedA,
            [nameof(OutlineR)] = OutlineR, [nameof(OutlineG)] = OutlineG, [nameof(OutlineB)] = OutlineB, [nameof(OutlineA)] = OutlineA,
            [nameof(OutlinePressedR)] = OutlinePressedR, [nameof(OutlinePressedG)] = OutlinePressedG, [nameof(OutlinePressedB)] = OutlinePressedB, [nameof(OutlinePressedA)] = OutlinePressedA,
            [nameof(TextR)] = TextR, [nameof(TextG)] = TextG, [nameof(TextB)] = TextB, [nameof(TextA)] = TextA,
            [nameof(TextPressedR)] = TextPressedR, [nameof(TextPressedG)] = TextPressedG, [nameof(TextPressedB)] = TextPressedB, [nameof(TextPressedA)] = TextPressedA,
            [nameof(Counts)] = counts,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        ShowOutsideGame = IOUtils.Read(token, nameof(ShowOutsideGame), ShowOutsideGame);
        Mode = NormalizeMode(IOUtils.Read(token, nameof(Mode), Mode));
        Style = Mathf.Clamp(IOUtils.Read(token, nameof(Style), Style), 0, 3);
        Size = IOUtils.Read(token, nameof(Size), Size);
        OffsetX = IOUtils.Read(token, nameof(OffsetX), OffsetX);
        OffsetY = IOUtils.Read(token, nameof(OffsetY), OffsetY);
        SyncToKeyLimiter = IOUtils.Read(token, nameof(SyncToKeyLimiter), SyncToKeyLimiter);

        RainEnabled = IOUtils.Read(token, nameof(RainEnabled), RainEnabled);
        RainSpeed = IOUtils.Read(token, nameof(RainSpeed), RainSpeed);
        RainHeight = IOUtils.Read(token, nameof(RainHeight), RainHeight);
        RainFade = IOUtils.Read(token, nameof(RainFade), RainFade);
        RainWidth = IOUtils.Read(token, nameof(RainWidth), RainWidth);
        Rain2Width = IOUtils.Read(token, nameof(Rain2Width), Rain2Width);
        RainOffsetY = IOUtils.Read(token, nameof(RainOffsetY), RainOffsetY);
        Rain2OffsetY = IOUtils.Read(token, nameof(Rain2OffsetY), Rain2OffsetY);
        StatsTogether = IOUtils.Read(token, nameof(StatsTogether), StatsTogether);
        RainR = IOUtils.Read(token, nameof(RainR), RainR);
        RainG = IOUtils.Read(token, nameof(RainG), RainG);
        RainB = IOUtils.Read(token, nameof(RainB), RainB);
        RainA = IOUtils.Read(token, nameof(RainA), RainA);
        Rain2R = IOUtils.Read(token, nameof(Rain2R), Rain2R);
        Rain2G = IOUtils.Read(token, nameof(Rain2G), Rain2G);
        Rain2B = IOUtils.Read(token, nameof(Rain2B), Rain2B);
        Rain2A = IOUtils.Read(token, nameof(Rain2A), Rain2A);
        Rain3R = IOUtils.Read(token, nameof(Rain3R), Rain3R);
        Rain3G = IOUtils.Read(token, nameof(Rain3G), Rain3G);
        Rain3B = IOUtils.Read(token, nameof(Rain3B), Rain3B);
        Rain3A = IOUtils.Read(token, nameof(Rain3A), Rain3A);

        DmPresetJson = IOUtils.Read(token, nameof(DmPresetJson), DmPresetJson) ?? "";
        DmSelectedTab = IOUtils.Read(token, nameof(DmSelectedTab), DmSelectedTab) ?? "4key";
        DmOffsetX = IOUtils.Read(token, nameof(DmOffsetX), DmOffsetX);
        DmOffsetY = IOUtils.Read(token, nameof(DmOffsetY), DmOffsetY);
        DmScale = Mathf.Clamp(IOUtils.Read(token, nameof(DmScale), DmScale), 0.2f, 4f);
        DmNoteEffect = IOUtils.Read(token, nameof(DmNoteEffect), DmNoteEffect);
        DmNoteSpeed = Mathf.Clamp(IOUtils.Read(token, nameof(DmNoteSpeed), DmNoteSpeed), 1f, 5000f);
        DmTrackHeight = Mathf.Clamp(IOUtils.Read(token, nameof(DmTrackHeight), DmTrackHeight), 0f, 5000f);
        DmNoteReverse = IOUtils.Read(token, nameof(DmNoteReverse), DmNoteReverse);
        DmShowCounter = IOUtils.Read(token, nameof(DmShowCounter), DmShowCounter);
        bool hasSingleFade = token[nameof(DmFadePx)] != null;
        DmFadePx = Mathf.Clamp(IOUtils.Read(token, nameof(DmFadePx), DmFadePx), 0f, 2000f);
        // Only DmFadeTopPx is read — it feeds the legacy (!hasSingleFade)
        // migration below. The other three split fields are always derived from
        // DmFadePx, so reading them here was dead work.
        DmFadeTopPx = Mathf.Clamp(IOUtils.Read(token, nameof(DmFadeTopPx), DmFadeTopPx), 0f, 500f);
        if(!hasSingleFade && token[nameof(DmFadeTopPx)] != null) {
            DmFadePx = DmFadeTopPx;
        }
        DmFadeTopPx = DmFadePx;
        DmFadeBottomPx = 0f;
        DmReverseFadeTopPx = 0f;
        DmReverseFadeBottomPx = DmFadePx;
        DmDelayedNoteEnabled = IOUtils.Read(token, nameof(DmDelayedNoteEnabled), DmDelayedNoteEnabled);
        DmShortNoteThresholdMs = Mathf.Clamp(IOUtils.Read(token, nameof(DmShortNoteThresholdMs), DmShortNoteThresholdMs), 0f, 2000f);
        DmShortNoteMinLengthPx = Mathf.Clamp(IOUtils.Read(token, nameof(DmShortNoteMinLengthPx), DmShortNoteMinLengthPx), 1f, 9999f);
        DmKeyDisplayDelayMs = Mathf.Clamp(IOUtils.Read(token, nameof(DmKeyDisplayDelayMs), DmKeyDisplayDelayMs), 0f, 9999f);
        DmOutOfLimiterMode = Mathf.Clamp(IOUtils.Read(token, nameof(DmOutOfLimiterMode), DmOutOfLimiterMode), 0, 2);

        Key10 = ReadKeys(token, nameof(Key10), Key10);
        Key12 = ReadKeys(token, nameof(Key12), Key12);
        Key16 = ReadKeys(token, nameof(Key16), Key16);
        Key20 = ReadKeys(token, nameof(Key20), Key20);

        Key10Text = ReadLabels(token, nameof(Key10Text), Key10Text);
        Key12Text = ReadLabels(token, nameof(Key12Text), Key12Text);
        Key16Text = ReadLabels(token, nameof(Key16Text), Key16Text);
        Key20Text = ReadLabels(token, nameof(Key20Text), Key20Text);

        BgR = IOUtils.Read(token, nameof(BgR), BgR);
        BgG = IOUtils.Read(token, nameof(BgG), BgG);
        BgB = IOUtils.Read(token, nameof(BgB), BgB);
        BgA = IOUtils.Read(token, nameof(BgA), BgA);
        BgPressedR = IOUtils.Read(token, nameof(BgPressedR), BgPressedR);
        BgPressedG = IOUtils.Read(token, nameof(BgPressedG), BgPressedG);
        BgPressedB = IOUtils.Read(token, nameof(BgPressedB), BgPressedB);
        BgPressedA = IOUtils.Read(token, nameof(BgPressedA), BgPressedA);
        OutlineR = IOUtils.Read(token, nameof(OutlineR), OutlineR);
        OutlineG = IOUtils.Read(token, nameof(OutlineG), OutlineG);
        OutlineB = IOUtils.Read(token, nameof(OutlineB), OutlineB);
        OutlineA = IOUtils.Read(token, nameof(OutlineA), OutlineA);
        OutlinePressedR = IOUtils.Read(token, nameof(OutlinePressedR), OutlinePressedR);
        OutlinePressedG = IOUtils.Read(token, nameof(OutlinePressedG), OutlinePressedG);
        OutlinePressedB = IOUtils.Read(token, nameof(OutlinePressedB), OutlinePressedB);
        OutlinePressedA = IOUtils.Read(token, nameof(OutlinePressedA), OutlinePressedA);
        TextR = IOUtils.Read(token, nameof(TextR), TextR);
        TextG = IOUtils.Read(token, nameof(TextG), TextG);
        TextB = IOUtils.Read(token, nameof(TextB), TextB);
        TextA = IOUtils.Read(token, nameof(TextA), TextA);
        TextPressedR = IOUtils.Read(token, nameof(TextPressedR), TextPressedR);
        TextPressedG = IOUtils.Read(token, nameof(TextPressedG), TextPressedG);
        TextPressedB = IOUtils.Read(token, nameof(TextPressedB), TextPressedB);
        TextPressedA = IOUtils.Read(token, nameof(TextPressedA), TextPressedA);

        Counts.Clear();
        if(token[nameof(Counts)] is JObject counts) {
            foreach(var prop in counts.Properties()) {
                try {
                    Counts[prop.Name] = prop.Value.Value<int>();
                } catch {
                }
            }
        }
    }

    public static string NormalizeMode(string mode) =>
        string.Equals(mode, ModeDmNote, StringComparison.OrdinalIgnoreCase)
            ? ModeDmNote
            : ModeSimple;

    private static JArray WriteLabels(string[] labels) {
        JArray arr = [];
        foreach(string label in labels) {
            arr.Add(label ?? "");
        }
        return arr;
    }

    private static string[] ReadLabels(JToken token, string name, string[] fallback) {
        if(token[name] is not JArray arr || arr.Count != fallback.Length) {
            return fallback;
        }

        string[] result = new string[arr.Count];
        for(int i = 0; i < arr.Count; i++) {
            result[i] = arr[i].Type == JTokenType.String ? arr[i].ToString() : "";
        }
        return result;
    }

    private static int[] ReadKeys(JToken token, string name, int[] fallback) {
        if(token[name] is not JArray arr || arr.Count != fallback.Length) {
            return fallback;
        }

        try {
            int[] result = new int[arr.Count];
            for(int i = 0; i < arr.Count; i++) {
                result[i] = arr[i].Value<int>();
            }
            return result;
        } catch {
            return fallback;
        }
    }
}
