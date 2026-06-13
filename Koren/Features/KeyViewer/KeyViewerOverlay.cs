using System.Globalization;
using Koren.Core;
using Koren.Features.Status;
using Koren.IO;
using Koren.Resource;
using Koren.UI;
using Koren.UI.Utility;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.Features.KeyViewer;

// Key viewer overlay — a port of v1's "simple" key viewer: a fixed grid of
// key boxes (10/12/16/20-key styles) with per-key press counters plus KPS and
// Total stat boxes. Layout constants and defaults come from v1's
// SimplePresets (50px keys, 4px gap, 54px row pitch, 8-column grid).
// Draggable in Reorganize mode like the other HUD elements.
//
// v1 features not ported yet: rain / ghost rain, foot keys, label overrides,
// key rebinding UI, key-limiter sync.
public static class KeyViewerOverlay {
    public static SettingsFile<KeyViewerSettings> ConfMgr { get; private set; }
    public static KeyViewerSettings Conf => ConfMgr?.Data;

    // v1 SimplePresets constants.
    private const float KeyW = 50f;
    private const float KeyH = 50f;
    private const float KeyGap = 4f;
    private const float RowGap = 54f;
    private const float KeyRadius = 8f;
    private const float BorderWidth = 2f;
    private const float KeyFontSize = 18f;
    private const float CounterFontSize = 14f;
    private const float StatFontSize = 16f;
    // Second-row slot order for the 16/20-key styles (v1 BackSeq16).
    private static readonly int[] BackSeq16 = [12, 13, 9, 8, 10, 11, 14, 15];

    private static GameObject canvasObj;
    private static RectTransform root;
    private static GameObject dragObj;
    private static readonly List<Box> boxes = [];
    private static int builtStyle = -1;
    private static string builtMode;
    private static RainManager rainManager;
    private static float dmCanvasHeight = 250f;
    private static float dmCanvasWidth = 800f;
    private static float dmTrackHeight = 200f;
    private static float dmNoteSpeed = 1000f;
    private static bool dmNoteReverse;
    private static float dmFadePx = 60f;
    private static bool dmDelayedNoteEnabled;
    private static float dmShortNoteThresholdMs = 50f;
    private static float dmShortNoteMinLengthPx = 30f;
    private static float dmKeyDisplayDelayMs;

    // KPS = presses in the last second, same as v1's press log.
    private static readonly List<float> pressLog = [];
    private static int kpsMax;
    private static int kpsSum;
    private static int kpsSamples;
    private static float nextKpsSample;
    private static int totalCount;
    private static bool countsDirty;
    private static float nextCountsSave;

    private sealed class Box {
        public KeyCode Key;
        public bool IsKps;
        public bool IsKpsAvg;
        public bool IsKpsMax;
        public bool IsTotal;
        public string Name;
        public Image Border;
        public Image Fill;
        public TextMeshProUGUI Label;
        public TextMeshProUGUI Value;
        public bool Pressed;
        public bool GhostPressed;
        public bool RawPressed;
        public bool DisplayTargetPressed;
        public float DisplayTargetTime;
        public bool DelayedNotePending;
        public bool DelayedReleasedBeforeStart;
        public float DelayedDownTime;
        public float DelayedStartTime;
        public float DelayedReleaseTime;
        public int Count;
        public int LastShown = int.MinValue;

        // Rain spawn parameters: color group (1 = front row, 2 = back row,
        // 3 = the 20-key style's third row, 0 = no rain) and the box span.
        public int RainGroup;
        public float CenterX;
        public float BoxW;
        public RawRain LastRain;
        public RawRain LastGhostRain;
        public DmNoteSpec Dm;

        public bool IsStat => IsKps || IsKpsAvg || IsKpsMax || IsTotal;
    }

    private sealed class DmNoteSpec {
        public string KeyName;
        public string CountKey;
        public KeyCode KeyCode;
        public KeyCode GhostKeyCode;
        public float X, Y, W, H;
        public string DisplayText;
        public bool CounterEnabled;
        public bool InlineStatCounter;
        public bool NoteEnabled;
        public bool IsStat;
        public bool IsKps;
        public bool IsKpsAvg;
        public bool IsKpsMax;
        public bool IsTotal;
        public string CounterAlign;
        public string CounterAlignMode;
        public float CounterGap;
        public int FontSize;
        public int CounterFontSize;
        public Color Bg, ActiveBg, Outline, ActiveOutline, Text, ActiveText;
        public Color CounterText, ActiveCounterText, Rain, GhostRain;
        public Color CounterStroke, ActiveCounterStroke;
        public Color RainTop, RainBottom, GhostRainTop, GhostRainBottom;
        public float BorderRadius = KeyRadius;
        public float BoxBorderWidth = KeyViewerOverlay.BorderWidth;
        public bool CounterOutside;
        public bool NoteAutoYCorrection = true;
        public string NoteAlignment = "center";
        public float NoteW;
        public float NoteOffsetX;
        public float NoteOffsetY;
        public float TrackX;
        public float TrackBottomY;
    }

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<KeyViewerSettings>(
            Path.Combine(MainCore.Paths.RootPath, "KeyViewer.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    public static void Initialize(GameObject rootObject) {
        if(canvasObj != null) {
            return;
        }

        EnsureConf();

        canvasObj = new GameObject("KorenKeyViewerCanvas");
        canvasObj.transform.SetParent(rootObject.transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the combo counter (32757).
        canvas.sortingOrder = 32758;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject gridObj = new("KeyViewerGrid");
        gridObj.transform.SetParent(canvasObj.transform, false);
        root = gridObj.AddComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 0f);
        root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0f);

        rainManager = canvasObj.AddComponent<RainManager>();
        canvasObj.AddComponent<Updater>();

        Rebuild();
    }

    // Rebuilds the key grid from the current style/keys. Cheap enough to run
    // on any structural settings change.
    public static void Rebuild() {
        if(root == null) {
            return;
        }

        // Drops live under the root being torn down; reset before destroying.
        rainManager?.Clear();

        for(int i = root.childCount - 1; i >= 0; i--) {
            Object.Destroy(root.GetChild(i).gameObject);
        }
        boxes.Clear();
        dragObj = null;
        kpsMax = 0;
        kpsSum = 0;
        kpsSamples = 0;
        nextKpsSample = 0f;

        if(Conf.IsDmNoteMode) {
            BuildDmNote();
            return;
        }

        if(!Conf.IsSimpleMode) {
            builtMode = null;
            builtStyle = -1;
            root.sizeDelta = Vector2.zero;
            Apply();
            return;
        }

        int style = Mathf.Clamp(Conf.Style, 0, 3);
        builtMode = KeyViewerSettings.ModeSimple;
        builtStyle = style;
        int[] keys = Conf.KeysForStyle(style);

        // Rain layer first so drops render behind the key boxes.
        GameObject rainObj = new("RainLayer");
        rainObj.transform.SetParent(root, false);
        RectTransform rainLayer = rainObj.AddComponent<RectTransform>();
        rainLayer.anchorMin = Vector2.zero;
        rainLayer.anchorMax = Vector2.one;
        rainLayer.offsetMin = Vector2.zero;
        rainLayer.offsetMax = Vector2.zero;
        rainManager?.SetLayer(rainLayer);

        List<KeySlot> keySlots = [];
        List<StatSlot> statSlots = [];
        BuildLayout(style, keySlots, statSlots);

        foreach(KeySlot slot in keySlots) {
            AddKey(keys, slot.Slot, slot.X, slot.Y, slot.W, slot.H);
        }
        foreach(StatSlot slot in statSlots) {
            AddStat(slot.Total, slot.X, slot.Y, slot.W, slot.H);
        }

        root.sizeDelta = GridSize(style);

        totalCount = 0;
        foreach(Box box in boxes) {
            if(!box.IsStat) {
                totalCount += box.Count;
            }
        }

        AddReorganizeHandle();

        Apply();
        SyncKeysToKeyLimiter();
    }

    // Fires when the sync-to-limiter arrangement may have changed (option
    // toggled, viewer mode switched) so the Key Limiter page can lock or
    // unlock its key-editing UI.
    public static event Action SyncSettingChanged;

    public static void RaiseSyncSettingChanged() => SyncSettingChanged?.Invoke();

    // True while the Key Limiter's allowed keys are owned by the key viewer —
    // sync only runs in simple mode.
    public static bool IsSyncingToKeyLimiter {
        get {
            EnsureConf();
            return Conf is { SyncToKeyLimiter: true } && Conf.IsSimpleMode;
        }
    }

    // v1 SettingsGui.SyncSimpleKeysToKeyLimiter: while the option is on, the
    // Key Limiter's allowed list is overwritten with exactly the keys shown
    // on the viewer (normalized, deduped). Runs after every rebuild — style
    // change, rebind, startup — and when the option itself is switched on.
    public static void SyncKeysToKeyLimiter() {
        EnsureConf();
        if(!Conf.IsSimpleMode || !Conf.SyncToKeyLimiter) {
            return;
        }

        Features.KeyLimiter.KeyLimiter.EnsureConf();

        int[] keys = Conf.KeysForStyle(Mathf.Clamp(Conf.Style, 0, 3));
        List<int> result = [];
        HashSet<int> seen = [];
        foreach(int code in keys) {
            int normalized = (int)Features.KeyLimiter.KeyLimiter.NormalizeKey((KeyCode)code);
            if(normalized != 0 && seen.Add(normalized)) {
                result.Add(normalized);
            }
        }

        if(result.Count == 0) {
            return;
        }

        int[] current = Features.KeyLimiter.KeyLimiter.Conf.AllowedKeys;
        if(current != null && current.Length == result.Count) {
            bool same = true;
            for(int i = 0; i < current.Length; i++) {
                if(current[i] != result[i]) {
                    same = false;
                    break;
                }
            }
            if(same) {
                return;
            }
        }

        Features.KeyLimiter.KeyLimiter.SetAllowedKeys([.. result]);
    }

    // Re-applies position, scale and colors (no structural change).
    public static void Apply() {
        if(root == null) {
            return;
        }

        if(Conf.IsDmNoteMode) {
            if(builtMode != KeyViewerSettings.ModeDmNote) {
                Rebuild();
                return;
            }

            ApplyDmRuntimeSettings();
            root.anchoredPosition = new Vector2(Conf.DmOffsetX, Conf.DmOffsetY);
            float dmScale = Mathf.Clamp(Conf.DmScale, 0.2f, 4f);
            root.localScale = new Vector3(dmScale, dmScale, 1f);

            if(!Conf.DmNoteEffect) {
                rainManager?.Clear();
            }
            return;
        }

        if(!Conf.IsSimpleMode) {
            rainManager?.Clear();
            if(root.gameObject.activeSelf) {
                root.gameObject.SetActive(false);
            }
            return;
        }

        if(builtMode != KeyViewerSettings.ModeSimple || builtStyle != Mathf.Clamp(Conf.Style, 0, 3)) {
            Rebuild();
            return;
        }

        root.anchoredPosition = new Vector2(Conf.OffsetX, Conf.OffsetY);
        float size = Mathf.Clamp(Conf.Size, 0.2f, 4f);
        root.localScale = new Vector3(size, size, 1f);

        if(!Conf.RainEnabled) {
            rainManager?.Clear();
        }

        foreach(Box box in boxes) {
            ApplyBoxColors(box);
        }
    }

    private static float ColX(int column) => (KeyW + KeyGap) * column;

    private static float SpanW(int columns) => KeyW * columns + KeyGap * (columns - 1);

    internal readonly struct KeySlot(int slot, float x, float y, float w, float h) {
        public readonly int Slot = slot;
        public readonly float X = x, Y = y, W = w, H = h;
    }

    internal readonly struct StatSlot(bool total, float x, float y, float w, float h) {
        public readonly bool Total = total;
        public readonly float X = x, Y = y, W = w, H = h;
    }

    // Slot geometry per style, shared by the overlay and the settings-page
    // preview. v1 SimplePresets.BuildKey10/12/16/20.
    internal static void BuildLayout(int style, List<KeySlot> keys, List<StatSlot> stats) {
        // Front row: always the first 8 keys.
        for(int i = 0; i < 8; i++) {
            keys.Add(new KeySlot(i, ColX(i), 0f, KeyW, KeyH));
        }

        switch(style) {
            case 0:
                keys.Add(new KeySlot(8, ColX(2), RowGap, SpanW(2), KeyH));
                keys.Add(new KeySlot(9, ColX(4), RowGap, SpanW(2), KeyH));
                stats.Add(new StatSlot(false, ColX(0), RowGap, SpanW(2), KeyH));
                stats.Add(new StatSlot(true, ColX(6), RowGap, SpanW(2), KeyH));
                break;
            case 1:
                keys.Add(new KeySlot(9, ColX(2), RowGap, KeyW, KeyH));
                keys.Add(new KeySlot(8, ColX(3), RowGap, KeyW, KeyH));
                keys.Add(new KeySlot(10, ColX(4), RowGap, KeyW, KeyH));
                keys.Add(new KeySlot(11, ColX(5), RowGap, KeyW, KeyH));
                stats.Add(new StatSlot(false, ColX(0), RowGap, SpanW(2), KeyH));
                stats.Add(new StatSlot(true, ColX(6), RowGap, SpanW(2), KeyH));
                break;
            case 2:
                for(int i = 0; i < 8; i++) {
                    keys.Add(new KeySlot(BackSeq16[i], ColX(i), RowGap, KeyW, KeyH));
                }
                stats.Add(new StatSlot(false, ColX(0), RowGap * 2f, SpanW(4), 30f));
                stats.Add(new StatSlot(true, ColX(4), RowGap * 2f, SpanW(4), 30f));
                break;
            case 3:
                for(int i = 0; i < 8; i++) {
                    keys.Add(new KeySlot(BackSeq16[i], ColX(i), RowGap, KeyW, KeyH));
                }
                keys.Add(new KeySlot(17, ColX(2), RowGap * 2f, KeyW, KeyH));
                keys.Add(new KeySlot(16, ColX(3), RowGap * 2f, KeyW, KeyH));
                keys.Add(new KeySlot(18, ColX(4), RowGap * 2f, KeyW, KeyH));
                keys.Add(new KeySlot(19, ColX(5), RowGap * 2f, KeyW, KeyH));
                stats.Add(new StatSlot(false, ColX(0), RowGap * 2f, SpanW(2), KeyH));
                stats.Add(new StatSlot(true, ColX(6), RowGap * 2f, SpanW(2), KeyH));
                break;
        }
    }

    internal static Vector2 GridSize(int style) => new(SpanW(8), style switch {
        2 => RowGap * 2f + 30f,
        3 => RowGap * 2f + KeyH,
        _ => RowGap + KeyH,
    });

    private static void AddKey(int[] keys, int slot, float x, float y, float w, float h) {
        if(slot < 0 || slot >= keys.Length) {
            return;
        }

        KeyCode key = (KeyCode)keys[slot];
        Box box = NewBox("Key_" + slot, x, y, w, h);
        box.Key = key;
        box.Name = key.ToString().ToUpperInvariant();
        box.Count = Conf.GetCount(box.Name);

        // v1 SlotRainGroup: front row = group 1, the 20-key style's third
        // row = group 3, everything else = group 2.
        box.RainGroup = slot < 8 ? 1 : builtStyle == 3 && slot >= 16 ? 3 : 2;
        box.CenterX = x + w * 0.5f;
        box.BoxW = w;

        // Key label: centered, lifted off the counter strip at the bottom.
        box.Label = NewText(box.Fill.transform, "Label", LabelFor(builtStyle, slot), KeyFontSize);
        RectTransform labelRect = box.Label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(0f, 12f);
        labelRect.offsetMax = Vector2.zero;

        box.Value = NewText(box.Fill.transform, "Counter", "0", CounterFontSize);
        RectTransform counterRect = box.Value.rectTransform;
        counterRect.anchorMin = Vector2.zero;
        counterRect.anchorMax = new Vector2(1f, 0f);
        counterRect.pivot = new Vector2(0.5f, 0f);
        counterRect.anchoredPosition = new Vector2(0f, 3f);
        counterRect.sizeDelta = new Vector2(0f, 16f);

        boxes.Add(box);
    }

    private static void AddStat(bool total, float x, float y, float w, float h) {
        Box box = NewBox(total ? "Total" : "Kps", x, y, w, h);
        box.IsKps = !total;
        box.IsTotal = total;
        string caption = total ? "Total" : "KPS";

        // v1: tall stat boxes stack label over value, short ones go inline.
        bool stacked = h >= 40f;
        box.Label = NewText(box.Fill.transform, "Label", caption, StatFontSize);
        box.Value = NewText(box.Fill.transform, "Value", "0", StatFontSize);

        RectTransform labelRect = box.Label.rectTransform;
        RectTransform valueRect = box.Value.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        valueRect.anchorMin = Vector2.zero;
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;

        if(stacked) {
            labelRect.offsetMin = new Vector2(0f, h * 0.42f);
            labelRect.offsetMax = Vector2.zero;
            valueRect.offsetMax = new Vector2(0f, -(h * 0.42f));
        } else {
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = Vector2.zero;
            box.Label.alignment = TextAlignmentOptions.MidlineLeft;
            valueRect.offsetMax = new Vector2(-10f, 0f);
            box.Value.alignment = TextAlignmentOptions.MidlineRight;
        }

        boxes.Add(box);
    }

    private static void BuildDmNote() {
        builtMode = KeyViewerSettings.ModeDmNote;
        builtStyle = -1;

        GameObject rainObj = new("RainLayer");
        rainObj.transform.SetParent(root, false);
        RectTransform rainLayer = rainObj.AddComponent<RectTransform>();
        rainLayer.anchorMin = Vector2.zero;
        rainLayer.anchorMax = Vector2.one;
        rainLayer.offsetMin = Vector2.zero;
        rainLayer.offsetMax = Vector2.zero;
        rainManager?.SetLayer(rainLayer);

        List<DmNoteSpec> specs = ParseDmNoteSpecs();
        root.sizeDelta = new Vector2(dmCanvasWidth, dmCanvasHeight);

        for(int i = 0; i < specs.Count; i++) {
            AddDmNoteBox(i, specs[i]);
        }

        totalCount = 0;
        foreach(Box box in boxes) {
            if(!box.IsStat) {
                totalCount += box.Count;
            }
        }

        AddReorganizeHandle();

        Apply();
    }

    private static void AddReorganizeHandle() {
        GameObject drag = new("Drag");
        dragObj = drag;
        drag.transform.SetParent(root, false);
        RectTransform dragRect = drag.AddComponent<RectTransform>();
        dragRect.anchorMin = Vector2.zero;
        dragRect.anchorMax = Vector2.one;
        dragRect.offsetMin = Vector2.zero;
        dragRect.offsetMax = Vector2.zero;
        drag.AddComponent<EmptyGraphic>().raycastTarget = true;
        ReorganizeHandle handle = drag.AddComponent<ReorganizeHandle>();
        handle.Target = root;
        handle.GetName = () => MainCore.Tr.Get("KEYVIEWER_TITLE", "Key Viewer");
        handle.OnMoved = Save;
        drag.SetActive(false);
    }

    private static List<DmNoteSpec> ParseDmNoteSpecs() {
        List<DmNoteSpec> result = [];
        dmCanvasHeight = 250f;
        dmCanvasWidth = 800f;
        ApplyDmRuntimeSettings();

        if(string.IsNullOrWhiteSpace(Conf.DmPresetJson)) {
            return result;
        }

        try {
            JObject preset = JObject.Parse(Conf.DmPresetJson);

            JObject keysTable = preset["keys"] as JObject;
            JObject posTable = (preset["keyPositions"] as JObject) ?? (preset["positions"] as JObject);
            string tab = ResolveDmTab(preset, keysTable, posTable);
            Conf.DmSelectedTab = tab;
            ApplyDmRuntimeSettings();

            JArray keyArr = keysTable?[tab] as JArray;
            JArray posArr = posTable?[tab] as JArray;
            if(keyArr == null || posArr == null) {
                return result;
            }

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            int count = Mathf.Min(keyArr.Count, posArr.Count);
            for(int i = 0; i < count; i++) {
                if(posArr[i] is not JObject p || JBool(p, "hidden", false)) {
                    continue;
                }

                DmNoteSpec spec = ParseDmNoteSpec(keyArr[i]?.ToString() ?? "", p, false);
                result.Add(spec);
                ExtendDmBounds(spec, ref minX, ref minY, ref maxX, ref maxY);
            }

            if(preset["statPositions"] is JObject statTable && statTable[tab] is JArray statArr) {
                for(int i = 0; i < statArr.Count; i++) {
                    if(statArr[i] is not JObject p || JBool(p, "hidden", false)) {
                        continue;
                    }

                    JObject statPosition = (p["position"] as JObject) ?? p;
                    if(JBool(statPosition, "hidden", false)) {
                        continue;
                    }

                    DmNoteSpec spec = ParseDmNoteSpec(JStr(p, "statType", JStr(statPosition, "statType", "stat")), statPosition, true);
                    result.Add(spec);
                    ExtendDmBounds(spec, ref minX, ref minY, ref maxX, ref maxY);
                }
            }

            if(float.IsPositiveInfinity(minX) || float.IsPositiveInfinity(minY)) {
                return result;
            }

            const float padding = 30f;
            float track = Conf.DmNoteEffect ? dmTrackHeight : 0f;
            float topOffset = track + padding;
            float offsetX = padding - minX;
            float offsetY = topOffset - minY;

            for(int i = 0; i < result.Count; i++) {
                DmNoteSpec spec = result[i];
                spec.X += offsetX;
                spec.Y += offsetY;
                ResolveDmTrackGeometry(spec, topOffset);
            }

            dmCanvasWidth = Mathf.Max(60f, maxX - minX) + padding * 2f;
            dmCanvasHeight = Mathf.Max(60f, maxY - minY) + padding * 2f + track;
        } catch(Exception ex) {
            MainCore.Log.Msg("[KeyViewer] DM Note parse failed: " + ex.Message);
            result.Clear();
        }

        return result;
    }

    private static string ResolveDmTab(JObject preset, JObject keysTable, JObject posTable) {
        string selected = JOptionalString(preset, "selectedKeyType");
        if(!string.IsNullOrWhiteSpace(selected) && keysTable?[selected] != null && posTable?[selected] != null) {
            return selected;
        }

        string configured = Conf.DmSelectedTab;
        if(!string.IsNullOrWhiteSpace(configured) && keysTable?[configured] != null && posTable?[configured] != null) {
            return configured;
        }

        if(keysTable != null) {
            foreach(JProperty prop in keysTable.Properties()) {
                if(posTable?[prop.Name] != null) {
                    return prop.Name;
                }
            }
        }

        return string.IsNullOrWhiteSpace(configured) ? "4key" : configured;
    }

    private static void ApplyDmRuntimeSettings() {
        // KRP v2 kept these rain controls outside the imported DM Note preset.
        // Preset JSON still supplies key layout and colors, but local sliders
        // remain authoritative for movement, height, reverse, delay, and fade.
        dmNoteSpeed = Mathf.Clamp(Conf.DmNoteSpeed, 1f, 5000f);
        dmTrackHeight = Mathf.Clamp(Conf.DmTrackHeight, 0f, 5000f);
        dmNoteReverse = Conf.DmNoteReverse;
        dmFadePx = Mathf.Clamp(Conf.DmFadePx, 0f, 2000f);
        dmDelayedNoteEnabled = Conf.DmDelayedNoteEnabled;
        dmShortNoteThresholdMs = Mathf.Clamp(Conf.DmShortNoteThresholdMs, 0f, 2000f);
        dmShortNoteMinLengthPx = Mathf.Clamp(Conf.DmShortNoteMinLengthPx, 1f, 9999f);
        dmKeyDisplayDelayMs = Mathf.Clamp(Conf.DmKeyDisplayDelayMs, 0f, 9999f);
    }

    private static void ExtendDmBounds(DmNoteSpec spec, ref float minX, ref float minY, ref float maxX, ref float maxY) {
        minX = Mathf.Min(minX, spec.X);
        minY = Mathf.Min(minY, spec.Y);
        maxX = Mathf.Max(maxX, spec.X + spec.W);
        maxY = Mathf.Max(maxY, spec.Y + spec.H);

        if(spec.IsStat) {
            return;
        }

        float noteW = spec.NoteW > 0.5f ? spec.NoteW : spec.W;
        float align = DmNoteAlignOffset(spec.W, noteW, spec.NoteAlignment);
        minX = Mathf.Min(minX, spec.X + align + spec.NoteOffsetX);
        maxX = Mathf.Max(maxX, spec.X + align + spec.NoteOffsetX + noteW);
        if(spec.NoteOffsetY < 0f) {
            minY = Mathf.Min(minY, spec.Y + spec.NoteOffsetY);
        } else if(spec.NoteOffsetY > 0f) {
            maxY = Mathf.Max(maxY, spec.Y + spec.H + spec.NoteOffsetY);
        }
    }

    private static void ResolveDmTrackGeometry(DmNoteSpec spec, float topMostY) {
        if(spec.IsStat) {
            return;
        }

        spec.NoteW = spec.NoteW > 0.5f ? spec.NoteW : spec.W;
        float align = DmNoteAlignOffset(spec.W, spec.NoteW, spec.NoteAlignment);
        spec.TrackX = spec.X + align + spec.NoteOffsetX;
        spec.TrackBottomY = (spec.NoteAutoYCorrection ? topMostY : spec.Y) + spec.NoteOffsetY;
    }

    private static float DmNoteAlignOffset(float keyWidth, float noteWidth, string align) {
        if(string.Equals(align, "left", StringComparison.OrdinalIgnoreCase)) {
            return 0f;
        }
        if(string.Equals(align, "right", StringComparison.OrdinalIgnoreCase)) {
            return keyWidth - noteWidth;
        }
        return (keyWidth - noteWidth) * 0.5f;
    }

    private static void ResolveDmNoteColors(JObject p, bool glow, out Color top, out Color bottom) {
        string colorKey = glow ? "noteGlowColor" : "noteColor";
        string opacityKey = glow ? "noteGlowOpacity" : "noteOpacity";
        string opacityTopKey = glow ? "noteGlowOpacityTop" : "noteOpacityTop";
        string opacityBottomKey = glow ? "noteGlowOpacityBottom" : "noteOpacityBottom";

        float baseOpacity = JFloat(p, opacityKey, glow ? 70f : 80f);
        float opacityTop = Mathf.Clamp01(JFloat(p, opacityTopKey, baseOpacity) / 100f);
        float opacityBottom = Mathf.Clamp01(JFloat(p, opacityBottomKey, baseOpacity) / 100f);

        JToken color = p?[colorKey];
        if(color is JObject obj && string.Equals(JStr(obj, "type", ""), "gradient", StringComparison.OrdinalIgnoreCase)) {
            top = HexToColor(JStr(obj, "top", "#FFFFFF"), opacityTop);
            bottom = HexToColor(JStr(obj, "bottom", "#FFFFFF"), opacityBottom);
            return;
        }

        string solid = color == null || color.Type == JTokenType.Null ? "#FFFFFF" : color.ToString();
        top = HexToColor(solid, opacityTop);
        bottom = HexToColor(solid, opacityBottom);
    }

    private static DmNoteSpec ParseDmNoteSpec(string keyName, JObject p, bool stat) {
        string fontHex = JStr(p, "fontColor", "rgba(121, 121, 121, 0.9)");
        string activeFontHex = JStr(p, "activeFontColor", "#FFFFFF");
        string bgHex = JStr(p, "backgroundColor", "rgba(46, 46, 47, 0.9)");
        string activeBgHex = JStr(p, "activeBackgroundColor", "rgba(121, 121, 121, 0.9)");
        string borderHex = JStr(p, "borderColor", "rgba(113, 113, 113, 0.9)");
        string activeBorderHex = JStr(p, "activeBorderColor", "rgba(255, 255, 255, 0.9)");
        JObject counter = p["counter"] as JObject;
        JObject counterFill = counter?["fill"] as JObject;
        JObject counterStroke = counter?["stroke"] as JObject;

        DmNoteSpec spec = new() {
            KeyName = keyName ?? "",
            X = JFloat(p, "dx", 0f),
            Y = JFloat(p, "dy", 0f),
            W = Mathf.Max(1f, JFloat(p, "width", stat ? 100f : 60f)),
            H = Mathf.Max(1f, JFloat(p, "height", stat ? 30f : 60f)),
            IsStat = stat,
        };

        spec.KeyCode = stat ? KeyCode.None : ResolveDmNoteKeyCode(spec.KeyName);
        string ghost = JOptionalString(p, "ghostKey");
        spec.GhostKeyCode = string.IsNullOrEmpty(ghost) ? KeyCode.None : ResolveDmNoteKeyCode(ghost);
        spec.CountKey = JOptionalString(p, "countKey");
        if(string.IsNullOrEmpty(spec.CountKey)) {
            spec.CountKey = spec.KeyName;
        }

        spec.Bg = HexToColor(bgHex, 0.9f);
        spec.ActiveBg = HexToColor(activeBgHex, 0.9f);
        if(JBool(p, "idleTransparent", false)) {
            spec.Bg.a = 0f;
        }
        if(JBool(p, "activeTransparent", false)) {
            spec.ActiveBg.a = 0f;
        }

        spec.Outline = HexToColor(borderHex, 0.9f);
        spec.ActiveOutline = HexToColor(activeBorderHex, spec.Outline.a);
        spec.Text = HexToColor(fontHex, 1f);
        spec.ActiveText = HexToColor(activeFontHex, 1f);
        spec.BorderRadius = Mathf.Clamp(JFloat(p, "borderRadius", 10f), 0f, 100f);
        spec.BoxBorderWidth = Mathf.Clamp(JFloat(p, "borderWidth", 3f), 0f, 20f);
        if(spec.BoxBorderWidth <= 0.01f) {
            spec.Outline.a = 0f;
            spec.ActiveOutline.a = 0f;
        }

        ResolveDmNoteColors(p, false, out spec.RainTop, out spec.RainBottom);
        spec.Rain = spec.RainBottom;
        string ghostNoteHex = JOptionalString(p, "ghostNoteColor");
        if(string.IsNullOrEmpty(ghostNoteHex)) {
            spec.GhostRainTop = new Color(spec.RainTop.r, spec.RainTop.g, spec.RainTop.b, spec.RainTop.a * 0.45f);
            spec.GhostRainBottom = new Color(spec.RainBottom.r, spec.RainBottom.g, spec.RainBottom.b, spec.RainBottom.a * 0.45f);
        } else {
            Color ghostColor = HexToColor(ghostNoteHex, JFloat(p, "ghostNoteOpacity", 45f) / 100f);
            spec.GhostRainTop = ghostColor;
            spec.GhostRainBottom = ghostColor;
        }
        spec.GhostRain = spec.GhostRainBottom;

        spec.FontSize = JInt(p, "fontSize", stat ? 16 : 18);
        spec.CounterEnabled = Conf.DmShowCounter && (counter != null ? JBool(counter, "enabled", true) : true);
        spec.CounterFontSize = counter != null
            ? JInt(counter, "fontSize", 16)
            : 16;
        spec.CounterAlign = counter != null ? JStr(counter, "align", "top") : "top";
        spec.CounterAlignMode = counter != null ? JStr(counter, "alignMode", "center") : "center";
        spec.CounterGap = counter != null ? JFloat(counter, "gap", 6f) : 6f;
        spec.CounterOutside = counter != null && string.Equals(JStr(counter, "placement", "inside"), "outside", StringComparison.OrdinalIgnoreCase);
        string counterIdle = counterFill != null ? JStr(counterFill, "idle", fontHex) : fontHex;
        string counterActive = counterFill != null ? JStr(counterFill, "active", activeFontHex) : activeFontHex;
        spec.CounterText = HexToColor(counterIdle, 1f);
        spec.ActiveCounterText = HexToColor(counterActive, 1f);
        spec.CounterStroke = HexToColor(counterStroke != null ? JStr(counterStroke, "idle", "transparent") : "transparent", 0f);
        spec.ActiveCounterStroke = HexToColor(counterStroke != null ? JStr(counterStroke, "active", "transparent") : "transparent", 0f);
        spec.NoteEnabled = JBool(p, "noteEffectEnabled", true);
        spec.NoteW = JFloat(p, "noteWidth", spec.W);
        spec.NoteAlignment = JStr(p, "noteAlignment", "center");
        spec.NoteOffsetX = Mathf.Clamp(JFloat(p, "noteOffsetX", 0f), -500f, 500f);
        spec.NoteOffsetY = Mathf.Clamp(JFloat(p, "noteOffsetY", 0f), -500f, 500f);
        spec.NoteAutoYCorrection = JBool(p, "noteAutoYCorrection", true);
        spec.IsKps = stat && spec.KeyName.Equals("kps", StringComparison.OrdinalIgnoreCase);
        spec.IsKpsAvg = stat && spec.KeyName.Equals("kpsAvg", StringComparison.OrdinalIgnoreCase);
        spec.IsKpsMax = stat && spec.KeyName.Equals("kpsMax", StringComparison.OrdinalIgnoreCase);
        spec.IsTotal = stat && spec.KeyName.Equals("total", StringComparison.OrdinalIgnoreCase);
        spec.InlineStatCounter = stat && spec.CounterEnabled
            && !spec.CounterOutside
            && string.Equals(spec.CounterAlignMode, "center", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(spec.CounterAlign, "top", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(spec.CounterAlign, "bottom", StringComparison.OrdinalIgnoreCase);
        string display = JOptionalString(p, "displayText");
        spec.DisplayText = !string.IsNullOrEmpty(display)
            ? display
            : DefaultDmNoteDisplay(spec.KeyName, stat);

        return spec;
    }

    private static void AddDmNoteBox(int index, DmNoteSpec spec) {
        (Image fill, Image border) = NewBoxVisual(
            "DmNote_" + index, root, spec.X, spec.Y, spec.W, spec.H,
            spec.BorderRadius, spec.BoxBorderWidth
        );

        Box box = new() {
            Key = spec.KeyCode,
            Name = spec.CountKey,
            Fill = fill,
            Border = border,
            Dm = spec,
            IsKps = spec.IsKps,
            IsKpsAvg = spec.IsKpsAvg,
            IsKpsMax = spec.IsKpsMax,
            IsTotal = spec.IsTotal,
            Count = spec.IsStat ? 0 : Conf.GetCount(spec.CountKey),
            RainGroup = 1,
            CenterX = spec.TrackX + spec.NoteW * 0.5f,
            BoxW = spec.NoteW,
        };

        box.Label = NewText(fill.transform, "Label", spec.DisplayText, spec.FontSize);
        box.Label.enableAutoSizing = true;
        box.Label.fontSizeMin = 0f;
        box.Label.fontSizeMax = Mathf.Max(8, spec.FontSize);

        if(spec.InlineStatCounter) {
            box.Label.text = DmInlineStatText(spec, spec.IsTotal ? totalCount : 0);
            LayoutDmText(box.Label.rectTransform, spec, false);
            box.Label.alignment = TextAlignmentOptions.Center;
        } else {
            LayoutDmText(box.Label.rectTransform, spec, false);
            box.Label.alignment = DmCounterAlignment(spec, false);
        }

        if(spec.CounterEnabled && !spec.InlineStatCounter) {
            Transform counterParent = spec.CounterOutside ? root : fill.transform;
            box.Value = NewText(counterParent, "Counter", "0", spec.CounterFontSize);
            box.Value.enableAutoSizing = true;
            box.Value.fontSizeMin = 0f;
            box.Value.fontSizeMax = Mathf.Max(8, spec.CounterFontSize);
            if(spec.CounterOutside) {
                LayoutDmOutsideCounter(box.Value.rectTransform, spec);
                box.Value.alignment = TextAlignmentOptions.Center;
            } else {
                LayoutDmText(box.Value.rectTransform, spec, true);
                box.Value.alignment = DmCounterAlignment(spec, true);
            }
        }

        boxes.Add(box);
        ApplyBoxColors(box);
    }

    private static void LayoutDmText(RectTransform rt, DmNoteSpec spec, bool counter) {
        bool top = string.Equals(spec.CounterAlign, "top", StringComparison.OrdinalIgnoreCase);
        bool bottom = string.Equals(spec.CounterAlign, "bottom", StringComparison.OrdinalIgnoreCase);
        bool left = string.Equals(spec.CounterAlign, "left", StringComparison.OrdinalIgnoreCase);
        bool right = string.Equals(spec.CounterAlign, "right", StringComparison.OrdinalIgnoreCase);
        bool between = string.Equals(spec.CounterAlignMode, "between", StringComparison.OrdinalIgnoreCase);
        float gap = Mathf.Clamp(spec.CounterGap, -64f, 64f);

        if(!spec.CounterEnabled || spec.CounterOutside || spec.InlineStatCounter || string.IsNullOrWhiteSpace(spec.DisplayText)) {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(spec.W - 4f, spec.H - 4f);
            return;
        }

        if(top || bottom) {
            float itemGap = between ? 0f : Mathf.Max(0f, gap);
            float avail = Mathf.Max(1f, spec.H - 4f);
            float labelH = Mathf.Clamp(spec.FontSize + 8f, 1f, avail);
            float counterH = Mathf.Clamp(spec.CounterFontSize + 8f, 1f, avail);
            if(labelH + counterH + itemGap > avail) {
                float k = Mathf.Max(1f, avail - itemGap) / (labelH + counterH);
                labelH *= k;
                counterH *= k;
            }

            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            float groupH = Mathf.Min(avail, labelH + counterH + itemGap);
            float y0 = Mathf.Max(2f, (spec.H - groupH) * 0.5f);
            rt.anchoredPosition = counter == bottom
                ? new Vector2(2f, y0)
                : new Vector2(2f, y0 + counterH + itemGap);
            if(top) {
                rt.anchoredPosition = counter
                    ? new Vector2(2f, y0 + labelH + itemGap)
                    : new Vector2(2f, y0);
            }
            rt.sizeDelta = new Vector2(spec.W - 4f, counter ? counterH : labelH);
            return;
        }

        if(left || right) {
            float itemGap = between ? 0f : Mathf.Max(0f, gap);
            float availW = Mathf.Max(1f, spec.W - 4f);
            float labelW = Mathf.Clamp(spec.FontSize * Mathf.Max(1f, (spec.DisplayText ?? "").Length) * 0.58f + 4f, 1f, availW);
            float counterW = Mathf.Clamp(spec.CounterFontSize * 3f, 1f, availW);
            if(labelW + counterW + itemGap > availW) {
                float k = Mathf.Max(1f, availW - itemGap) / (labelW + counterW);
                labelW *= k;
                counterW *= k;
            }

            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            float groupW = Mathf.Min(availW, labelW + counterW + itemGap);
            float x0 = Mathf.Max(2f, (spec.W - groupW) * 0.5f);
            rt.anchoredPosition = counter == left
                ? new Vector2(x0, 2f)
                : new Vector2(x0 + counterW + itemGap, 2f);
            if(right) {
                rt.anchoredPosition = counter
                    ? new Vector2(x0 + labelW + itemGap, 2f)
                    : new Vector2(x0, 2f);
            }
            rt.sizeDelta = new Vector2(counter ? counterW : labelW, spec.H - 4f);
            return;
        }

        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(spec.W - 4f, spec.H - 4f);
    }

    private static void LayoutDmOutsideCounter(RectTransform rt, DmNoteSpec spec) {
        string align = spec.CounterAlign;
        float gap = Mathf.Max(0f, spec.CounterGap);
        float w = Mathf.Max(spec.W, spec.CounterFontSize * 4f);
        float h = Mathf.Max(12f, spec.CounterFontSize + 8f);

        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        if(string.Equals(align, "bottom", StringComparison.OrdinalIgnoreCase)) {
            rt.anchoredPosition = new Vector2(spec.X + spec.W * 0.5f - w * 0.5f, -(spec.Y + spec.H + gap));
            rt.sizeDelta = new Vector2(w, h);
            return;
        }
        if(string.Equals(align, "left", StringComparison.OrdinalIgnoreCase)) {
            rt.anchoredPosition = new Vector2(spec.X - gap - w, -(spec.Y + spec.H * 0.5f - h * 0.5f));
            rt.sizeDelta = new Vector2(w, h);
            return;
        }
        if(string.Equals(align, "right", StringComparison.OrdinalIgnoreCase)) {
            rt.anchoredPosition = new Vector2(spec.X + spec.W + gap, -(spec.Y + spec.H * 0.5f - h * 0.5f));
            rt.sizeDelta = new Vector2(w, h);
            return;
        }

        rt.anchoredPosition = new Vector2(spec.X + spec.W * 0.5f - w * 0.5f, -(spec.Y - gap - h));
        rt.sizeDelta = new Vector2(w, h);
    }

    private static TextAlignmentOptions DmCounterAlignment(DmNoteSpec spec, bool counter) {
        string align = spec.CounterAlign;
        bool between = string.Equals(spec.CounterAlignMode, "between", StringComparison.OrdinalIgnoreCase);
        if(!between && (string.Equals(align, "top", StringComparison.OrdinalIgnoreCase)
            || string.Equals(align, "bottom", StringComparison.OrdinalIgnoreCase))) {
            return TextAlignmentOptions.Center;
        }
        if(string.Equals(align, "top", StringComparison.OrdinalIgnoreCase)) {
            return counter ? TextAlignmentOptions.Bottom : TextAlignmentOptions.Top;
        }
        if(string.Equals(align, "bottom", StringComparison.OrdinalIgnoreCase)) {
            return counter ? TextAlignmentOptions.Top : TextAlignmentOptions.Bottom;
        }
        if(!between && (string.Equals(align, "left", StringComparison.OrdinalIgnoreCase)
            || string.Equals(align, "right", StringComparison.OrdinalIgnoreCase))) {
            return TextAlignmentOptions.Center;
        }
        if(string.Equals(align, "left", StringComparison.OrdinalIgnoreCase)) {
            return counter ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
        }
        if(string.Equals(align, "right", StringComparison.OrdinalIgnoreCase)) {
            return counter ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        }
        return TextAlignmentOptions.Center;
    }

    // Box visuals: a rounded fill with an analytic ring drawn ON TOP of it —
    // a full border-colored rect behind a translucent fill would tint the
    // whole box with the outline color.
    internal static (Image fill, Image border) NewBoxVisual(
        string name, Transform parent, float x, float y, float w, float h,
        float radius = KeyRadius, float borderWidth = BorderWidth
    ) {
        GameObject obj = new(name);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);

        Image fill = obj.AddComponent<Image>();
        fill.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P1024);
        fill.type = Image.Type.Sliced;
        fill.pixelsPerUnitMultiplier = 8f / Mathf.Max(0.5f, radius);
        fill.raycastTarget = false;

        GameObject borderObj = new("Border");
        borderObj.transform.SetParent(obj.transform, false);

        RectTransform borderRect = borderObj.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;

        Image border = borderObj.AddComponent<Image>();
        border.sprite = MainCore.Spr.GetRing(Mathf.Max(0.5f, radius), Mathf.Max(0.1f, borderWidth));
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;

        return (fill, border);
    }

    private static Box NewBox(string name, float x, float y, float w, float h) {
        (Image fill, Image border) = NewBoxVisual(name, root, x, y, w, h);
        return new Box { Border = border, Fill = fill };
    }

    // Caption for a key slot: the user override if set, else derived from the
    // bound key code. Shared with the settings-page preview.
    internal static string LabelFor(int style, int slot) {
        string[] overrides = Conf.LabelsForStyle(style);
        if(slot >= 0 && slot < overrides.Length && !string.IsNullOrEmpty(overrides[slot])) {
            return overrides[slot];
        }

        int[] keys = Conf.KeysForStyle(style);
        return slot >= 0 && slot < keys.Length ? KeyCodeShortLabel((KeyCode)keys[slot]) : "";
    }

    internal static TextMeshProUGUI NewText(Transform parent, string name, string text, float fontSize) {
        GameObject obj = new(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.font = FontManager.Current;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        tmp.text = text;
        return tmp;
    }

    private static void ApplyBoxColors(Box box) {
        if(box.Dm != null) {
            bool dmPressed = box.Pressed;
            DmNoteSpec spec = box.Dm;
            box.Border.color = dmPressed ? spec.ActiveOutline : spec.Outline;
            box.Fill.color = dmPressed ? spec.ActiveBg : spec.Bg;

            if(box.Label != null) {
                box.Label.color = dmPressed ? spec.ActiveText : spec.Text;
            }
            if(box.Value != null) {
                box.Value.color = dmPressed ? spec.ActiveCounterText : spec.CounterText;
            }
            return;
        }

        bool pressed = box.Pressed;
        box.Border.color = pressed ? Conf.GetOutlinePressed() : Conf.GetOutline();
        box.Fill.color = pressed ? Conf.GetBgPressed() : Conf.GetBg();

        Color text = pressed ? Conf.GetTextPressed() : Conf.GetText();
        if(box.Label != null) {
            box.Label.color = text;
        }
        if(box.Value != null) {
            box.Value.color = text;
        }
    }

    // v1 SimplePresets.KeyCodeShortLabel: compact key captions.
    internal static string KeyCodeShortLabel(KeyCode kc) {
        string s = kc.ToString();
        if(s.StartsWith("Alpha")) s = s[5..];
        if(s.StartsWith("Keypad")) s = "N" + s[6..];
        if(s.StartsWith("Left")) s = "L" + s[4..];
        if(s.StartsWith("Right")) s = "R" + s[5..];
        if(s.EndsWith("Shift")) s = s[..^5] + "⇧";
        if(s.EndsWith("Control")) s = s[..^7] + "Ctrl";
        return s switch {
            "NDivide" => "/",
            "PageUp" => "PgUp",
            "PageDown" => "PgDn",
            "Insert" => "Ins",
            "Delete" => "Del",
            "Numlock" => "NmLk",
            "ScrollLock" => "ScLk",
            "Print" or "SysReq" => "PrtSc",
            "Break" => "Brk",
            "Plus" => "+",
            "Minus" => "-",
            "Multiply" => "*",
            "Divide" => "/",
            "Enter" or "Return" => "↵",
            "Equals" => "=",
            "Period" => ".",
            "Comma" => ",",
            "Tab" => "⇥",
            "Space" => "␣",
            "Backslash" => "\\",
            "Slash" => "/",
            "Semicolon" => ";",
            "Quote" => "'",
            "BackQuote" => "`",
            "CapsLock" => "⇪",
            "Backspace" => "Back",
            "UpArrow" => "↑",
            "DownArrow" => "↓",
            "LeftArrow" => "←",
            "RightArrow" => "→",
            "LBracket" or "LeftBracket" => "[",
            "RBracket" or "RightBracket" => "]",
            "None" => "",
            _ => s,
        };
    }

    private static string DmInlineStatText(DmNoteSpec spec, int value)
        => (spec.DisplayText ?? "") + "  " + value.ToString(CultureInfo.InvariantCulture);

    private static string DefaultDmNoteDisplay(string keyName, bool stat) {
        if(stat) {
            if(keyName.Equals("kps", StringComparison.OrdinalIgnoreCase)) {
                return "KPS";
            }
            if(keyName.Equals("kpsAvg", StringComparison.OrdinalIgnoreCase)) {
                return "AVG";
            }
            if(keyName.Equals("kpsMax", StringComparison.OrdinalIgnoreCase)) {
                return "MAX";
            }
            if(keyName.Equals("total", StringComparison.OrdinalIgnoreCase)) {
                return "Total";
            }
            return keyName.ToUpperInvariant();
        }

        if(string.IsNullOrEmpty(keyName)) {
            return "";
        }

        KeyCode key = ResolveDmNoteKeyCode(keyName);
        return key == KeyCode.None ? keyName : KeyCodeShortLabel(key);
    }

    private static KeyCode ResolveDmNoteKeyCode(string name) {
        if(string.IsNullOrEmpty(name)) {
            return KeyCode.None;
        }

        if(name.Length > 1 && int.TryParse(name, out int numeric)) {
            return Features.KeyLimiter.KeyLimiter.NormalizeKey((KeyCode)numeric);
        }

        string normalized = name.Replace(" ", "").Replace("_", "").Replace("-", "");
        if(normalized.StartsWith("KEY", StringComparison.OrdinalIgnoreCase) && normalized.Length == 4) {
            normalized = normalized[3..];
        }
        if(normalized.StartsWith("DIGIT", StringComparison.OrdinalIgnoreCase) && normalized.Length == 6) {
            normalized = normalized[5..];
        }
        // Enum.TryParse accepts numeric strings as raw enum values, so "3"
        // would become the undefined (KeyCode)3 instead of Alpha3 — digit
        // names must fall through to the single-char mapping below.
        if(!char.IsDigit(normalized[0]) && Enum.TryParse(normalized, true, out KeyCode parsed)) {
            return Features.KeyLimiter.KeyLimiter.NormalizeKey(parsed);
        }

        switch(normalized.ToUpperInvariant()) {
            case "DOT":
            case "PERIOD": return KeyCode.Period;
            case "ENTER": return KeyCode.Return;
            case "ESC": return KeyCode.Escape;
            case "LEFTSHIFT": return KeyCode.LeftShift;
            case "RIGHTSHIFT": return KeyCode.RightShift;
            case "LCONTROL":
            case "LEFTCONTROL":
            case "LCTRL": return KeyCode.LeftControl;
            case "RCONTROL":
            case "RIGHTCONTROL":
            case "RCTRL":
            case "HANJA": return KeyCode.RightControl;
            case "RALT":
            case "RIGHTALT":
            case "ALTGR":
            case "HANGUL": return KeyCode.RightAlt;
            case "LALT":
            case "LEFTALT": return KeyCode.LeftAlt;
            case "INS": return KeyCode.Insert;
            case "CONTEXTMENU": return KeyCode.Menu;
            case "BACKSLASH": return KeyCode.Backslash;
            case "SLASH": return KeyCode.Slash;
            case "FORWARDSLASH": return KeyCode.Slash;
            case "CAPSLOCK": return KeyCode.CapsLock;
            case "SPACE": return KeyCode.Space;
            case "SECTION": return KeyCode.BackQuote;
            case "COMMA": return KeyCode.Comma;
            case "PLUS": return KeyCode.Plus;
            case "MINUS": return KeyCode.Minus;
            case "EQUAL":
            case "EQUALS": return KeyCode.Equals;
            case "SEMICOLON": return KeyCode.Semicolon;
            case "QUOTE": return KeyCode.Quote;
            case "BACKQUOTE":
            case "BACKTICK": return KeyCode.BackQuote;
            case "LEFTBRACKET":
            case "LBRACKET": return KeyCode.LeftBracket;
            case "RIGHTBRACKET":
            case "RBRACKET": return KeyCode.RightBracket;
            case "UP":
            case "UPARROW": return KeyCode.UpArrow;
            case "DOWN":
            case "DOWNARROW": return KeyCode.DownArrow;
            case "LEFT":
            case "LEFTARROW": return KeyCode.LeftArrow;
            case "RIGHT":
            case "RIGHTARROW": return KeyCode.RightArrow;
        }

        if(normalized.Length == 1) {
            char c = char.ToUpperInvariant(normalized[0]);
            if(c >= 'A' && c <= 'Z') {
                return (KeyCode)((int)KeyCode.A + (c - 'A'));
            }
            if(c >= '0' && c <= '9') {
                return (KeyCode)((int)KeyCode.Alpha0 + (c - '0'));
            }
        }

        return KeyCode.None;
    }

    private static Color HexToColor(string hex, float alpha) {
        if(string.IsNullOrEmpty(hex)) {
            return new Color(1f, 1f, 1f, alpha);
        }

        string s = hex.Trim();
        try {
            if(string.Equals(s, "transparent", StringComparison.OrdinalIgnoreCase)) {
                return new Color(0f, 0f, 0f, 0f);
            }

            if(s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) {
                int lp = s.IndexOf('(');
                int rp = s.IndexOf(')');
                if(lp > 0 && rp > lp) {
                    string inner = s[(lp + 1)..rp];
                    string[] parts = inner.Split(new[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if(parts.Length >= 3) {
                        float r = ParseColorComponent(parts[0], 255f);
                        float g = ParseColorComponent(parts[1], 255f);
                        float b = ParseColorComponent(parts[2], 255f);
                        float a = parts.Length >= 4 ? ParseAlphaComponent(parts[3]) : alpha;
                        return new Color(r, g, b, a);
                    }
                }
            }

            string h = s.TrimStart('#');
            if(h.Length == 3 || h.Length == 4) {
                int r = Convert.ToInt32(new string(h[0], 2), 16);
                int g = Convert.ToInt32(new string(h[1], 2), 16);
                int b = Convert.ToInt32(new string(h[2], 2), 16);
                int a = h.Length == 4 ? Convert.ToInt32(new string(h[3], 2), 16) : Mathf.RoundToInt(alpha * 255f);
                return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            }
            if(h.Length == 6 || h.Length == 8) {
                int r = Convert.ToInt32(h[..2], 16);
                int g = Convert.ToInt32(h[2..4], 16);
                int b = Convert.ToInt32(h[4..6], 16);
                int a = h.Length == 8 ? Convert.ToInt32(h[6..8], 16) : Mathf.RoundToInt(alpha * 255f);
                return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            }
        } catch {
        }

        return new Color(1f, 1f, 1f, alpha);
    }

    private static float ParseColorComponent(string s, float scale) {
        string t = s.Trim();
        if(t.EndsWith("%")) {
            return float.TryParse(t.TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct)
                ? Mathf.Clamp01(pct / 100f)
                : 1f;
        }
        return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            ? Mathf.Clamp01(v / scale)
            : 1f;
    }

    private static float ParseAlphaComponent(string s) {
        string t = s.Trim();
        if(t.EndsWith("%")) {
            return float.TryParse(t.TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct)
                ? Mathf.Clamp01(pct / 100f)
                : 1f;
        }
        return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            ? Mathf.Clamp01(v <= 1f ? v : v / 255f)
            : 1f;
    }

    private static string JStr(JObject p, string key, string def) {
        JToken t = p?[key];
        return t == null || t.Type == JTokenType.Null ? def : t.ToString();
    }

    private static string JOptionalString(JObject p, string key) {
        JToken t = p?[key];
        return t == null || t.Type == JTokenType.Null ? null : t.ToString();
    }

    private static float JFloat(JObject p, string key, float def) {
        JToken t = p?[key];
        if(t == null || t.Type == JTokenType.Null) {
            return def;
        }
        try { return t.ToObject<float>(); } catch { return def; }
    }

    private static int JInt(JObject p, string key, int def) {
        JToken t = p?[key];
        if(t == null || t.Type == JTokenType.Null) {
            return def;
        }
        try { return t.ToObject<int>(); } catch { return def; }
    }

    private static bool JBool(JObject p, string key, bool def) {
        JToken t = p?[key];
        if(t == null || t.Type == JTokenType.Null) {
            return def;
        }
        try { return t.ToObject<bool>(); } catch { return def; }
    }

    public static void ResetPosition() {
        KeyViewerSettings def = new();
        if(Conf.IsDmNoteMode) {
            Conf.DmOffsetX = def.DmOffsetX;
            Conf.DmOffsetY = def.DmOffsetY;
        } else {
            Conf.OffsetX = def.OffsetX;
            Conf.OffsetY = def.OffsetY;
        }
        Apply();
        Save();
    }

    public static bool ImportDmNotePreset(out string error) {
        error = null;
        string picked;

        try {
            picked = UnityFileDialog.FileBrowser.PickFile(
                "", "JSON Preset", new[] { "json" }, "Select DM Note preset");
        } catch(Exception ex) {
            error = "Picker failed: " + ex.Message;
            MainCore.Log.Msg("[KeyViewer] " + error);
            return false;
        }

        if(string.IsNullOrEmpty(picked)) {
            return false;
        }

        try {
            string text = File.ReadAllText(picked);
            JObject.Parse(text);
            Conf.DmPresetJson = text;
            Rebuild();
            Save();
            MainCore.Log.Msg("[KeyViewer] Imported DM Note preset from " + picked);
            return true;
        } catch(Exception ex) {
            error = "Import failed: " + ex.Message;
            MainCore.Log.Msg("[KeyViewer] " + error);
            return false;
        }
    }

    public static void ResetCounts() {
        Conf.Counts.Clear();
        pressLog.Clear();
        kpsMax = 0;
        kpsSum = 0;
        kpsSamples = 0;
        totalCount = 0;
        foreach(Box box in boxes) {
            box.Count = 0;
            box.LastShown = int.MinValue;
        }
        Save();
    }

    private static void FlushCounts() {
        if(!countsDirty) {
            return;
        }

        countsDirty = false;
        foreach(Box box in boxes) {
            if(!box.IsStat) {
                Conf.SetCount(box.Name, box.Count);
            }
        }
        Save();
    }

    public static void Dispose() {
        if(canvasObj == null) {
            return;
        }

        FlushCounts();
        ConfMgr?.Save();

        Object.Destroy(canvasObj);
        canvasObj = null;
        root = null;
        dragObj = null;
        rainManager = null;
        boxes.Clear();
        pressLog.Clear();
        kpsMax = 0;
        kpsSum = 0;
        kpsSamples = 0;
        builtStyle = -1;
    }

    // ===== rain (port of v1's KvRain* — pooled drops, one manager Update,
    // custom graphic with vertex-alpha fade; allocation only on key press) =====

    private static RawRain SpawnRain(Box box, float now) {
        bool frontRow = box.RainGroup == 1;
        float width = frontRow ? Conf.RainWidth : Conf.Rain2Width;
        if(width <= 0.5f) {
            width = box.BoxW;
        }

        RawRain raw = new() {
            Group = box.RainGroup,
            StartTime = now,
            AnchorX = box.CenterX,
            Width = width,
            // Offset moves the base line; rain rises from the grid top.
            BaseY = -(frontRow ? Conf.RainOffsetY : Conf.Rain2OffsetY),
            TrackHeight = Mathf.Max(1f, Conf.RainHeight),
            Speed = Mathf.Max(1f, Conf.RainSpeed),
            FadePx = Mathf.Max(0f, Conf.RainFade),
            Color = box.RainGroup switch {
                1 => Conf.GetRain(),
                3 => Conf.GetRain3(),
                _ => Conf.GetRain2(),
            },
        };
        raw.ColorTop = raw.Color;
        raw.ColorBottom = raw.Color;
        rainManager.Enqueue(raw);
        return raw;
    }

    private static RawRain SpawnDmRain(Box box, float now, bool ghost) {
        DmNoteSpec spec = box.Dm;
        if(spec == null) {
            return null;
        }

        RawRain raw = new() {
            Group = 1,
            StartTime = now,
            AnchorX = box.CenterX,
            Width = box.BoxW,
            BaseY = -spec.TrackBottomY,
            TrackHeight = Mathf.Max(1f, dmTrackHeight),
            Speed = Mathf.Max(1f, dmNoteSpeed),
            FadePx = Mathf.Max(0f, dmFadePx),
            Reverse = dmNoteReverse,
            Color = ghost ? spec.GhostRain : spec.Rain,
            ColorTop = ghost ? spec.GhostRainTop : spec.RainTop,
            ColorBottom = ghost ? spec.GhostRainBottom : spec.RainBottom,
        };
        rainManager.Enqueue(raw);
        return raw;
    }

    // Spawn-time parameters of one drop. The drop stretches between a leading
    // edge ((now - start) * speed) and a trailing edge that starts moving on
    // release — same scheme as v1.
    internal sealed class RawRain {
        public int Group;
        public float StartTime;
        public float EndTime = -1f;
        public float AnchorX;
        public float Width;
        public float BaseY;
        public float TrackHeight;
        public float Speed;
        public float FadePx;
        public bool Reverse;
        public Color Color;
        public Color ColorTop;
        public Color ColorBottom;
    }

    // One quad (two when the fade boundary crosses it) with KRP v2's
    // vertex-alpha fade over the last FadePx units of the track.
    private sealed class RainGraphic : MaskableGraphic {
        private float dNear;
        private float dFar;
        private float trackHeight;
        private float fadePx;
        private bool reverseFade;
        private Color colorTop = Color.white;
        private Color colorBottom = Color.white;

        public void SetFadeParams(float near, float far, float track, float fade, bool reverse, Color top, Color bottom) {
            bool changed = dNear != near || dFar != far || trackHeight != track
                || fadePx != fade || reverseFade != reverse
                || colorTop != top || colorBottom != bottom;
            dNear = near;
            dFar = far;
            trackHeight = track;
            fadePx = fade;
            reverseFade = reverse;
            colorTop = top;
            colorBottom = bottom;
            if(changed) {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            Rect r = rectTransform.rect;
            if(r.width <= 0f || r.height <= 0f) {
                return;
            }

            float fade = fadePx;
            float trackH = trackHeight;
            float span = dFar - dNear;

            if(fade <= 0.5f || trackH <= 0.5f || span <= 0.0001f) {
                Color colNearFull = ColorAtD(dNear, 1f);
                Color colFarFull = ColorAtD(dFar, 1f);
                if(reverseFade) {
                    AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMax, colFarFull, colNearFull);
                } else {
                    AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMax, colNearFull, colFarFull);
                }
                return;
            }

            float fadeStartD = trackH - fade;
            float aNear = AlphaAtD(dNear, fadeStartD, trackH, fade);
            float aFar = AlphaAtD(dFar, fadeStartD, trackH, fade);
            Color colNear = ColorAtD(dNear, aNear);
            Color colFar = ColorAtD(dFar, aFar);

            bool crosses = dNear < fadeStartD && dFar > fadeStartD;
            if(!crosses) {
                if(reverseFade) {
                    AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMax, colFar, colNear);
                } else {
                    AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMax, colNear, colFar);
                }
                return;
            }

            float t = (fadeStartD - dNear) / span;
            Color full = ColorAtD(fadeStartD, 1f);
            if(reverseFade) {
                float yMid = r.yMax - t * r.height;
                AddQuad(vh, r.xMin, yMid, r.xMax, r.yMax, full, colNear);
                AddQuad(vh, r.xMin, r.yMin, r.xMax, yMid, colFar, full);
            } else {
                float yMid = r.yMin + t * r.height;
                AddQuad(vh, r.xMin, r.yMin, r.xMax, yMid, colNear, full);
                AddQuad(vh, r.xMin, yMid, r.xMax, r.yMax, full, colFar);
            }
        }

        private Color ColorAtD(float d, float alphaMul) {
            float t = trackHeight <= 0.0001f ? 0f : Mathf.Clamp01(d / trackHeight);
            Color c = Color.Lerp(colorBottom, colorTop, t);
            c.a *= alphaMul;
            return c;
        }

        private static float AlphaAtD(float d, float fadeStartD, float trackH, float fade) {
            if(d <= fadeStartD) {
                return 1f;
            }
            if(d >= trackH) {
                return 0f;
            }
            return (trackH - d) / fade;
        }

        private static void AddQuad(VertexHelper vh, float xL, float yB, float xR, float yT, Color bot, Color top) {
            int i = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.position = new Vector3(xL, yB, 0f); v.color = bot; vh.AddVert(v);
            v.position = new Vector3(xR, yB, 0f); v.color = bot; vh.AddVert(v);
            v.position = new Vector3(xR, yT, 0f); v.color = top; vh.AddVert(v);
            v.position = new Vector3(xL, yT, 0f); v.color = top; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }

    private sealed class RainDrop {
        public readonly GameObject Obj;
        public readonly RectTransform Rect;
        public readonly RainGraphic Graphic;
        public RawRain Raw;

        public RainDrop(RectTransform layer) {
            Obj = new GameObject("Rain", typeof(RectTransform));
            Obj.transform.SetParent(layer, false);
            Rect = (RectTransform)Obj.transform;
            Rect.anchorMin = new Vector2(0f, 1f);
            Rect.anchorMax = new Vector2(0f, 1f);
            // Bottom-center pivot: the drop grows upward from the base line.
            Rect.pivot = new Vector2(0.5f, 0f);
            Rect.anchoredPosition = Vector2.zero;
            Rect.sizeDelta = Vector2.zero;

            Graphic = Obj.AddComponent<RainGraphic>();
            Graphic.raycastTarget = false;
            Graphic.color = Color.clear;
        }
    }

    // Single shared pool + one Update for every drop. Spawning dequeues from
    // the pool (or builds a new drop when dry); finished drops go back.
    // Drops are parented into per-row sub-layers so later rows always render
    // above earlier ones, regardless of press order.
    private sealed class RainManager : MonoBehaviour {
        private readonly RectTransform[] rowLayers = new RectTransform[3];
        private readonly List<RainDrop> active = new(64);
        private readonly Queue<RawRain> pending = new(64);
        private readonly Stack<RainDrop> pool = new(32);

        public void SetLayer(RectTransform value) {
            // Old layers (and the pooled/active drops under them) are being
            // destroyed by the rebuild.
            active.Clear();
            pending.Clear();
            pool.Clear();

            for(int i = 0; i < rowLayers.Length; i++) {
                if(value == null) {
                    rowLayers[i] = null;
                    continue;
                }

                GameObject obj = new("Row" + (i + 1));
                obj.transform.SetParent(value, false);
                RectTransform rect = obj.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rowLayers[i] = rect;
            }
        }

        public void Enqueue(RawRain raw) => pending.Enqueue(raw);

        public void Clear() {
            pending.Clear();
            for(int i = active.Count - 1; i >= 0; i--) {
                Recycle(active[i]);
                active.RemoveAt(i);
            }
        }

        private void Recycle(RainDrop drop) {
            drop.Raw = null;
            drop.Obj.SetActive(false);
            pool.Push(drop);
        }

        private void Update() {
            if(rowLayers[0] == null) {
                pending.Clear();
                return;
            }

            while(pending.Count > 0) {
                RawRain raw = pending.Dequeue();
                RectTransform rowLayer = rowLayers[Mathf.Clamp(raw.Group, 1, 3) - 1];
                RainDrop drop = pool.Count > 0 ? pool.Pop() : new RainDrop(rowLayer);
                if(drop.Rect.parent != rowLayer) {
                    drop.Rect.SetParent(rowLayer, false);
                }
                drop.Obj.SetActive(true);
                drop.Raw = raw;
                drop.Graphic.color = raw.Color;
                drop.Rect.sizeDelta = Vector2.zero;
                active.Add(drop);
            }

            int count = active.Count;
            if(count == 0) {
                return;
            }

            float now = Time.unscaledTime;
            for(int i = 0; i < count; i++) {
                RainDrop drop = active[i];
                RawRain raw = drop.Raw;

                float lead = (now - raw.StartTime) * raw.Speed;
                float trail = raw.EndTime < 0f ? 0f : Mathf.Max(0f, (now - raw.EndTime) * raw.Speed);
                if(trail > raw.TrackHeight + 8f) {
                    Recycle(drop);
                    active.RemoveAt(i);
                    i--;
                    count--;
                    continue;
                }

                float dNear = trail;
                float dFar = Mathf.Min(lead, raw.TrackHeight);
                float height = dFar - dNear;
                if(height <= 0.5f) {
                    drop.Rect.sizeDelta = Vector2.zero;
                    continue;
                }

                drop.Graphic.SetFadeParams(
                    dNear, dFar, raw.TrackHeight, raw.FadePx, raw.Reverse,
                    raw.ColorTop, raw.ColorBottom
                );
                float y = raw.Reverse ? raw.BaseY + raw.TrackHeight - dFar : raw.BaseY + dNear;
                drop.Rect.anchoredPosition = new Vector2(raw.AnchorX, y);
                drop.Rect.sizeDelta = new Vector2(raw.Width, height);
            }
        }
    }

    private static void RecordDmPress(Box box, float now) {
        box.Count++;
        totalCount++;
        pressLog.Add(now);
        countsDirty = true;
    }

    private static void BeginDmNoteRain(Box box, float now) {
        DmNoteSpec spec = box.Dm;
        if(!Conf.DmNoteEffect || spec == null || !spec.NoteEnabled || rainManager == null) {
            return;
        }

        float delay = dmDelayedNoteEnabled ? dmShortNoteThresholdMs / 1000f : 0f;
        if(delay > 0.0001f) {
            box.DelayedNotePending = true;
            box.DelayedReleasedBeforeStart = false;
            box.DelayedDownTime = now;
            box.DelayedStartTime = now + delay;
            box.DelayedReleaseTime = -1f;
            return;
        }

        box.LastRain = SpawnDmRain(box, now, false);
    }

    private static void EndDmNoteRain(Box box, float now, bool forceMinLength = false) {
        if(box.DelayedNotePending) {
            box.DelayedReleasedBeforeStart = true;
            box.DelayedReleaseTime = now;
            return;
        }

        if(box.LastRain == null) {
            return;
        }

        float minLengthSeconds = dmNoteSpeed > 0f ? dmShortNoteMinLengthPx / dmNoteSpeed : 0f;
        float end = now;
        if(forceMinLength || minLengthSeconds > 0.0001f) {
            end = Mathf.Max(end, box.LastRain.StartTime + Mathf.Max(0.001f, minLengthSeconds));
        }

        box.LastRain.EndTime = end;
        box.LastRain = null;
    }

    private static void UpdateDelayedDmNote(Box box, float now) {
        if(!box.DelayedNotePending || now < box.DelayedStartTime) {
            return;
        }

        DmNoteSpec spec = box.Dm;
        box.DelayedNotePending = false;
        if(!Conf.DmNoteEffect || spec == null || !spec.NoteEnabled || rainManager == null) {
            return;
        }

        box.LastRain = SpawnDmRain(box, box.DelayedStartTime, false);
        if(box.DelayedReleasedBeforeStart) {
            EndDmNoteRain(box, box.DelayedReleaseTime >= 0f ? box.DelayedReleaseTime : now, forceMinLength: true);
            box.DelayedReleasedBeforeStart = false;
        }
    }

    private static int DmStatValue(Box box) {
        if(box.IsKps) {
            return pressLog.Count;
        }
        if(box.IsKpsAvg) {
            return kpsSamples > 0 ? Mathf.RoundToInt(kpsSum / (float)kpsSamples) : 0;
        }
        if(box.IsKpsMax) {
            return kpsMax;
        }
        return box.IsTotal ? totalCount : 0;
    }

    private static void UpdateDmNote(float now) {
        ApplyDmRuntimeSettings();

        while(pressLog.Count > 0 && now - pressLog[0] > 1f) {
            pressLog.RemoveAt(0);
        }

        if(now >= nextKpsSample) {
            int kps = pressLog.Count;
            if(kps > kpsMax) {
                kpsMax = kps;
            }
            if(kps > 0) {
                kpsSum += kps;
                kpsSamples++;
            }
            nextKpsSample = now + 0.05f;
        }

        TMP_FontAsset font = FontManager.Current;
        int limiterMode = Mathf.Clamp(Conf.DmOutOfLimiterMode, 0, 2);

        foreach(Box box in boxes) {
            if(box.Label != null && box.Label.font != font) {
                box.Label.font = font;
            }
            if(box.Value != null && box.Value.font != font) {
                box.Value.font = font;
            }

            DmNoteSpec spec = box.Dm;
            if(spec == null) {
                continue;
            }

            if(spec.IsStat) {
                int value = DmStatValue(box);
                if(box.Value != null && box.LastShown != value) {
                    box.Value.text = value.ToString(CultureInfo.InvariantCulture);
                } else if(box.Value == null && box.Label != null && spec.InlineStatCounter && box.LastShown != value) {
                    box.Label.text = DmInlineStatText(spec, value);
                }
                box.LastShown = value;
                continue;
            }

            bool rawPressed = box.Key != KeyCode.None && Input.GetKey(box.Key);
            bool blocked = box.Key != KeyCode.None && rawPressed && Features.KeyLimiter.KeyLimiter.ShouldBlockKey(box.Key);
            bool hidden = blocked && limiterMode == 0;
            bool rainOnly = blocked && limiterMode == 1;
            bool physicalPressed = rawPressed && !hidden && !rainOnly;
            bool ghostPressed = (rainOnly || (spec.GhostKeyCode != KeyCode.None && Input.GetKey(spec.GhostKeyCode))) && !hidden;

            if(physicalPressed && !box.RawPressed) {
                RecordDmPress(box, now);
                BeginDmNoteRain(box, now);
            } else if(!physicalPressed && box.RawPressed) {
                EndDmNoteRain(box, now);
            }
            box.RawPressed = physicalPressed;
            UpdateDelayedDmNote(box, now);

            if(ghostPressed && !box.GhostPressed) {
                if(Conf.DmNoteEffect && spec.NoteEnabled && rainManager != null) {
                    box.LastGhostRain = SpawnDmRain(box, now, true);
                }
                if(rainOnly) {
                    totalCount++;
                    pressLog.Add(now);
                }
            } else if(!ghostPressed && box.GhostPressed && box.LastGhostRain != null) {
                float minLengthSeconds = dmNoteSpeed > 0f ? dmShortNoteMinLengthPx / dmNoteSpeed : 0f;
                box.LastGhostRain.EndTime = Mathf.Max(now, box.LastGhostRain.StartTime + Mathf.Max(0.001f, minLengthSeconds));
                box.LastGhostRain = null;
            }

            bool displayPressed;
            float displayDelay = dmKeyDisplayDelayMs / 1000f;
            if(displayDelay <= 0.0001f) {
                box.DisplayTargetPressed = physicalPressed;
                box.DisplayTargetTime = now;
                displayPressed = physicalPressed;
            } else {
                if(physicalPressed != box.DisplayTargetPressed) {
                    box.DisplayTargetPressed = physicalPressed;
                    box.DisplayTargetTime = now + displayDelay;
                }
                displayPressed = now >= box.DisplayTargetTime ? box.DisplayTargetPressed : box.Pressed;
            }

            if(displayPressed != box.Pressed) {
                box.Pressed = displayPressed;
                ApplyBoxColors(box);
            }
            box.GhostPressed = ghostPressed;

            if(box.Value != null && box.Count != box.LastShown) {
                box.LastShown = box.Count;
                box.Value.text = box.Count.ToString(CultureInfo.InvariantCulture);
            }
        }

        if(countsDirty && now >= nextCountsSave) {
            nextCountsSave = now + 2f;
            FlushCounts();
        }
    }

    private sealed class Updater : MonoBehaviour {
        private int lastKps = int.MinValue;
        private int lastTotal = int.MinValue;

        private void Update() {
            if(root == null) {
                return;
            }

            bool isReorganizing = UICore.IsReorganizing;
            bool overlayVisible = (Panels.PanelsOverlay.IsEnabled && Conf.Enabled && GameStats.InGame) || isReorganizing;
            bool show = (Conf.IsSimpleMode || Conf.IsDmNoteMode) && overlayVisible;
            if(root.gameObject.activeSelf != show) {
                root.gameObject.SetActive(show);
            }

            if(dragObj != null && dragObj.activeSelf != isReorganizing) {
                dragObj.SetActive(isReorganizing);
            }

            if(!show) {
                return;
            }

            float now = Time.unscaledTime;

            if(Conf.IsDmNoteMode) {
                Conf.DmOffsetX = root.anchoredPosition.x;
                Conf.DmOffsetY = root.anchoredPosition.y;
                UpdateDmNote(now);
                return;
            }

            // Drag writes the position; mirror it back into the settings so
            // it persists.
            Conf.OffsetX = root.anchoredPosition.x;
            Conf.OffsetY = root.anchoredPosition.y;

            // KPS window: drop presses older than one second.
            while(pressLog.Count > 0 && now - pressLog[0] > 1f) {
                pressLog.RemoveAt(0);
            }

            TMP_FontAsset font = FontManager.Current;

            foreach(Box box in boxes) {
                if(box.Label != null && box.Label.font != font) {
                    box.Label.font = font;
                }
                if(box.Value != null && box.Value.font != font) {
                    box.Value.font = font;
                }

                if(box.IsStat) {
                    int value = box.IsKps ? pressLog.Count : totalCount;
                    int last = box.IsKps ? lastKps : lastTotal;
                    if(value != last) {
                        box.Value.text = value.ToString(CultureInfo.InvariantCulture);
                        if(box.IsKps) {
                            lastKps = value;
                        } else {
                            lastTotal = value;
                        }
                    }
                    continue;
                }

                bool pressed = box.Key != KeyCode.None && Input.GetKey(box.Key);
                if(pressed && !box.Pressed) {
                    box.Count++;
                    totalCount++;
                    pressLog.Add(now);
                    countsDirty = true;

                    if(Conf.RainEnabled && box.RainGroup != 0 && rainManager != null) {
                        box.LastRain = SpawnRain(box, now);
                    }
                } else if(!pressed && box.Pressed && box.LastRain != null) {
                    // Release: freeze the drop's trailing edge.
                    box.LastRain.EndTime = now;
                    box.LastRain = null;
                }

                if(pressed != box.Pressed) {
                    box.Pressed = pressed;
                    ApplyBoxColors(box);
                }

                if(box.Count != box.LastShown) {
                    box.LastShown = box.Count;
                    box.Value.text = box.Count.ToString(CultureInfo.InvariantCulture);
                }
            }

            // Counts persist with the config; batch the writes so a press
            // burst doesn't spam the debounced save.
            if(countsDirty && now >= nextCountsSave) {
                nextCountsSave = now + 2f;
                FlushCounts();
            }
        }
    }
}
