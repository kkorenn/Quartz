using System.Globalization;
using System.Text;
using Koren.Core;
using Koren.Features.Interop;
using Koren.Features.Status;
using Koren.IO;
using Koren.Resource;
using Koren.UI;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

using TMPro;

namespace Koren.Features.Panels;

// User-composed HUD panels. Each panel is a named, draggable box showing the
// stat lines the user put on it, with per-panel appearance settings. This
// replaces the old fixed Left/Right Status HUD — same live stats (from
// GameStats), but any number of panels, freely composed.
//
// Panels rebuild from config whenever the settings UI adds/removes one; stat
// text refreshes every frame while visible. In Reorganize mode every panel
// renders (its name when empty) and becomes draggable; positions persist
// per panel.
public static class PanelsOverlay {
    public static SettingsFile<PanelsSettings> ConfMgr { get; private set; }
    public static PanelsSettings Conf => ConfMgr?.Data;

    // Master "Enable Overlays" switch, null-safe for the other overlay
    // features (ProgressBar/Combo/Judgement) that gate on it.
    public static bool IsEnabled => ConfMgr?.Data is { Enabled: true };

    // ===== stat catalog =====

    public sealed class StatDef {
        public string Id;
        public string Label;
        // Settings-UI grouping (same categories the old fixed HUD page used).
        public string Category;
        // Returns the line's value text, or null to skip the line entirely.
        public Func<PanelConfig, string> Value;
    }

    public static readonly StatDef[] Catalog = [
        new() { Id = "progress", Category = "Accuracy", Label = "Progress", Value = p =>
            GameStats.RunHasStartProgress
                ? Pct(GameStats.RunStartProgress, p) + " - " + Pct(GameStats.Progress, p)
                : Pct(GameStats.Progress, p) },
        new() { Id = "accuracy", Category = "Accuracy", Label = "Accuracy", Value = p => Pct(GameStats.Accuracy, p) },
        new() { Id = "xaccuracy", Category = "Accuracy", Label = "X-Accuracy", Value = p => Pct(GameStats.XAccuracy, p) },
        new() { Id = "maxaccuracy", Category = "Accuracy", Label = "Max X-Acc", Value = p => Pct(GameStats.MaxXAccuracy, p) },
        new() { Id = "musictime", Category = "Time", Label = "Music Time", Value = _ => GameStats.MusicTimeText },
        new() { Id = "maptime", Category = "Time", Label = "Map Time", Value = _ => GameStats.MapTimeText },
        new() { Id = "checkpoints", Category = "Map Stats", Label = "Checkpoints", Value = _ =>
            GameStats.CheckpointCount.ToString(CultureInfo.InvariantCulture) },
        new() { Id = "tbpm", Category = "BPM", Label = "TBPM", Value = _ => Bpm(true) },
        new() { Id = "cbpm", Category = "BPM", Label = "CBPM", Value = _ => Bpm(false) },
        new() { Id = "kps", Category = "BPM", Label = "KPS", Value = _ => {
            GameStats.GetBpm(out float tbpm, out float cbpm);
            return (cbpm / 60f).ToString("0.##", CultureInfo.InvariantCulture);
        } },
        new() { Id = "autokps", Category = "BPM", Label = "Auto KPS", Value = _ =>
            GameStats.AutoKps.ToString(CultureInfo.InvariantCulture) },
        new() { Id = "hold", Category = "Other", Label = "Holds", Value = _ => {
            string hold = GameStats.HoldBehaviorLabel;
            return string.IsNullOrEmpty(hold) ? null : hold;
        } },
        new() { Id = "timingscale", Category = "Other", Label = "Timing Scale", Value = _ =>
            (GameStats.MarginScale * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%" },
        new() { Id = "pitch", Category = "Other", Label = "Pitch", Value = _ =>
            (GameStats.Pitch * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%" },
        new() { Id = "attempt", Category = "Map Stats", Label = "Attempt", Value = _ =>
            GameStats.SessionAttempts.ToString(CultureInfo.InvariantCulture) },
        new() { Id = "totalattempts", Category = "Map Stats", Label = "Total Attempts", Value = _ =>
            GameStats.TotalAttempts.ToString(CultureInfo.InvariantCulture) },
        new() { Id = "best", Category = "Map Stats", Label = "Best", Value = p => {
            // Checkpoint bests render as a "start - best" range, like Progress.
            float start = GameStats.BestStart;
            return start > 0.0001f
                ? Pct(start, p) + " - " + Pct(GameStats.Best, p)
                : Pct(GameStats.Best, p);
        } },
        new() { Id = "fps", Category = "Other", Label = "FPS", Value = _ =>
            GameStats.Fps.ToString(CultureInfo.InvariantCulture) },
        // XPerfect perfect breakdown. Value returns null (line hidden) unless the
        // XPerfect mod is active, so the panel only shows them when meaningful.
        new() { Id = "xperfect", Category = "Accuracy", Label = "X Perfect", Value = _ =>
            XPerfectBridge.Active ? GameStats.XPerfectX.ToString(CultureInfo.InvariantCulture) : null },
        new() { Id = "plusperfect", Category = "Accuracy", Label = "+ Perfect", Value = _ =>
            XPerfectBridge.Active ? GameStats.XPerfectPlus.ToString(CultureInfo.InvariantCulture) : null },
        new() { Id = "minusperfect", Category = "Accuracy", Label = "- Perfect", Value = _ =>
            XPerfectBridge.Active ? GameStats.XPerfectMinus.ToString(CultureInfo.InvariantCulture) : null },
    ];

    public static string LocalizedStatLabel(StatDef stat)
        => stat == null ? "" : MainCore.Tr.Get(LocaleKey("PANEL_STAT_", stat.Id), stat.Label);

    // The text drawn between a stat's label and its value. Stored raw, padded
    // here so a tidy single-character separator doesn't need manual spaces:
    //   ""          -> a single space
    //   "|"  (1 ch) -> " | "  (a space added each side)
    //   "::" (2+ ch) -> used verbatim (the user supplied their own spacing)
    internal static string EffectiveSeparator(string raw) {
        if(string.IsNullOrEmpty(raw)) {
            return " ";
        }
        return raw.Length == 1 ? " " + raw + " " : raw;
    }

    public static string LocalizedCategory(string category)
        => MainCore.Tr.Get(LocaleKey("PANEL_CATEGORY_", category), category);

    private static string LocaleKey(string prefix, string id) {
        if(string.IsNullOrWhiteSpace(id)) {
            return prefix;
        }

        StringBuilder key = new(prefix);
        bool lastUnderscore = false;
        foreach(char raw in id.Trim().ToUpperInvariant()) {
            char c = char.IsLetterOrDigit(raw) ? raw : '_';
            if(c == '_') {
                if(lastUnderscore) {
                    continue;
                }
                lastUnderscore = true;
            } else {
                lastUnderscore = false;
            }
            key.Append(c);
        }

        while(key.Length > prefix.Length && key[^1] == '_') {
            key.Length--;
        }

        return key.ToString();
    }

    private static string Bpm(bool tile) {
        GameStats.GetBpm(out float tbpm, out float cbpm);
        return (tile ? tbpm : cbpm).ToString("0.##", CultureInfo.InvariantCulture);
    }

    // Precomputed "0", "0.0" … "0.000000" so Pct (called per stat per panel per
    // frame) doesn't rebuild `"0." + new string('0', d)` on every call.
    private static readonly string[] PctFormats = {
        "0", "0.0", "0.00", "0.000", "0.0000", "0.00000", "0.000000"
    };

    private static string Pct(float ratio, PanelConfig p) {
        if(float.IsNaN(ratio) || float.IsInfinity(ratio)) {
            ratio = 0f;
        }

        int d = Mathf.Clamp(p.Decimals, 0, 6);
        return (ratio * 100f).ToString(PctFormats[d], CultureInfo.InvariantCulture) + "%";
    }

    // ===== lifecycle =====

    private const float PadX = 14f;
    private const float PadY = 10f;

    private static GameObject canvasObj;
    private static readonly List<LivePanel> panels = [];
    private static Updater updater;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<PanelsSettings>(
            Path.Combine(MainCore.Paths.RootPath, "OverlayPanels.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.Save();

    public static void Initialize(GameObject root) {
        if(canvasObj != null) {
            return;
        }

        EnsureConf();

        canvasObj = new GameObject("KorenPanelsCanvas");
        canvasObj.transform.SetParent(root.transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        BuildPanels();

        updater = canvasObj.AddComponent<Updater>();
    }

    public static void Dispose() {
        if(canvasObj == null) {
            return;
        }

        SyncPositionsToConfig();
        ConfMgr?.Save();

        Object.Destroy(canvasObj);
        canvasObj = null;
        panels.Clear();
        updater = null;
    }

    // Tears the live panel objects down and rebuilds them from config —
    // called from the settings UI after add/delete/rename. skipPositionSync:
    // a panel whose config position was just rewritten (anchor change) and
    // must not be clobbered by its live rect's old-anchor position.
    public static void Rebuild(PanelConfig skipPositionSync = null) {
        if(canvasObj == null) {
            return;
        }

        SyncPositionsToConfig(skipPositionSync);

        foreach(LivePanel p in panels) {
            if(p.Rect != null) {
                Object.Destroy(p.Rect.gameObject);
            }
        }
        panels.Clear();

        BuildPanels();
        Apply();
    }

    // Changes a panel's anchor preset. Offsets are relative to the anchor, so
    // the old offset is meaningless at the new corner — snap to the new
    // corner's default inset. The rebuild skips syncing this panel: its live
    // rect still holds the old-anchor position, which would overwrite the
    // fresh default and strand the panel off-screen.
    public static void SetAnchor(PanelConfig config, PanelAnchor anchor) {
        config.Anchor = (int)anchor;
        Vector2 def = PanelConfig.DefaultOffset(anchor);
        config.PosX = def.x;
        config.PosY = def.y;
        Save();
        Rebuild(config);
    }

    private static void BuildPanels() {
        List<PanelConfig> configs = Conf.Panels;
        for(int i = 0; i < configs.Count; i++) {
            panels.Add(CreatePanel(configs[i]));
        }
    }

    // Re-applies appearance settings to the live panels (UI change).
    public static void Apply() {
        foreach(LivePanel p in panels) {
            ApplyPanel(p);
        }
    }

    private static void ApplyPanel(LivePanel p) {
        if(p?.Config == null) {
            return;
        }

        if(p.Text != null) {
            p.Text.font = FontManager.Current;
            p.Text.fontSize = p.Config.FontSize;
            p.Text.color = p.Config.GetTextColor();
            p.Text.lineSpacing = p.Config.LineSpacing;
            TMPTextShadow.Apply(
                p.Text,
                p.Config.TextShadowEnabled,
                p.Config.TextShadowX,
                p.Config.TextShadowY,
                p.Config.TextShadowSoftness,
                p.Config.GetTextShadowColor()
            );
            // FontSize/lineSpacing change the preferred size, which UpdatePanel
            // only recomputes when the body changes — force one re-measure.
            p.Dirty = true;
        }

        if(p.Background != null) {
            p.Background.enabled = p.Config.BackgroundEnabled;
        }
    }

    public static void ResetPosition(PanelConfig config) {
        Vector2 def = PanelConfig.DefaultOffset((PanelAnchor)config.Anchor);
        config.PosX = def.x;
        config.PosY = def.y;

        foreach(LivePanel p in panels) {
            if(p.Config == config && p.Rect != null) {
                p.Rect.anchoredPosition = new Vector2(config.PosX, config.PosY);
            }
        }

        Save();
    }

    private static void SyncPositionsToConfig(PanelConfig skip = null) {
        foreach(LivePanel p in panels) {
            if(p.Rect != null && p.Config != null && p.Config != skip) {
                p.Config.PosX = p.Rect.anchoredPosition.x;
                p.Config.PosY = p.Rect.anchoredPosition.y;
            }
        }
    }

    private static LivePanel CreatePanel(PanelConfig config) {
        GameObject panelObj = new("Panel_" + config.Name);
        panelObj.transform.SetParent(canvasObj.transform, false);

        Vector2 anchor = PanelConfig.AnchorVector((PanelAnchor)config.Anchor);

        RectTransform rect = panelObj.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = new Vector2(config.PosX, config.PosY);

        Image bg = panelObj.AddComponent<Image>();
        bg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P1024);
        bg.type = Image.Type.Sliced;
        bg.color = UIColors.PanelBG;
        bg.raycastTarget = false;

        GameObject drag = new("Drag");
        drag.transform.SetParent(rect, false);
        RectTransform dragRect = drag.AddComponent<RectTransform>();
        dragRect.anchorMin = Vector2.zero;
        dragRect.anchorMax = Vector2.one;
        dragRect.offsetMin = Vector2.zero;
        dragRect.offsetMax = Vector2.zero;
        drag.AddComponent<EmptyGraphic>().raycastTarget = true;
        ReorganizeHandle handle = drag.AddComponent<ReorganizeHandle>();
        handle.Target = rect;
        handle.GetName = () => config.Name;
        handle.OnMoved = Save;
        drag.SetActive(false);

        GameObject textObj = new("Text");
        textObj.transform.SetParent(rect, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(PadX, PadY);
        textRect.offsetMax = new Vector2(-PadX, -PadY);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.font = FontManager.Current;
        // Text follows the anchor's horizontal side, like the old left/right
        // panels did (right-anchored panels read right-aligned).
        text.alignment = anchor.x switch {
            0f => TextAlignmentOptions.TopLeft,
            1f => TextAlignmentOptions.TopRight,
            _ => TextAlignmentOptions.Top,
        };
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = "";

        LivePanel panel = new() {
            Config = config,
            Rect = rect,
            Background = bg,
            DragObj = drag,
            Text = text,
        };

        ApplyPanel(panel);
        return panel;
    }

    private sealed class LivePanel {
        public PanelConfig Config;
        public RectTransform Rect;
        public Image Background;
        public GameObject DragObj;
        public TextMeshProUGUI Text;

        // Per-frame change-guard state (see UpdatePanel). LastBody = null forces
        // the first render; Dirty is raised by Apply()/reactivation to force one
        // re-measure when appearance changed but the body string did not.
        public string LastBody;
        public bool Dirty = true;
    }

    private sealed class Updater : MonoBehaviour {
        private readonly StringBuilder sb = new();

        private void Update() {
            bool isReorganizing = UICore.IsReorganizing;
            bool show = (IsEnabled && GameStats.InGame) || isReorganizing;

            foreach(LivePanel p in panels) {
                UpdatePanel(p, show, isReorganizing);
            }
        }

        private void UpdatePanel(LivePanel p, bool show, bool isReorganizing) {
            if(p?.Text == null || p.Rect == null) {
                return;
            }

            sb.Clear();

            if(show) {
                PanelConfig c = p.Config;
                if(!string.IsNullOrEmpty(c.Prefix)) {
                    sb.AppendLine(c.Prefix);
                }

                for(int i = 0; i < c.Stats.Count; i++) {
                    StatEntry entry = c.Stats[i];
                    if(!entry.Enabled) {
                        continue;
                    }

                    StatDef stat = FindStat(entry.Id);
                    if(stat == null) {
                        continue;
                    }

                    string value;
                    try { value = stat.Value(c); }
                    catch { continue; }
                    if(value == null) {
                        continue;
                    }

                    // English by default; localized only when this panel opts
                    // in (the settings UI always shows localized labels though).
                    // Skipped entirely when the entry hides its label (number only).
                    if(entry.ShowLabel) {
                        string label = c.LocalizeStatLabels
                            ? LocalizedStatLabel(stat)
                            : stat.Label;
                        sb.Append(label).Append(EffectiveSeparator(c.LabelSeparator));
                    }

                    // Per-stat value coloring (v1 ColorRange): tint the value
                    // by the stat's own ratio through the entry's gradient.
                    StatColor color = entry.Color;
                    if(color is { Enabled: true }) {
                        Color tint = color.Evaluate(ColorRatio(entry.Id, color));
                        sb.Append("<color=#");
                        AppendHex(sb, tint);
                        sb.Append('>').Append(value).AppendLine("</color>");
                    } else {
                        sb.AppendLine(value);
                    }
                }
            }

            string body = TrimmedBody(sb);
            // Reorganize mode forces an empty panel to render its name so the
            // user has a hit target to grab.
            if(isReorganizing && body.Length == 0) {
                body = p.Config.Name;
            }

            bool active = body.Length > 0 || isReorganizing;
            if(p.Rect.gameObject.activeSelf != active) {
                p.Rect.gameObject.SetActive(active);
                // Re-show: shadow layers may have been disabled while hidden, so
                // force a full text + shadow re-sync on the next applied frame.
                if(active) {
                    p.Dirty = true;
                }
            }

            if(p.DragObj != null && p.DragObj.activeSelf != isReorganizing) {
                p.DragObj.SetActive(isReorganizing);
            }

            if(!active) {
                return;
            }

            TMP_FontAsset font = FontManager.Current;
            bool fontChanged = p.Text.font != font;
            if(fontChanged) {
                p.Text.font = font;
            }

            // Only re-tessellate the TMP mesh, re-measure, and re-sync the drop
            // shadow when something actually changed. For static-stat panels
            // (FPS/BPM/checkpoints/attempts) the body is identical frame-to-frame,
            // so this skips a full mesh rebuild + GetPreferredValues + shadow
            // re-apply every frame — the per-frame TMP churn that caused stutter.
            if(fontChanged || p.Dirty || body != p.LastBody) {
                p.Text.text = body;
                Vector2 pref = p.Text.GetPreferredValues(body);
                p.Rect.sizeDelta = new Vector2(pref.x + PadX * 2f, pref.y + PadY * 2f);
                TMPTextShadow.Apply(
                    p.Text,
                    p.Config.TextShadowEnabled,
                    p.Config.TextShadowX,
                    p.Config.TextShadowY,
                    p.Config.TextShadowSoftness,
                    p.Config.GetTextShadowColor()
                );
                p.LastBody = body;
                p.Dirty = false;
            }

            // Position only changes in Reorganize mode (drag); writing it back
            // every frame otherwise is a no-op round-trip against Apply()'s value.
            if(isReorganizing) {
                p.Config.PosX = p.Rect.anchoredPosition.x;
                p.Config.PosY = p.Rect.anchoredPosition.y;
            }
        }

        private static StatDef FindStat(string id) {
            for(int i = 0; i < Catalog.Length; i++) {
                if(Catalog[i].Id == id) {
                    return Catalog[i];
                }
            }
            return null;
        }

        // Appends `tint` as 8 uppercase hex chars (RRGGBBAA) straight into the
        // builder — same output as ColorUtility.ToHtmlStringRGBA, but without the
        // intermediate string it allocates per colored stat per frame.
        private static void AppendHex(StringBuilder sb, Color tint) {
            Color32 c = tint;
            AppendHexByte(sb, c.r);
            AppendHexByte(sb, c.g);
            AppendHexByte(sb, c.b);
            AppendHexByte(sb, c.a);
        }

        private static void AppendHexByte(StringBuilder sb, byte b) {
            const string hex = "0123456789ABCDEF";
            sb.Append(hex[b >> 4]).Append(hex[b & 0xF]);
        }

        // sb.ToString().TrimEnd() with one allocation instead of two: scan past
        // trailing whitespace, then copy once. Byte-identical to the old result.
        private static string TrimmedBody(StringBuilder sb) {
            int len = sb.Length;
            while(len > 0 && char.IsWhiteSpace(sb[len - 1])) {
                len--;
            }
            return len == 0 ? "" : sb.ToString(0, len);
        }

        // The 0..1 value that drives a stat's color gradient — mirrors which
        // ratio v1 fed each ColorRange. Stats without a moving ratio sit at
        // the top of the gradient (static color).
        private static float ColorRatio(string id, StatColor color) {
            try {
                switch(id) {
                    case "progress": return GameStats.Progress;
                    case "accuracy": return GameStats.Accuracy;
                    case "xaccuracy": return GameStats.XAccuracy;
                    case "maxaccuracy": return GameStats.MaxXAccuracy;
                    case "musictime": return GameStats.MusicTimeRatio;
                    case "maptime": return GameStats.MapTimeRatio;
                    case "best": return GameStats.Best;

                    case "tbpm": {
                        GameStats.GetBpm(out float tbpm, out _);
                        return color.MaxBpm <= 0f ? 0f : tbpm / color.MaxBpm;
                    }

                    // v1 colored KPS and Auto KPS with the current-BPM color.
                    case "cbpm":
                    case "kps":
                    case "autokps": {
                        GameStats.GetBpm(out _, out float cbpm);
                        return color.MaxBpm <= 0f ? 0f : cbpm / color.MaxBpm;
                    }

                    default: return 1f;
                }
            } catch {
                return 1f;
            }
        }
    }
}
