using System.Globalization;
using Quartz.Core;
using Quartz.Features.Status;
using Quartz.IO;
using Quartz.Resource;
using Quartz.UI;
using Quartz.UI.Utility;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

using TMPro;

namespace Quartz.Features.KeyViewer;

// Key viewer overlay — a port of v1's "simple" key viewer: a fixed grid of
// key boxes (10/12/16/20-key styles) with per-key press counters plus KPS and
// Total stat boxes. Layout constants and defaults come from v1's
// SimplePresets (50px keys, 4px gap, 54px row pitch, 8-column grid).
// Draggable in Reorganize mode like the other HUD elements.
//
// v1 features not ported yet: rain / ghost rain, foot keys, label overrides,
// key rebinding UI, key-limiter sync.
public static partial class KeyViewerOverlay {
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
    // Foot keys: smaller boxes on their own row(s) below the main grid (v1
    // FootKeyviewerStyle: 30px boxes, font 13, no rain or counter).
    private const float FootKeyW = 30f;
    private const float FootKeyH = 30f;
    private const float FootKeyGap = 6f;
    private const float FootRowPitch = 40f;
    private const float FootGapAbove = 12f;
    private const float FootFontSize = 13f;
    // Second-row slot order for the 16/20-key styles (v1 BackSeq16).
    private static readonly int[] BackSeq16 = [12, 13, 9, 8, 10, 11, 14, 15];

    private static GameObject canvasObj;
    private static GraphicRaycaster raycaster;
    private static RectTransform root;
    private static GameObject dragObj;
    // Foot keys are a separate, independently-draggable element (own root +
    // reorganize handle), so they never move or resize the main grid.
    private static RectTransform footRoot;
    private static GameObject footDragObj;
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
    private static readonly Queue<float> pressLog = new(64);
    private static int kpsMax;
    private static int kpsSum;
    private static int kpsSamples;
    private static float nextKpsSample;
    private static int totalCount;
    private static bool countsDirty;
    private static float nextCountsSave;

    private sealed class Box {
        public KeyCode Key;
        public KeyCode GhostKey = KeyCode.None;
        public bool IsFoot;
        public bool IsKps;
        public bool IsKpsAvg;
        public bool IsKpsMax;
        public bool IsTotal;
        public string Name;
        public Image Border;
        public Image Fill;
        // Optional soft sprite behind the box for a CSS box-shadow halo.
        public Image Glow;
        // CSS extras: a masked gradient fill child, the :before/:after layers,
        // and the per-state background image.
        public RawImage FillGrad;
        public RawImage BeforeLayer;
        public RawImage AfterLayer;
        public RawImage KeyImage;
        // Last text the per-glyph gradient coloured, so the mesh is only forced
        // to rebuild when the string actually changes.
        public string GradLabelText;
        public string GradValueText;
        // transition: timestamp the state flip started (<0 = settled).
        public float TransStart = -1f;
        public TextMeshProUGUI Label;
        public TextMeshProUGUI Value;
        // Simple-mode KPS/Total stat boxes: when StatTogether, the caption and
        // value render centred together in the Value text ("KPS  0") and Label
        // is hidden; otherwise the caption sits left and the value right.
        public string StatCaption;
        public bool StatTogether;
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
        // Flat per-key slot (0-19 main, 20-35 foot) for per-key colour/font
        // lookups; -1 for the stat boxes.
        public int Slot = -1;
        // Per-key press timestamps for the optional per-key KPS counter; only
        // filled for the key boxes (not stats).
        public readonly Queue<float> KpsLog = new();

        // Rain spawn parameters: color group (1 = front row, 2 = back row,
        // 3 = the 20-key style's third row, 0 = no rain) and the box span.
        public int RainGroup;
        public float CenterX;
        public float BoxW;
        // Horizontal rain alignment within the box: -1 = left edge, 0 = center,
        // +1 = right edge. Only matters when the rain is narrower than the box
        // (a wide key); single keys leave it 0 (centered).
        public float RainAlign;
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

        // Custom-CSS layer (KeyViewerStylesheet). ClassName is the preset key's
        // assigned CSS class (".blue"); the rest are filled by ApplyCssToSpec
        // when a stylesheet is active and override the preset values above.
        public string ClassName = "";
        public bool Bold;
        public bool CounterBold;
        public float ActiveOffsetX, ActiveOffsetY;
        public float CounterStrokeWidth;
        // Text glow (CSS text-shadow) per state, on the label and the counter.
        public CssGlow LabelGlow, ActiveLabelGlow, CounterGlow, ActiveCounterGlow;
        // Box-shadow halo per state (color + blur drive a soft sprite behind).
        public CssGlow BoxGlow, ActiveBoxGlow;
        // Animated gradients (CSS linear-gradient + animation) for the label /
        // counter text and the box fill. Text/counter gradients paint per glyph
        // via TMP vertex colours; the fill gradient drives a masked child image.
        public CssAnimGradient LabelGradient, ActiveLabelGradient;
        public CssAnimGradient CounterGradient, ActiveCounterGradient;
        public CssAnimGradient FillGradient, ActiveFillGradient;

        // transform: scale()/rotate() per state (translate folds into the offsets
        // below). transition: tween duration. @font-face / font-family resolved.
        public Vector2 IdleOffset, ActiveOffset;
        public Vector2 IdleScale = Vector2.one, ActiveScale = Vector2.one;
        public float IdleRot, ActiveRot;
        public float TransitionSec;
        public TMP_FontAsset CssFont;
        // filter: brightness()/contrast() fold into a colour multiply; saturate()
        // is applied to the resolved colours. 1 / white = identity.
        public Color IdleFilter = Color.white, ActiveFilter = Color.white;
        // backdrop-filter: blur() — approximated as a frosted fill (no true
        // scene blur is possible from a ScreenSpaceOverlay canvas).
        public float IdleBackdrop, ActiveBackdrop;
        // :before / :after pseudo layers per state.
        public CssLayerRt IdleBefore, ActiveBefore, IdleAfter, ActiveAfter;

        // KPS-graph element (DM Note GraphPanel). When IsGraph the box renders a
        // line/bar chart of the stat history instead of a key/counter. Defaults
        // mirror DmNote's GraphPanel.
        public bool IsGraph;
        public string GraphType = "line";      // "line" | "bar"
        public string GraphStat = "kps";        // which stat to plot
        public float GraphSpeed = 1000f;         // window in ms (clamped 500..5000)
        public Color GraphColor = new(0.525f, 0.937f, 0.678f, 1f);  // #86EFAC
        public bool GraphShowAvg = true;
        public bool GraphAnim = true;
        public Color GraphBg = new(17f / 255f, 17f / 255f, 20f / 255f, 0.9f);
        public Color GraphBorder = new(1f, 1f, 1f, 0.1f);
        public float GraphBorderWidth = 3f;
        public float GraphBorderRadius = 8f;
        // DM Note's "Inline Styles Priority": when true the preset's inline
        // colours win and --graph-* CSS is ignored.
        public bool GraphInlineStyles;

        public bool HasPseudo =>
            IdleBefore != null || ActiveBefore != null || IdleAfter != null || ActiveAfter != null;

        // Background images (DM Note inactiveImage/activeImage + object-fit). Held
        // as raw source strings (data URI / URL / file path); resolved to textures
        // in BuildKeyImage. Fit precedence mirrors useKeyElementStyles.
        public string InactiveImage = "", ActiveImage = "";
        public string IdleImageFit = "", ActiveImageFit = "", ImageFitDefault = "";
        public Texture2D IdleTex, ActiveTex;
        public bool HasImage => InactiveImage.Length > 0 || ActiveImage.Length > 0;

        // Whether ApplyCssState has per-press work: glow, offset, transform,
        // filter, backdrop or pseudo layers. Gradients tick separately.
        public bool NeedsCssState =>
            BoxGlow.On || ActiveBoxGlow.On || LabelGlow.On || ActiveLabelGlow.On
            || CounterGlow.On || ActiveCounterGlow.On || CounterStrokeWidth > 0.01f
            || ActiveOffsetX != 0f || ActiveOffsetY != 0f
            || IdleOffset != Vector2.zero || ActiveOffset != Vector2.zero
            || IdleScale != Vector2.one || ActiveScale != Vector2.one
            || IdleRot != 0f || ActiveRot != 0f
            || IdleFilter != Color.white || ActiveFilter != Color.white
            || IdleBackdrop > 0f || ActiveBackdrop > 0f
            || FillGradient != null || ActiveFillGradient != null
            || HasImage || HasPseudo;
    }

    // A resolved glow (Unity colour + blur) ready to feed TMPTextShadow or the
    // box-halo sprite. Default On=false.
    internal readonly struct CssGlow {
        public readonly bool On;
        public readonly float X, Y, Blur;
        public readonly Color Color;
        public CssGlow(float x, float y, float blur, Color color) {
            On = true; X = x; Y = y; Blur = blur; Color = color;
        }
    }

    // A gradient resolved to Unity colours plus its scroll period and axis angle.
    // Text/counter gradients are sampled per glyph; the fill gradient is baked to
    // a cached texture. Period <= 0 = static.
    internal sealed class CssAnimGradient {
        public Color[] Stops;
        public float Period;     // seconds for a full scroll; <=0 = static
        public float AngleDeg;   // CSS angle (0 = up, 90 = right, 180 = down)
    }

    // A resolved :before / :after pseudo layer. Rendered as a child Image behind
    // (Z<0) or over (Z>=0) the box, optionally with a scrolling gradient texture.
    internal sealed class CssLayerRt {
        public Color[] GradStops;   // null = solid Bg
        public float GradPeriod;
        public float GradAngle;
        public Color Bg = new(0f, 0f, 0f, 0f);
        public float Radius = -1f;  // <0 = inherit the box radius
        public float InsetT, InsetR, InsetB, InsetL;
        public float Blur;
        public int Z;
        public bool HasGradient => GradStops != null && GradStops.Length > 0;
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

        canvasObj = new GameObject("QuartzKeyViewerCanvas");
        canvasObj.transform.SetParent(rootObject.transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the combo counter (32757).
        canvas.sortingOrder = 32758;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        raycaster = canvasObj.AddComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        GameObject gridObj = new("KeyViewerGrid");
        gridObj.transform.SetParent(canvasObj.transform, false);
        root = gridObj.AddComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 0f);
        root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0f);

        // Separate element for the foot keys, dragged on its own.
        GameObject footObj = new("KeyViewerFoot");
        footObj.transform.SetParent(canvasObj.transform, false);
        footRoot = footObj.AddComponent<RectTransform>();
        footRoot.anchorMin = new Vector2(0.5f, 0f);
        footRoot.anchorMax = new Vector2(0.5f, 0f);
        footRoot.pivot = new Vector2(0.5f, 0f);

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
        if(footRoot != null) {
            for(int i = footRoot.childCount - 1; i >= 0; i--) {
                Object.Destroy(footRoot.GetChild(i).gameObject);
            }
            footRoot.sizeDelta = Vector2.zero;
        }
        boxes.Clear();
        cssFx.Clear();
        cssGlowLayer = null;
        dragObj = null;
        footDragObj = null;
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

        int style = Mathf.Clamp(Conf.Style, 0, KeyViewerSettings.MaxStyle);
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
        rainObj.AddComponent<Canvas>().overrideSorting = false;
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

        BuildFoot();

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

        int[] keys = Conf.KeysForStyle(Mathf.Clamp(Conf.Style, 0, KeyViewerSettings.MaxStyle));
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
            root.anchoredPosition = OverlayCalibration.Scale(new Vector2(Conf.DmOffsetX, Conf.DmOffsetY));
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

        if(builtMode != KeyViewerSettings.ModeSimple || builtStyle != Mathf.Clamp(Conf.Style, 0, KeyViewerSettings.MaxStyle)) {
            Rebuild();
            return;
        }

        root.anchoredPosition = OverlayCalibration.Scale(new Vector2(Conf.OffsetX, Conf.OffsetY));
        float size = Mathf.Clamp(Conf.Size, 0.2f, 4f);
        root.localScale = new Vector3(size, size, 1f);

        // Foot element rides its own position at the same scale.
        if(footRoot != null) {
            footRoot.anchoredPosition = OverlayCalibration.Scale(new Vector2(Conf.FootOffsetX, Conf.FootOffsetY));
            footRoot.localScale = new Vector3(size, size, 1f);
        }

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

        // KPS/Total sit on the outer edges with the back-row keys between them.
        // (The Together/Apart setting no longer moves these boxes — it controls
        // the caption/value arrangement inside each stat box; see AddStat.)
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
            case 4:
                // 8 keys: the front row only, with the stat boxes on the row
                // below (no back row).
                stats.Add(new StatSlot(false, ColX(0), RowGap, SpanW(4), 30f));
                stats.Add(new StatSlot(true, ColX(4), RowGap, SpanW(4), 30f));
                break;
            case 5:
                // 14 keys: front row + a 6-key back row centred on columns 1-6,
                // stats on a third row like the 20-key style.
                for(int i = 0; i < 6; i++) {
                    keys.Add(new KeySlot(8 + i, ColX(1 + i), RowGap, KeyW, KeyH));
                }
                stats.Add(new StatSlot(false, ColX(0), RowGap * 2f, SpanW(2), KeyH));
                stats.Add(new StatSlot(true, ColX(6), RowGap * 2f, SpanW(2), KeyH));
                break;
        }
    }

    internal static Vector2 GridSize(int style) => new(SpanW(8), style switch {
        2 => RowGap * 2f + 30f,
        3 => RowGap * 2f + KeyH,
        4 => RowGap + 30f,
        5 => RowGap * 2f + KeyH,
        _ => RowGap + KeyH,
    });

    // Foot-key layout in the foot element's OWN local space (top-left origin),
    // independent of the main grid. footCount 0 = none. Fills footSlots with
    // KeySlots whose Slot is FootSlotBase + foot index, and returns the foot
    // block's size. Up to 8 keys per row, centred; a second row centres below.
    internal static Vector2 BuildFootLayout(int footCount, List<KeySlot> footSlots) {
        if(footCount <= 0) {
            return Vector2.zero;
        }

        int row1 = Mathf.Min(footCount, 8);
        int row2 = Mathf.Max(0, footCount - 8);

        static float RowW(int n) => n <= 0 ? 0f : n * FootKeyW + (n - 1) * FootKeyGap;
        float blockW = Mathf.Max(RowW(row1), RowW(row2));

        void Row(int startIndex, int count, float y) {
            float startX = (blockW - RowW(count)) * 0.5f;
            for(int i = 0; i < count; i++) {
                int footIndex = startIndex + i;
                footSlots.Add(new KeySlot(
                    KeyViewerSettings.FootSlotBase + footIndex,
                    startX + i * (FootKeyW + FootKeyGap), y, FootKeyW, FootKeyH));
            }
        }

        Row(0, row1, 0f);
        if(row2 > 0) {
            Row(8, row2, FootRowPitch);
        }

        float blockH = row2 > 0 ? FootRowPitch + FootKeyH : FootKeyH;
        return new Vector2(blockW, blockH);
    }

    // Combined size used ONLY by the settings-page preview, which stacks the
    // foot block under the main grid for rebinding (the live overlay keeps them
    // as two separate elements). Returns the bounding size; the page offsets the
    // foot rows down by main height + gap.
    internal static Vector2 GridSizeWithFoot(int style, int footCount) {
        Vector2 main = GridSize(style);
        if(footCount <= 0) {
            return main;
        }
        List<KeySlot> footSlots = [];
        Vector2 foot = BuildFootLayout(footCount, footSlots);
        return new Vector2(Mathf.Max(main.x, foot.x), main.y + FootGapAbove + foot.y);
    }

    // Builds the foot-key element (its own root, sized + reorganize handle).
    private static void BuildFoot() {
        if(footRoot == null) {
            return;
        }

        int footCount = Conf.FootKeyCount();
        if(!Conf.IsSimpleMode || footCount <= 0) {
            footRoot.sizeDelta = Vector2.zero;
            return;
        }

        List<KeySlot> footSlots = [];
        Vector2 footSize = BuildFootLayout(footCount, footSlots);
        footRoot.sizeDelta = footSize;

        foreach(KeySlot slot in footSlots) {
            AddFootKey(slot.Slot - KeyViewerSettings.FootSlotBase, slot.X, slot.Y, slot.W, slot.H);
        }

        AddFootReorganizeHandle();
    }

    private static void AddKey(int[] keys, int slot, float x, float y, float w, float h) {
        if(slot < 0 || slot >= keys.Length) {
            return;
        }

        KeyCode key = (KeyCode)keys[slot];
        Box box = NewBox("Key_" + slot, x, y, w, h);
        box.Key = key;
        box.Slot = slot;
        int[] ghostKeys = Conf.GhostKeysForStyle(builtStyle);
        box.GhostKey = slot < ghostKeys.Length ? (KeyCode)ghostKeys[slot] : KeyCode.None;
        box.Name = key.ToString().ToUpperInvariant();
        box.Count = Conf.GetCount(box.Name);

        // v1 SlotRainGroup: front row = group 1, the 20-key style's third
        // row = group 3, everything else = group 2.
        box.RainGroup = slot < 8 ? 1 : builtStyle == 3 && slot >= 16 ? 3 : 2;
        box.CenterX = x + w * 0.5f;
        box.BoxW = w;

        // A wide key (e.g. the 10-key back row's 2-wide keys) pulls its one-key
        // rain toward the grid center: a key left of center aligns its rain to its
        // RIGHT edge, one right of center to its LEFT edge — so the two inner
        // rains sit next to each other instead of each emitting from the middle of
        // a wide key. Single keys (cols == 1) stay centered.
        int cols = Mathf.Max(1, Mathf.RoundToInt((w + KeyGap) / (KeyW + KeyGap)));
        if(cols > 1) {
            float gridCenter = SpanW(8) * 0.5f;
            box.RainAlign = box.CenterX < gridCenter - 0.5f ? 1f
                : box.CenterX > gridCenter + 0.5f ? -1f
                : 0f;
        }

        // With the counter shown, the label is lifted off the counter strip at
        // the bottom; with main counts hidden there's no strip, so the label
        // fills the box and reads vertically centered.
        bool showCount = !Conf.HideMainKeyCount;
        box.Label = NewText(box.Fill.transform, "Label", LabelFor(builtStyle, slot), KeyFontSize * Conf.KeyFontFor(slot));
        RectTransform labelRect = box.Label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(0f, showCount ? 12f : 0f);
        labelRect.offsetMax = Vector2.zero;

        // No counter object at all when main counts are hidden — keeps the label
        // centered and skips the per-frame counter work for these boxes.
        if(showCount) {
            box.Value = NewText(box.Fill.transform, "Counter", "0", CounterFontSize * Conf.CounterFontFor(slot));
            RectTransform counterRect = box.Value.rectTransform;
            counterRect.anchorMin = Vector2.zero;
            counterRect.anchorMax = new Vector2(1f, 0f);
            counterRect.pivot = new Vector2(0.5f, 0f);
            counterRect.anchoredPosition = new Vector2(0f, 3f);
            counterRect.sizeDelta = new Vector2(0f, 16f);
        }

        boxes.Add(box);
    }

    private static void AddStat(bool total, float x, float y, float w, float h) {
        Box box = NewBox(total ? "Total" : "Kps", x, y, w, h);
        box.IsKps = !total;
        box.IsTotal = total;
        string caption = total ? MainCore.Tr.Get("KEYVIEWER_STAT_TOTAL", "Total") : "KPS";
        box.StatCaption = caption;

        // 10/12-key styles (0/1): the stat box is narrow, so stack the caption
        // over the value instead of side by side. Overrides Together/Apart.
        bool stacked = builtStyle is 0 or 1;
        bool together = Conf != null && Conf.StatsTogether && !stacked;
        box.StatTogether = together;

        box.Label = NewText(box.Fill.transform, "Label", caption, StatFontSize);
        box.Value = NewText(box.Fill.transform, "Value", "0", StatFontSize);

        RectTransform labelRect = box.Label.rectTransform;
        RectTransform valueRect = box.Value.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        valueRect.anchorMin = Vector2.zero;
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;

        if(stacked) {
            // Caption in the top half, value in the bottom half (vertical stack).
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            box.Label.alignment = TextAlignmentOptions.Center;
            valueRect.anchorMax = new Vector2(1f, 0.5f);
            box.Value.alignment = TextAlignmentOptions.Center;
        } else if(together) {
            // Caption + value centred together as one group ("KPS  0"). The
            // value text carries the caption (see the stat update in Update),
            // so the standalone label is hidden.
            box.Label.gameObject.SetActive(false);
            box.Value.alignment = TextAlignmentOptions.Center;
            box.Value.text = caption + "  0";
        } else {
            // Spread apart on one line: caption pinned left, value pinned right.
            labelRect.offsetMin = new Vector2(10f, 0f);
            box.Label.alignment = TextAlignmentOptions.MidlineLeft;
            valueRect.offsetMax = new Vector2(-10f, 0f);
            box.Value.alignment = TextAlignmentOptions.MidlineRight;
        }

        boxes.Add(box);
    }

    private static void AddFootKey(int footIndex, float x, float y, float w, float h) {
        int[] footKeys = Conf.FootKeys;
        if(footIndex < 0 || footIndex >= footKeys.Length) {
            return;
        }

        int slot = KeyViewerSettings.FootSlotBase + footIndex;
        KeyCode key = (KeyCode)footKeys[footIndex];
        // Foot boxes live under the separate foot element, not the main grid.
        (Image fill, Image border) = NewBoxVisual("Foot_" + footIndex, footRoot, x, y, w, h);
        Box box = new() { Border = border, Fill = fill };
        box.Key = key;
        box.Slot = slot;
        box.IsFoot = true;
        // Foot keys never rain or count; they only light on press.
        box.RainGroup = 0;
        box.CenterX = x + w * 0.5f;
        box.BoxW = w;
        box.Name = key.ToString().ToUpperInvariant();

        box.Label = NewText(box.Fill.transform, "Label", LabelFor(builtStyle, slot), FootFontSize * Conf.KeyFontFor(slot));
        RectTransform labelRect = box.Label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

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
        rainObj.AddComponent<Canvas>().overrideSorting = false;
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

    // Separate reorganize handle for the foot element, so it drags on its own.
    private static void AddFootReorganizeHandle() {
        if(footRoot == null) {
            return;
        }

        GameObject drag = new("FootDrag");
        footDragObj = drag;
        drag.transform.SetParent(footRoot, false);
        RectTransform dragRect = drag.AddComponent<RectTransform>();
        dragRect.anchorMin = Vector2.zero;
        dragRect.anchorMax = Vector2.one;
        dragRect.offsetMin = Vector2.zero;
        dragRect.offsetMax = Vector2.zero;
        drag.AddComponent<EmptyGraphic>().raycastTarget = true;
        ReorganizeHandle handle = drag.AddComponent<ReorganizeHandle>();
        handle.Target = footRoot;
        handle.GetName = () => MainCore.Tr.Get("KEYVIEWER_FOOT_TITLE", "Foot Keys");
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

            // KPS graphs (DM Note graphPositions table), keyed by tab like keys
            // and stats.
            if(preset["graphPositions"] is JObject graphTable && graphTable[tab] is JArray graphArr) {
                for(int i = 0; i < graphArr.Count; i++) {
                    if(graphArr[i] is not JObject p || JBool(p, "hidden", false)) {
                        continue;
                    }
                    JObject pos = (p["position"] as JObject) ?? p;
                    if(JBool(pos, "hidden", false)) {
                        continue;
                    }
                    DmNoteSpec spec = ParseGraphSpec(pos);
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

        ApplyCssToSpecs(result);
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

    // A KPS-graph element from the graphPositions table. Mirrors DM Note's
    // GraphPanel defaults (200x100, line, #86EFAC, dark bg, faint border).
    private static DmNoteSpec ParseGraphSpec(JObject p) {
        DmNoteSpec spec = new() {
            IsGraph = true,
            X = JFloat(p, "dx", 0f),
            Y = JFloat(p, "dy", 0f),
            W = Mathf.Max(1f, JFloat(p, "width", 200f)),
            H = Mathf.Max(1f, JFloat(p, "height", 100f)),
            GraphType = JStr(p, "graphType", "line"),
            GraphStat = JStr(p, "statType", "kps"),
            GraphSpeed = Mathf.Clamp(JFloat(p, "graphSpeed", 1000f), 500f, 5000f),
            GraphColor = HexToColor(JStr(p, "graphColor", "#86EFAC"), 1f),
            GraphShowAvg = JBool(p, "showAvgLine", true),
            GraphAnim = JBool(p, "graphAnimationEnabled", true),
            GraphBg = HexToColor(JStr(p, "backgroundColor", "rgba(17, 17, 20, 0.9)"), 0.9f),
            GraphBorder = HexToColor(JStr(p, "borderColor", "rgba(255, 255, 255, 0.1)"), 0.1f),
            GraphBorderWidth = Mathf.Clamp(JFloat(p, "borderWidth", 3f), 0f, 20f),
            GraphBorderRadius = Mathf.Clamp(JFloat(p, "borderRadius", 8f), 0f, 100f),
            GraphInlineStyles = JBool(p, "useInlineStyles", false),
            ClassName = JOptionalString(p, "className") ?? "",
            InactiveImage = JOptionalString(p, "inactiveImage") ?? "",
            ActiveImage = JOptionalString(p, "activeImage") ?? "",
            IdleImageFit = JStr(p, "idleImageFit", ""),
            ImageFitDefault = JStr(p, "imageFit", ""),
        };
        spec.CountKey = "graph";
        spec.DisplayText = "";
        return spec;
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
        spec.ClassName = JOptionalString(p, "className") ?? "";
        spec.InactiveImage = JOptionalString(p, "inactiveImage") ?? "";
        spec.ActiveImage = JOptionalString(p, "activeImage") ?? "";
        spec.IdleImageFit = JStr(p, "idleImageFit", "");
        spec.ActiveImageFit = JStr(p, "activeImageFit", "");
        spec.ImageFitDefault = JStr(p, "imageFit", "");

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
        if(spec.IsGraph) {
            AddDmNoteGraph(index, spec);
            return;
        }

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
        BuildCssFx(box, spec);
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
        // Foot slots (20+) live in their own key/label arrays, shared by every
        // main style.
        if(slot >= KeyViewerSettings.FootSlotBase) {
            int fi = slot - KeyViewerSettings.FootSlotBase;
            string[] footOverrides = Conf.FootKeysText;
            if(fi >= 0 && fi < footOverrides.Length && !string.IsNullOrEmpty(footOverrides[fi])) {
                return footOverrides[fi];
            }
            int[] footKeys = Conf.FootKeys;
            return fi >= 0 && fi < footKeys.Length ? KeyCodeShortLabel((KeyCode)footKeys[fi]) : "";
        }

        string[] overrides = Conf.LabelsForStyle(style);
        if(slot >= 0 && slot < overrides.Length && !string.IsNullOrEmpty(overrides[slot])) {
            return overrides[slot];
        }

        int[] keys = Conf.KeysForStyle(style);
        return slot >= 0 && slot < keys.Length ? KeyCodeShortLabel((KeyCode)keys[slot]) : "";
    }

    // Counter text: optionally with a thousands separator (v1 count formatting).
    private static string FormatCount(int value) =>
        Conf != null && Conf.CountFormatting
            ? value.ToString("N0", CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

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
            // Solid base for the state; an animated gradient overwrites these per
            // frame in CssTick (the gradient's first stop already seeds them).
            box.Fill.color = dmPressed ? spec.ActiveBg : spec.Bg;
            if(box.Label != null) {
                box.Label.color = dmPressed ? spec.ActiveText : spec.Text;
            }
            if(box.Value != null) {
                box.Value.color = dmPressed ? spec.ActiveCounterText : spec.CounterText;
            }

            if(spec.NeedsCssState) {
                ApplyCssState(box, dmPressed);
            }
            return;
        }

        bool pressed = box.Pressed;
        int slot = box.Slot;
        box.Border.color = pressed
            ? Conf.PerKeyOr(Conf.PerKeyOutlinePressed, slot, Conf.GetOutlinePressed())
            : Conf.PerKeyOr(Conf.PerKeyOutline, slot, Conf.GetOutline());
        box.Fill.color = pressed
            ? Conf.PerKeyOr(Conf.PerKeyBgPressed, slot, Conf.GetBgPressed())
            : Conf.PerKeyOr(Conf.PerKeyBg, slot, Conf.GetBg());

        Color text = pressed
            ? Conf.PerKeyOr(Conf.PerKeyTextPressed, slot, Conf.GetTextPressed())
            : Conf.PerKeyOr(Conf.PerKeyText, slot, Conf.GetText());
        if(box.Label != null) {
            box.Label.color = text;
        }
        if(box.Value != null) {
            box.Value.color = text;
        }
    }

    // True while the key is held. Unity's legacy Input is NumLock-aware: with
    // NumLock OFF the numpad keys report as their navigation twins (Keypad0 ->
    // Insert, KeypadPeriod -> Delete, Keypad2 -> DownArrow, ...) and it always
    // reports the numpad Enter as Return (it can't tell them apart). So a box
    // bound to a numpad key would never light with NumLock off — accept the nav
    // twin as a fallback too. (Mirror of the KeypadEnter -> Return special case
    // this replaces. The reverse ambiguity — a real Insert/arrow press lighting
    // a numpad box when NumLock is on — is unavoidable with the Input API and
    // rare in play; the hook-based KeyLimiter path stays NumLock-independent.)
    private static bool KeyHeld(KeyCode key) {
        if(key == KeyCode.None) {
            return false;
        }
        if(Input.GetKey(key)) {
            return true;
        }
        KeyCode twin = NumpadNavTwin(key);
        if(twin != KeyCode.None && Input.GetKey(twin)) {
            return true;
        }
        // Unity's Input is blind to the Korean Hangul/Hanja keys (which map to
        // RightAlt/RightControl); fall back to the SkyHook-fed held state, the
        // only path that sees them. Additive — a no-op for keys Unity already
        // reports, so normal keys are unaffected.
        return Features.KeyLimiter.KeyLimiter.HookKeyHeld(key);
    }

    // The navigation key Unity's legacy Input reports for each numpad key while
    // NumLock is off. KeyCode.None for non-numpad keys (no fallback).
    private static KeyCode NumpadNavTwin(KeyCode key) => key switch {
        KeyCode.KeypadEnter => KeyCode.Return,
        KeyCode.Keypad0 => KeyCode.Insert,
        KeyCode.Keypad1 => KeyCode.End,
        KeyCode.Keypad2 => KeyCode.DownArrow,
        KeyCode.Keypad3 => KeyCode.PageDown,
        KeyCode.Keypad4 => KeyCode.LeftArrow,
        KeyCode.Keypad5 => KeyCode.Clear,
        KeyCode.Keypad6 => KeyCode.RightArrow,
        KeyCode.Keypad7 => KeyCode.Home,
        KeyCode.Keypad8 => KeyCode.UpArrow,
        KeyCode.Keypad9 => KeyCode.PageUp,
        KeyCode.KeypadPeriod => KeyCode.Delete,
        _ => KeyCode.None,
    };

    // v1 SimplePresets.KeyCodeShortLabel: compact key captions.
    internal static string KeyCodeShortLabel(KeyCode kc) {
        // Arrows resolve first: the Left/Right prefix rewrite below would turn
        // "LeftArrow"/"RightArrow" into "LArrow"/"RArrow", which then miss the
        // arrow-glyph switch at the end (Up/Down lack the prefix and worked).
        switch(kc) {
            case KeyCode.UpArrow: return "↑";
            case KeyCode.DownArrow: return "↓";
            case KeyCode.LeftArrow: return "←";
            case KeyCode.RightArrow: return "→";
        }

        string s = kc.ToString();
        if(s.StartsWith("Alpha")) s = s[5..];
        // Numpad keys: "N" prefix + the symbol/digit (the generic transforms
        // below would otherwise leave "NMultiply", "NEnter" etc.).
        if(s.StartsWith("Keypad")) {
            string rest = s[6..];
            return "N" + rest switch {
                "Enter" => "↵",
                "Plus" => "+",
                "Minus" => "-",
                "Multiply" => "*",
                "Divide" => "/",
                "Period" => ".",
                "Equals" => "=",
                _ => rest,
            };
        }
        if(s.StartsWith("Left")) s = "L" + s[4..];
        if(s.StartsWith("Right")) s = "R" + s[5..];
        if(s.EndsWith("Shift")) s = s[..^5] + "⇧";
        if(s.EndsWith("Control")) s = s[..^7] + "Ctrl";
        return s switch {
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
                return MainCore.Tr.Get("KEYVIEWER_STAT_TOTAL", "Total");
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
            return Features.KeyLimiter.KeyLimiter.NormalizeNumericKey(numeric);
        }

        string normalized = name.Replace(" ", "").Replace("_", "").Replace("-", "");
        if(normalized.StartsWith("KEY", StringComparison.OrdinalIgnoreCase) && normalized.Length == 4) {
            normalized = normalized[3..];
        }
        if(normalized.StartsWith("DIGIT", StringComparison.OrdinalIgnoreCase) && normalized.Length == 6) {
            normalized = normalized[5..];
        }
        // Numpad keys: the DM Note app names them "NUMPAD <x>" (e.g. "NUMPAD
        // RETURN", "NUMPAD MULTIPLY"), which don't match Unity's "Keypad*"
        // enum, so map them explicitly.
        if(normalized.StartsWith("NUMPAD", StringComparison.OrdinalIgnoreCase) && normalized.Length > 6) {
            string np = normalized.Substring(6).ToUpperInvariant();
            KeyCode npk = np switch {
                "ENTER" or "RETURN" => KeyCode.KeypadEnter,
                "PLUS" or "ADD" => KeyCode.KeypadPlus,
                "MINUS" or "SUBTRACT" => KeyCode.KeypadMinus,
                "MULTIPLY" or "STAR" or "ASTERISK" => KeyCode.KeypadMultiply,
                "DIVIDE" or "SLASH" => KeyCode.KeypadDivide,
                "DELETE" or "DECIMAL" or "PERIOD" or "DOT" or "DEL" => KeyCode.KeypadPeriod,
                "EQUALS" or "EQUAL" => KeyCode.KeypadEquals,
                _ => np.Length == 1 && np[0] >= '0' && np[0] <= '9'
                    ? (KeyCode)((int)KeyCode.Keypad0 + (np[0] - '0'))
                    : KeyCode.None,
            };
            if(npk != KeyCode.None) {
                return npk;
            }
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
            case "LEFTCTRL":
            case "CTRL":
            case "CONTROL":
            case "LCTRL": return KeyCode.LeftControl;
            case "RCONTROL":
            case "RIGHTCONTROL":
            case "RIGHTCTRL":
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
            case "SQUAREBRACKETOPEN":
            case "OPENBRACKET":
            case "LEFTBRACKET":
            case "LBRACKET": return KeyCode.LeftBracket;
            case "SQUAREBRACKETCLOSE":
            case "CLOSEBRACKET":
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
            // The foot element is a separate piece — reset it too.
            Conf.FootOffsetX = def.FootOffsetX;
            Conf.FootOffsetY = def.FootOffsetY;
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

    // Picks a DM Note custom-CSS file and stores its text on the config (like
    // the preset, the CSS travels with the config so it survives a file move).
    // Enables the CSS layer on a successful import. Mirrors ImportDmNotePreset.
    public static bool ImportDmNoteCss(out string error) {
        error = null;
        string picked;

        try {
            picked = UnityFileDialog.FileBrowser.PickFile(
                "", "CSS", new[] { "css" }, "Select DM Note custom CSS");
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
            Conf.DmCssText = text;
            Conf.DmCssPath = picked;
            Conf.DmCssEnabled = true;
            Rebuild();
            Save();
            MainCore.Log.Msg("[KeyViewer] Imported DM Note CSS from " + picked);
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
        raycaster = null;
        root = null;
        dragObj = null;
        footRoot = null;
        footDragObj = null;
        rainManager = null;
        boxes.Clear();
        pressLog.Clear();
        kpsMax = 0;
        kpsSum = 0;
        kpsSamples = 0;
        builtStyle = -1;
    }

    // ===== rain (port of v1's KvRain* — one manager Update, batched row meshes,
    // custom graphic with vertex-alpha fade; allocation only on key press) =====

    private static RawRain SpawnRain(Box box, float now, bool ghost = false) {
        bool frontRow = box.RainGroup == 1;
        float width = frontRow ? Conf.RainWidth : Conf.Rain2Width;
        if(width <= 0.5f) {
            // 0 = one key wide. A multi-column key (e.g. a wide spacebar or the
            // 10-key back row's 2-wide keys) gets a single-key-wide rain, NOT one
            // spanning the whole box — so it never reads as "2 keys wide".
            width = KeyW;
        }
        // A set width is used as-is — never multiplied per column — so a 2-wide
        // key keeps a one-key rain unless the width is deliberately set wider.

        // Center the rain over the KEY-column nearest the aligned edge, not the
        // box edge: the offset runs from the box center to a single column's
        // center (half the box minus one key), so a wide key's rain sits squarely
        // over that key whatever the rain's own width.
        float keyOffset = Mathf.Max(0f, box.BoxW - KeyW) * 0.5f;

        RawRain raw = new() {
            Group = box.RainGroup,
            StartTime = now,
            AnchorX = box.CenterX + box.RainAlign * keyOffset,
            Width = width,
            // Offset moves the base line; rain rises from the grid top.
            BaseY = -(frontRow ? Conf.RainOffsetY : Conf.Rain2OffsetY),
            TrackHeight = Mathf.Max(1f, Conf.RainHeight),
            Speed = Mathf.Max(1f, Conf.RainSpeed),
            FadePx = Mathf.Max(0f, Conf.RainFade),
            // Ghost rain uses its own shared colour and ignores per-key rain.
            Color = ghost ? Conf.GetGhostRain() : Conf.PerKeyOr(Conf.PerKeyRain, box.Slot, box.RainGroup switch {
                1 => Conf.GetRain(),
                3 => Conf.GetRain3(),
                _ => Conf.GetRain2(),
            }),
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

    // Batched row renderer for rain drops. One Graphic per row replaces one
    // GameObject + Graphic + RectTransform write per live drop.
    private sealed class RainGraphic : MaskableGraphic {
        private List<RawRain> active;
        private float now;

        public void SetSource(List<RawRain> source) {
            active = source;
            SetVerticesDirty();
        }

        public void SetFrame(float frameTime) {
            now = frameTime;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            if(active == null || active.Count == 0) {
                return;
            }

            Rect layer = rectTransform.rect;
            for(int i = 0; i < active.Count; i++) {
                AddDrop(vh, layer, active[i]);
            }
        }

        private void AddDrop(VertexHelper vh, Rect layer, RawRain raw) {
            float lead = (now - raw.StartTime) * raw.Speed;
            float trail = raw.EndTime < 0f ? 0f : Mathf.Max(0f, (now - raw.EndTime) * raw.Speed);
            float dNear = trail;
            float dFar = Mathf.Min(lead, raw.TrackHeight);
            float height = dFar - dNear;
            if(height <= 0.5f || raw.Width <= 0.5f) {
                return;
            }

            float dropY = raw.Reverse ? raw.BaseY + raw.TrackHeight - dFar : raw.BaseY + dNear;
            float xMin = layer.xMin + raw.AnchorX - (raw.Width * 0.5f);
            float xMax = xMin + raw.Width;
            float yMin = layer.yMax + dropY;
            float yMax = yMin + height;

            // Plain vertex-coloured quad — no rounded corners. Matches the
            // original KRP rain: 4 verts / 2 tris per drop, the cheapest a drop
            // can be. The top/bottom colour gradient and the fade are both linear
            // in distance and distance is linear in Y, so a single quad
            // reproduces them exactly — except across the fade boundary, where
            // the alpha kinks. Split into two quads there (as the original did),
            // one quad otherwise.
            Color cMin = ColorForY(raw, dNear, dFar, yMin, yMin, height);
            Color cMax = ColorForY(raw, dNear, dFar, yMax, yMin, height);

            if(raw.FadePx > 0.5f && raw.TrackHeight > 0.5f) {
                float fadeStartD = raw.TrackHeight - raw.FadePx;
                float span = dFar - dNear;
                if(span > 0.0001f) {
                    float tB = raw.Reverse
                        ? (fadeStartD - dFar) / (dNear - dFar)
                        : (fadeStartD - dNear) / span;
                    if(tB > 0.0001f && tB < 0.9999f) {
                        float yMid = yMin + (tB * height);
                        Color cMid = ColorForY(raw, dNear, dFar, yMid, yMin, height);
                        AddQuad(vh, xMin, yMin, xMax, yMid, cMin, cMid);
                        AddQuad(vh, xMin, yMid, xMax, yMax, cMid, cMax);
                        return;
                    }
                }
            }

            AddQuad(vh, xMin, yMin, xMax, yMax, cMin, cMax);
        }

        private static void AddQuad(VertexHelper vh, float xMin, float yMin, float xMax, float yMax, Color bottom, Color top) {
            int idx = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.position = new Vector3(xMin, yMin, 0f); v.color = bottom; vh.AddVert(v);
            v.position = new Vector3(xMax, yMin, 0f); v.color = bottom; vh.AddVert(v);
            v.position = new Vector3(xMax, yMax, 0f); v.color = top; vh.AddVert(v);
            v.position = new Vector3(xMin, yMax, 0f); v.color = top; vh.AddVert(v);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

        private static Color ColorForY(RawRain raw, float dNear, float dFar, float y, float yMin, float height) {
            float t = height <= 0.0001f ? 0f : (y - yMin) / height;
            float d = raw.Reverse
                ? Mathf.Lerp(dFar, dNear, t)
                : Mathf.Lerp(dNear, dFar, t);

            float alpha = (raw.FadePx > 0.5f && raw.TrackHeight > 0.5f)
                ? AlphaAtD(d, raw.TrackHeight - raw.FadePx, raw.TrackHeight, raw.FadePx)
                : 1f;

            return ColorAtD(raw, d, alpha);
        }

        private static Color ColorAtD(RawRain raw, float d, float alphaMul) {
            float t = raw.TrackHeight <= 0.0001f ? 0f : Mathf.Clamp01(d / raw.TrackHeight);
            Color c = Color.Lerp(raw.ColorBottom, raw.ColorTop, t);
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
    }

    private sealed class RainRow {
        public readonly RectTransform Rect;
        public readonly RainGraphic Graphic;
        public readonly List<RawRain> Active = new(64);

        public RainRow(RectTransform parent, int index) {
            GameObject obj = new("Row" + index);
            obj.transform.SetParent(parent, false);
            Rect = obj.AddComponent<RectTransform>();
            Rect.anchorMin = Vector2.zero;
            Rect.anchorMax = Vector2.one;
            Rect.offsetMin = Vector2.zero;
            Rect.offsetMax = Vector2.zero;

            Graphic = obj.AddComponent<RainGraphic>();
            Graphic.raycastTarget = false;
            Graphic.color = Color.white;
            Graphic.SetSource(Active);
        }
    }

    // One manager update. Drops are grouped into per-row meshes so row ordering
    // stays stable without per-drop Unity components.
    private sealed class RainManager : MonoBehaviour {
        private readonly RainRow[] rows = new RainRow[3];
        private readonly Queue<RawRain> pending = new(64);

        public void SetLayer(RectTransform value) {
            pending.Clear();

            for(int i = 0; i < rows.Length; i++) {
                rows[i]?.Active.Clear();
                if(value == null) {
                    rows[i] = null;
                    continue;
                }

                rows[i] = new RainRow(value, i + 1);
            }
        }

        public void Enqueue(RawRain raw) {
            if(raw != null) {
                pending.Enqueue(raw);
            }
        }

        public void Clear() {
            pending.Clear();
            for(int i = 0; i < rows.Length; i++) {
                if(rows[i] == null) {
                    continue;
                }
                rows[i].Active.Clear();
                rows[i].Graphic.SetVerticesDirty();
            }
        }

        private void Update() {
            if(rows[0] == null) {
                pending.Clear();
                return;
            }

            while(pending.Count > 0) {
                RawRain raw = pending.Dequeue();
                rows[Mathf.Clamp(raw.Group, 1, 3) - 1].Active.Add(raw);
            }

            float now = Time.unscaledTime;

            for(int r = 0; r < rows.Length; r++) {
                RainRow row = rows[r];
                List<RawRain> active = row.Active;
                bool dirty = active.Count > 0;
                int write = 0;
                for(int read = 0; read < active.Count; read++) {
                    RawRain raw = active[read];
                    float trail = raw.EndTime < 0f ? 0f : Mathf.Max(0f, (now - raw.EndTime) * raw.Speed);
                    if(trail <= raw.TrackHeight + 8f) {
                        if(write != read) {
                            active[write] = raw;
                        }
                        write++;
                        continue;
                    }

                    dirty = true;
                }
                if(write < active.Count) {
                    active.RemoveRange(write, active.Count - write);
                }

                if(dirty) {
                    row.Graphic.SetFrame(now);
                }
            }
        }
    }

    private static void RecordDmPress(Box box, float now) {
        box.Count++;
        totalCount++;
        pressLog.Enqueue(now);
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

    // Current value of a named stat, for the KPS graph to plot.
    internal static int GraphStatValue(string statType) {
        if(string.IsNullOrEmpty(statType)) {
            return pressLog.Count;
        }
        if(statType.Equals("kpsAvg", StringComparison.OrdinalIgnoreCase)) {
            return kpsSamples > 0 ? Mathf.RoundToInt(kpsSum / (float)kpsSamples) : 0;
        }
        if(statType.Equals("kpsMax", StringComparison.OrdinalIgnoreCase)) {
            return kpsMax;
        }
        if(statType.Equals("total", StringComparison.OrdinalIgnoreCase)) {
            return totalCount;
        }
        return pressLog.Count; // kps (default)
    }

    private static void UpdateDmNote(float now) {
        // dm* runtime caches are refreshed by Apply()/ParseDmNoteSpecs() on every
        // settings change, so re-deriving them (8 reads + 8 Mathf.Clamp) every
        // frame here was pure waste — removed.

        while(pressLog.Count > 0 && now - pressLog.Peek() > 1f) {
            pressLog.Dequeue();
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

            bool rawPressed = KeyHeld(box.Key);
            bool blocked = box.Key != KeyCode.None && rawPressed && Features.KeyLimiter.KeyLimiter.ShouldBlockKey(box.Key);
            bool hidden = blocked && limiterMode == 0;
            bool rainOnly = blocked && limiterMode == 1;
            bool physicalPressed = rawPressed && !hidden && !rainOnly;
            bool ghostPressed = (rainOnly || KeyHeld(spec.GhostKeyCode)) && !hidden;

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
                    pressLog.Enqueue(now);
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
        // CSS animation runs in LateUpdate so it samples the press state set in
        // Update and recolours after TMP has regenerated its mesh this frame. A
        // finished font download (background thread) triggers one rebuild here.
        private void LateUpdate() {
            // A finished font/image download (background thread) → one rebuild.
            if(cssFontArrived || cssImageArrived) {
                cssFontArrived = false;
                cssImageArrived = false;
                if(Conf != null && Conf.IsDmNoteMode) {
                    Rebuild();
                    return;
                }
            }
            if(cssFx.Count > 0 && root != null && root.gameObject.activeSelf) {
                CssTick(Time.unscaledTime);
            }
        }

        private void Update() {
            if(root == null) {
                return;
            }

            bool isReorganizing = UICore.IsReorganizing;
            bool overlayVisible = (Panels.PanelsOverlay.IsEnabled && Conf.Enabled && (Conf.ShowOutsideGame || GameStats.InGame)) || isReorganizing;
            bool show = (Conf.IsSimpleMode || Conf.IsDmNoteMode) && overlayVisible;
            if(raycaster != null && raycaster.enabled != isReorganizing) {
                raycaster.enabled = isReorganizing;
            }
            if(root.gameObject.activeSelf != show) {
                root.gameObject.SetActive(show);
            }

            if(dragObj != null && dragObj.activeSelf != isReorganizing) {
                dragObj.SetActive(isReorganizing);
            }

            // Foot element: shown only in simple mode with foot keys configured,
            // and draggable on its own in Reorganize mode.
            bool footShow = show && Conf.IsSimpleMode && Conf.FootKeyCount() > 0;
            if(footRoot != null && footRoot.gameObject.activeSelf != footShow) {
                footRoot.gameObject.SetActive(footShow);
            }
            if(footDragObj != null) {
                bool footDragActive = isReorganizing && footShow;
                if(footDragObj.activeSelf != footDragActive) {
                    footDragObj.SetActive(footDragActive);
                }
            }

            if(!show) {
                return;
            }

            float now = Time.unscaledTime;

            if(Conf.IsDmNoteMode) {
                // Position only moves while dragging in Reorganize mode; gate the
                // writeback so it isn't a per-frame no-op round-trip otherwise.
                if(isReorganizing) {
                    Vector2 stored = OverlayCalibration.Unscale(root.anchoredPosition);
                    Conf.DmOffsetX = stored.x;
                    Conf.DmOffsetY = stored.y;
                }
                UpdateDmNote(now);
                return;
            }

            // Drag writes the position; mirror it back into the settings so it
            // persists. Only the drag (Reorganize mode) can move root. The foot
            // element is dragged independently and writes its own position.
            if(isReorganizing && footRoot != null) {
                Vector2 footStored = OverlayCalibration.Unscale(footRoot.anchoredPosition);
                Conf.FootOffsetX = footStored.x;
                Conf.FootOffsetY = footStored.y;
            }
            if(isReorganizing) {
                Vector2 stored = OverlayCalibration.Unscale(root.anchoredPosition);
                Conf.OffsetX = stored.x;
                Conf.OffsetY = stored.y;
            }

            // KPS window: drop presses older than one second.
            while(pressLog.Count > 0 && now - pressLog.Peek() > 1f) {
                pressLog.Dequeue();
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
                    // Streamer mode hides the KPS and Total boxes entirely.
                    bool statVisible = !Conf.StreamerMode;
                    if(box.Fill.gameObject.activeSelf != statVisible) {
                        box.Fill.gameObject.SetActive(statVisible);
                    }
                    if(!statVisible) {
                        continue;
                    }

                    // Per-box LastShown (not persistent updater fields), so the
                    // cache dies with the box on Rebuild/ResetCounts and a fresh
                    // box never gets stuck on "0" when its restored value happens
                    // to equal the pre-rebuild value.
                    int value = box.IsKps ? pressLog.Count : totalCount;
                    if(box.Value != null && box.LastShown != value) {
                        // Together mode renders the caption inline with the value.
                        box.Value.text = box.StatTogether
                            ? box.StatCaption + "  " + FormatCount(value)
                            : FormatCount(value);
                        box.LastShown = value;
                    }
                    continue;
                }

                bool pressed = KeyHeld(box.Key);
                if(pressed && !box.Pressed) {
                    // Foot keys light up but never add to the counters.
                    if(!box.IsFoot) {
                        box.Count++;
                        totalCount++;
                        pressLog.Enqueue(now);
                        // Only the per-key KPS readout drains box.KpsLog; when it's
                        // off (the default) nothing ever dequeues, so an unconditional
                        // enqueue grows the queue unbounded for the whole session.
                        if(Conf.PerKeyKps) {
                            box.KpsLog.Enqueue(now);
                        }
                        countsDirty = true;
                    }

                    if(Conf.RainEnabled && box.RainGroup != 0 && rainManager != null) {
                        box.LastRain = SpawnRain(box, now);
                    }
                } else if(!pressed && box.Pressed && box.LastRain != null) {
                    // Release: freeze the drop's trailing edge.
                    box.LastRain.EndTime = now;
                    box.LastRain = null;
                }

                // Ghost rain: a separate streak from the slot's secondary key,
                // ghost-coloured, with no effect on the press counters. Active
                // whenever the slot has a ghost key set (no separate enable).
                if(Conf.RainEnabled && box.RainGroup != 0
                    && rainManager != null && box.GhostKey != KeyCode.None) {
                    bool ghostPressed = KeyHeld(box.GhostKey);
                    if(ghostPressed && !box.GhostPressed) {
                        box.LastGhostRain = SpawnRain(box, now, ghost: true);
                    } else if(!ghostPressed && box.GhostPressed && box.LastGhostRain != null) {
                        box.LastGhostRain.EndTime = now;
                        box.LastGhostRain = null;
                    }
                    box.GhostPressed = ghostPressed;
                } else if(box.GhostPressed) {
                    // Ghost disabled mid-hold: close any open streak.
                    if(box.LastGhostRain != null) {
                        box.LastGhostRain.EndTime = now;
                        box.LastGhostRain = null;
                    }
                    box.GhostPressed = false;
                }

                if(pressed != box.Pressed) {
                    box.Pressed = pressed;
                    ApplyBoxColors(box);
                }

                if(box.Value == null) {
                    continue;
                }

                // Hide the per-key counter entirely if requested.
                bool countVisible = !Conf.HideMainKeyCount;
                if(box.Value.gameObject.activeSelf != countVisible) {
                    box.Value.gameObject.SetActive(countVisible);
                }
                if(!countVisible) {
                    continue;
                }

                if(Conf.PerKeyKps) {
                    // Per-key KPS: this key's presses in the last second. The
                    // window slides every frame, so recompute unconditionally.
                    while(box.KpsLog.Count > 0 && now - box.KpsLog.Peek() > 1f) {
                        box.KpsLog.Dequeue();
                    }
                    int kps = box.KpsLog.Count;
                    if(box.LastShown != kps) {
                        box.LastShown = kps;
                        box.Value.text = FormatCount(kps);
                    }
                } else if(box.Count != box.LastShown) {
                    box.LastShown = box.Count;
                    box.Value.text = FormatCount(box.Count);
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
