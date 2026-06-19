using System.Globalization;
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

namespace Koren.Features.Judgement;

// Bottom-center judgement counts HUD: a row of nine colored counters, one per
// judgement slot, centered on the Perfect count, sitting just above the
// bottom edge like the original (v1 placed it at screenHeight - margin -
// fontPx - offset). Ported from v1's IMGUI to uGUI — a HorizontalLayoutGroup
// row replaces the manual width measuring, so the row self-centers as counts
// grow. Draggable in Reorganize mode like the other HUD elements.
public static class JudgementOverlay {
    public static SettingsFile<JudgementSettings> ConfMgr { get; private set; }
    public static JudgementSettings Conf => ConfMgr.Data;

    // v1 drew at 3.5% of frame height (≈38px at 1080) before user scaling.
    private const float BaseFontSize = 38f;
    // v1: max(4, screenHeight * 0.006) above the bottom edge ≈ 6.5 at 1080.
    private const float BottomMargin = 6.5f;

    private static GameObject canvasObj;
    private static RectTransform root;
    private static HorizontalLayoutGroup rowLayout;
    private static readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[Judgement.Slots];
    private static GameObject dragObj;
    private static Updater updater;

    // XPerfect judgement line: when the XPerfect mod is active, the Perfect slot
    // (index 4) shows the X (dead-center) count in cyan, flanked by a +Perfect
    // count on its left and a -Perfect count on its right, both green — exactly
    // v1's layout. The HorizontalLayoutGroup positions them by sibling order, so
    // these two extra labels are inserted around slot 4 and simply toggled on/off.
    private const int PerfectSlot = 4;
    private static TextMeshProUGUI xPlusLabel;
    private static TextMeshProUGUI xMinusLabel;
    private static readonly Color XPerfectColor = new(0.30f, 0.80f, 1f, 1f);
    private static readonly Color PlusMinusPerfectColor = new(0.38f, 1f, 0.31f, 1f);

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<JudgementSettings>(
            Path.Combine(MainCore.Paths.RootPath, "Judgement.json")
        );
        ConfMgr.Load();
    }

    public static void Initialize(GameObject rootObject) {
        if(canvasObj != null) {
            return;
        }

        EnsureConf();

        canvasObj = new GameObject("KorenJudgementCanvas");
        canvasObj.transform.SetParent(rootObject.transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Between the progress bar (32755) and the combo counter (32757).
        canvas.sortingOrder = 32756;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject rowObj = new("JudgementRow");
        rowObj.transform.SetParent(canvasObj.transform, false);
        root = rowObj.AddComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 0f);
        root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0f);

        rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        ContentSizeFitter fit = rowObj.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        for(int i = 0; i < labels.Length; i++) {
            GameObject obj = new("Judgement_" + i);
            obj.transform.SetParent(rowObj.transform, false);
            obj.AddComponent<RectTransform>();

            TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
            text.font = FontManager.Current;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Judgement.SlotColors[i];
            text.raycastTarget = false;
            text.text = "0";
            labels[i] = text;
        }

        // XPerfect +/- labels. Sibling order sets layout position: insert +Perfect
        // just before the Perfect slot and -Perfect just after it, giving the row
        // [... EarlyPerfect, +Perfect, X, -Perfect, LatePerfect ...] when active.
        xPlusLabel = CreateJudgementLabel("Judgement_XPlus", PlusMinusPerfectColor);
        xMinusLabel = CreateJudgementLabel("Judgement_XMinus", PlusMinusPerfectColor);
        xPlusLabel.transform.SetSiblingIndex(PerfectSlot);       // before slot 4
        xMinusLabel.transform.SetSiblingIndex(PerfectSlot + 2);  // after slot 4
        xPlusLabel.gameObject.SetActive(false);
        xMinusLabel.gameObject.SetActive(false);

        GameObject drag = new("Drag");
        dragObj = drag;
        drag.transform.SetParent(root, false);
        RectTransform dragRect = drag.AddComponent<RectTransform>();
        dragRect.anchorMin = Vector2.zero;
        dragRect.anchorMax = Vector2.one;
        dragRect.offsetMin = Vector2.zero;
        dragRect.offsetMax = Vector2.zero;
        // The row layout group must not size the drag surface — without this
        // it gets laid out to zero width and the overlay can't be grabbed.
        drag.AddComponent<LayoutElement>().ignoreLayout = true;
        drag.AddComponent<EmptyGraphic>().raycastTarget = true;
        ReorganizeHandle handle = drag.AddComponent<ReorganizeHandle>();
        handle.Target = root;
        handle.GetName = () => MainCore.Tr.Get("JUDGEMENT", "Judgement");
        handle.OnMoved = Save;
        drag.SetActive(false);

        updater = canvasObj.AddComponent<Updater>();

        Apply();
    }

    public static void Apply() {
        if(root == null) {
            return;
        }

        root.anchoredPosition = OverlayCalibration.Scale(new Vector2(Conf.OffsetX, BottomMargin + Conf.OffsetY));
        rowLayout.spacing = RowSpacing();

        float fontSize = FontSize();
        foreach(TextMeshProUGUI label in labels) {
            ApplyTextStyle(label, fontSize);
        }
        ApplyTextStyle(xPlusLabel, fontSize);
        ApplyTextStyle(xMinusLabel, fontSize);
    }

    private static float FontSize() => BaseFontSize * Mathf.Clamp(Conf.Size, 0.3f, 3f);

    // v1: a small font-relative base gap plus the user spacing.
    private static float RowSpacing() => Mathf.Max(3f, FontSize() * 0.07f) + Mathf.Clamp(Conf.Spacing, -20f, 80f);

    private static void ApplyTextStyle(TextMeshProUGUI label, float fontSize) {
        if(label == null) {
            return;
        }

        label.fontSize = fontSize;
        TMPTextShadow.Apply(
            label,
            Conf.TextShadowEnabled,
            Conf.TextShadowX,
            Conf.TextShadowY,
            Conf.TextShadowSoftness,
            Conf.GetTextShadowColor()
        );
    }

    private static TextMeshProUGUI CreateJudgementLabel(string name, Color color) {
        GameObject obj = new(name);
        obj.transform.SetParent(root, false);
        obj.AddComponent<RectTransform>();

        TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
        text.font = FontManager.Current;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        text.text = "0";
        return text;
    }

    public static void Save() => ConfMgr?.Save();

    public static void ResetPosition() {
        JudgementSettings def = new();
        Conf.OffsetX = def.OffsetX;
        Conf.OffsetY = def.OffsetY;
        Apply();
        Save();
    }

    public static void Dispose() {
        if(canvasObj == null) {
            return;
        }

        ConfMgr?.Save();

        Object.Destroy(canvasObj);
        canvasObj = null;
        root = null;
        rowLayout = null;
        System.Array.Clear(labels, 0, labels.Length);
        xPlusLabel = null;
        xMinusLabel = null;
        dragObj = null;
        updater = null;
    }

    private sealed class Updater : MonoBehaviour {
        private readonly int[] cached = new int[Judgement.Slots];
        private int cachedPlus = -1;
        private int cachedMinus = -1;
        private bool lastXpMode;
        private bool cacheValid;
        private float lastFontSize = float.NaN;
        private TMP_FontAsset lastFont;

        private void Update() {
            if(root == null) {
                return;
            }

            bool isReorganizing = UICore.IsReorganizing;
            bool show = (Panels.PanelsOverlay.IsEnabled && Conf.Enabled && GameStats.InGame) || isReorganizing;
            if(root.gameObject.activeSelf != show) {
                root.gameObject.SetActive(show);
            }

            if(dragObj != null && dragObj.activeSelf != isReorganizing) {
                dragObj.SetActive(isReorganizing);
            }

            if(!show) {
                return;
            }

            // Position only changes while dragging in Reorganize mode; mirroring
            // it back into Conf every frame otherwise is a no-op round-trip.
            if(isReorganizing) {
                Vector2 stored = OverlayCalibration.Unscale(root.anchoredPosition);
                Conf.OffsetX = stored.x;
                Conf.OffsetY = stored.y - BottomMargin;
            }

            TMP_FontAsset font = FontManager.Current;
            float fontSize = FontSize();
            rowLayout.spacing = RowSpacing();

            // XPerfect splits the Perfect slot into X / +Perfect / -Perfect. A
            // mode change re-texts and recolors slot 4 and toggles the +/- labels.
            // Gated on the user setting so it can be collapsed back to a single
            // Perfect count even while the XPerfect mod is installed.
            bool xpMode = Conf.ShowXPerfect && XPerfectBridge.Active;
            bool xpModeChanged = xpMode != lastXpMode;

            // First pass: set everything that feeds the layout (font, size,
            // text). Shadows are synced separately, after the layout settles.
            // `changed` also folds in font/size/mode changes so the second pass
            // and layout rebuild still fire on a real font, size or mode edit.
            bool changed = !cacheValid || fontSize != lastFontSize || font != lastFont || xpModeChanged;
            for(int i = 0; i < labels.Length; i++) {
                TextMeshProUGUI label = labels[i];
                if(label.font != font) {
                    label.font = font;
                }
                if(label.fontSize != fontSize) {
                    label.fontSize = fontSize;
                }

                // Slot 4 shows the X (dead-center) count under XPerfect, the
                // combined Perfect+Auto count otherwise.
                int count = i == PerfectSlot && xpMode ? XPerfectBridge.XCount() : Judgement.SlotCount(i);
                if(!cacheValid || count != cached[i] || xpModeChanged) {
                    cached[i] = count;
                    label.text = count.ToString(CultureInfo.InvariantCulture);
                    changed = true;
                }
            }

            UpdateXPerfectLabels(xpMode, xpModeChanged, font, fontSize, ref changed);

            if(xpModeChanged) {
                labels[PerfectSlot].color = xpMode ? XPerfectColor : Judgement.SlotColors[PerfectSlot];
            }

            cacheValid = true;
            lastFontSize = fontSize;
            lastFont = font;
            lastXpMode = xpMode;

            // The HorizontalLayoutGroup repositions the labels when a digit
            // count (or size) changes, but that rebuild only lands after
            // Update. The shadow copies each label's rect, so without forcing
            // the rebuild now it would read last frame's positions and ghost a
            // second set of digits for one frame on every hit. Both the rebuild
            // and the shadow re-sync only need to run when something changed —
            // on a no-hit frame (the common case) the geometry is identical, so
            // re-syncing 9 labels' shadows (sibling scans + material churn) every
            // frame was pure waste. Live shadow edits go through Apply().
            if(changed) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(root);

                // Second pass: sync the shadow against the now-final geometry.
                for(int i = 0; i < labels.Length; i++) {
                    ApplyTextStyle(labels[i], fontSize);
                }
                if(xpMode) {
                    ApplyTextStyle(xPlusLabel, fontSize);
                    ApplyTextStyle(xMinusLabel, fontSize);
                }
            }
        }

        // Drives the +Perfect / -Perfect labels: toggled with XPerfect mode,
        // showing the running +/- counts beside the Perfect slot. Their green
        // color is set once at creation, so only text + active state move here.
        private void UpdateXPerfectLabels(
            bool xpMode, bool xpModeChanged, TMP_FontAsset font, float fontSize, ref bool changed
        ) {
            if(xPlusLabel == null || xMinusLabel == null) {
                return;
            }

            if(xPlusLabel.gameObject.activeSelf != xpMode) {
                xPlusLabel.gameObject.SetActive(xpMode);
                xMinusLabel.gameObject.SetActive(xpMode);
                changed = true;
            }

            if(!xpMode) {
                return;
            }

            if(xPlusLabel.font != font) {
                xPlusLabel.font = font;
            }
            if(xMinusLabel.font != font) {
                xMinusLabel.font = font;
            }
            if(xPlusLabel.fontSize != fontSize) {
                xPlusLabel.fontSize = fontSize;
            }
            if(xMinusLabel.fontSize != fontSize) {
                xMinusLabel.fontSize = fontSize;
            }

            int plus = XPerfectBridge.PlusCount();
            if(!cacheValid || plus != cachedPlus || xpModeChanged) {
                cachedPlus = plus;
                xPlusLabel.text = plus.ToString(CultureInfo.InvariantCulture);
                changed = true;
            }

            int minus = XPerfectBridge.MinusCount();
            if(!cacheValid || minus != cachedMinus || xpModeChanged) {
                cachedMinus = minus;
                xMinusLabel.text = minus.ToString(CultureInfo.InvariantCulture);
                changed = true;
            }
        }
    }
}
