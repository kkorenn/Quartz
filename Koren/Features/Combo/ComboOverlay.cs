using System.Globalization;
using Koren.Core;
using Koren.Features.Interop;
using Koren.Features.ProgressBar;
using Koren.Features.Status;
using Koren.IO;
using Koren.Resource;
using Koren.UI;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

using TMPro;

namespace Koren.Features.Combo;

// Center-screen combo counter. Mirrors the original KorenResourcePack layout:
// large value text anchored below the progress bar, horizontally centered,
// with an optional caption label underneath.
public static class ComboOverlay {
    public static SettingsFile<ComboSettings> ConfMgr { get; private set; }
    public static ComboSettings Conf => ConfMgr.Data;

    private static GameObject canvasObj;
    private static RectTransform root;
    private static TextMeshProUGUI valueText;
    private static TextMeshProUGUI captionText;
    private static GameObject dragObj;
    private static Updater updater;

    private const float VerticalGap = 32f;
    private const float CaptionGap = 24f;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<ComboSettings>(
            Path.Combine(MainCore.Paths.RootPath, "Combo.json")
        );
        ConfMgr.Load();
    }

    public static void Initialize(GameObject rootObject) {
        if(canvasObj != null) {
            return;
        }

        EnsureConf();

        canvasObj = new GameObject("KorenComboCanvas");
        canvasObj.transform.SetParent(rootObject.transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32757;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject rootObj = new("ComboRoot");
        rootObj.transform.SetParent(canvasObj.transform, false);
        root = rootObj.AddComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 1f);
        root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);

        valueText = CreateLabel(root, "Value", TextAlignmentOptions.Center);
        captionText = CreateLabel(root, "Caption", TextAlignmentOptions.Center);

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
        handle.GetName = () => MainCore.Tr.Get("COMBO", "Combo");
        handle.OnMoved = Save;
        drag.SetActive(false);

        updater = canvasObj.AddComponent<Updater>();

        Apply();
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, TextAlignmentOptions align) {
        GameObject obj = new(name);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);

        TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
        text.font = FontManager.Current;
        text.alignment = align;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = "";
        return text;
    }

    public static void Apply() {
        if(root == null) {
            return;
        }

        ApplyFont();
        root.anchoredPosition = GetDefaultPosition();
        root.localScale = Vector3.one * Mathf.Max(0.01f, Conf.MasterSize);
        ApplyCaption();
        ApplyValueMaterial();
        ApplyCaptionMaterial();
    }

    private static void ApplyFont() {
        TMP_FontAsset font = FontManager.Current;
        if(valueText != null) {
            valueText.font = font;
        }
        if(captionText != null) {
            captionText.font = font;
        }
    }

    private static void ApplyCaption() {
        if(captionText == null) {
            return;
        }

        // XPerfect combo mode prefixes the caption with "X" (e.g. "XCombo") to
        // signal only dead-center perfects are being counted.
        string caption = Conf.CaptionText ?? "Combo";
        if(Conf.XPerfectComboEnabled && XPerfectBridge.Active) {
            caption = "X" + caption;
        }
        captionText.text = caption;
        captionText.gameObject.SetActive(Conf.ShowCaption);
    }

    private static Vector2 GetDefaultPosition() {
        ProgressBarOverlay.EnsureConf();
        ProgressBarSettings bar = ProgressBarOverlay.Conf;
        float y = -(bar.TopOffset + bar.Height + VerticalGap + Conf.OffsetY);
        return OverlayCalibration.Scale(new Vector2(Conf.OffsetX, y));
    }

    public static void Save() => ConfMgr?.Save();

    public static void ResetPosition() {
        ComboSettings def = new();
        Conf.OffsetX = def.OffsetX;
        Conf.OffsetY = def.OffsetY;
        Apply();
        Save();
    }

    public static void ApplyCountShadow() => ApplyValueMaterial();

    public static void ApplyCaptionShadow() => ApplyCaptionMaterial();

    public static void Dispose() {
        if(canvasObj == null) {
            return;
        }

        if(root != null) {
            Vector2 stored = OverlayCalibration.Unscale(root.anchoredPosition);
            Conf.OffsetX = stored.x;
            Conf.OffsetY = GetOffsetYFromPosition(stored.y);
        }

        ConfMgr?.Save();

        Object.Destroy(canvasObj);
        canvasObj = null;
        root = null;
        valueText = null;
        captionText = null;
        dragObj = null;
        updater = null;
    }

    private static float GetOffsetYFromPosition(float anchoredY) {
        ProgressBarOverlay.EnsureConf();
        ProgressBarSettings bar = ProgressBarOverlay.Conf;
        // Exact inverse of GetDefaultPosition's y = -(B + OffsetY), i.e.
        // OffsetY = -(anchoredY + B). The old "+(anchoredY + B)" had the wrong
        // sign, so every reorganize/Dispose writeback negated OffsetY and the
        // combo crept to the top of the screen across launches.
        return -(anchoredY + bar.TopOffset + bar.Height + VerticalGap);
    }

    private static void ApplyValueMaterial() {
        if(valueText == null) {
            return;
        }
        ApplyThickness(valueText, Conf.CountThickness);
        TMPTextShadow.Apply(
            valueText,
            Conf.CountShadowEnabled,
            Conf.CountShadowX,
            Conf.CountShadowY,
            Conf.CountShadowSoftness,
            Conf.GetCountShadowColor()
        );
    }

    private static void ApplyCaptionMaterial() {
        if(captionText == null) {
            return;
        }
        TMPTextShadow.Apply(
            captionText,
            Conf.CaptionShadowEnabled,
            Conf.CaptionShadowX,
            Conf.CaptionShadowY,
            Conf.CaptionShadowSoftness,
            Conf.GetCaptionShadowColor()
        );
    }

    // TMP face dilate — thickens the glyph strokes. 0 = native weight.
    private static void ApplyThickness(TextMeshProUGUI text, float dilate) {
        Material mat = text.fontMaterial;
        if(mat == null) {
            return;
        }
        mat.SetFloat("_FaceDilate", Mathf.Clamp(dilate, -1f, 1f));
    }

    private sealed class Updater : MonoBehaviour {
        private int cachedCount = -1;
        // Last-applied state for the idle change-guard (NaN/default forces the
        // first frame to apply). Lets Update skip the TMP re-measure + shadow
        // re-apply when the combo number, size and color are all unchanged.
        private float lastValueSize = float.NaN;
        private Color lastColor;
        private bool lastCaptionShown;
        private float lastCaptionSize = float.NaN;
        private float lastLabelKick = float.NaN;
        private float lastCaptionOffsetY = float.NaN;
        private float lastBlockH = float.NaN;
        // Cached value-text preferred size per point, so the pulse (which only
        // scales the size, not the text) doesn't pay a full GetPreferredValues
        // layout pass every frame — re-measured only when the digits change.
        private Vector2 prefPerPoint;
        // Same per-point cache for the caption: the pulse only scales captionSize,
        // and the caption string is constant during a pulse, so re-measure only
        // when the caption text or font changes — not every pulse frame.
        private Vector2 captionPrefPerPoint;
        private string lastCaptionText;

        private void Update() {
            if(root == null || valueText == null) {
                return;
            }

            bool isReorganizing = UICore.IsReorganizing;
            // Gated by the master Overlay enable as well as Combo's own toggle.
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
            // it into Conf every frame otherwise is a no-op round-trip.
            if(isReorganizing) {
                Vector2 stored = OverlayCalibration.Unscale(root.anchoredPosition);
                Conf.OffsetX = stored.x;
                Conf.OffsetY = GetOffsetYFromPosition(stored.y);
            }

            // Only reassign the font when it actually changes — setting .font
            // rebuilds the material instance and would wipe the shadow/thickness
            // we baked in, so re-apply those right after.
            TMP_FontAsset font = FontManager.Current;
            bool fontChanged = false;
            if(valueText.font != font) {
                valueText.font = font;
                ApplyValueMaterial();
                fontChanged = true;
            }
            bool captionFontChanged = false;
            if(captionText != null && captionText.font != font) {
                captionText.font = font;
                ApplyCaptionMaterial();
                captionFontChanged = true;
            }

            int count = isReorganizing && Combo.Count <= 0 ? 42 : Combo.Count;
            (float pulse, float pulseIntensity) = Combo.EvaluatePulse(Conf.CountPulseScale, Conf.PulseDuration);
            float valueSize = Conf.FontSize * pulse;
            Color color = Conf.GetComboColor(count);

            // Idle guard: skip the value mesh re-measure + material/shadow re-apply
            // when the number, pulse-scaled size and color are all unchanged (the
            // common case — pulse settles to exactly 1f between hits). The color
            // compare self-adapts: an animated combo color re-applies every frame,
            // a static one doesn't. Live shadow/thickness edits re-apply through
            // ApplyCountShadow/ApplyCaptionShadow, not this path.
            bool valueChanged = fontChanged
                || count != cachedCount
                || valueSize != lastValueSize
                || color != lastColor;

            if(valueChanged) {
                bool textChanged = count != cachedCount;
                if(textChanged) {
                    cachedCount = count;
                    valueText.text = count.ToString(CultureInfo.InvariantCulture);
                }
                valueText.fontSize = valueSize;
                valueText.color = color;

                // TMP preferred size scales linearly with point size, so only
                // re-measure when the digits change (a digit added/removed). During
                // the post-hit pulse the text is identical and only valueSize moves,
                // so scale the cached per-point size instead of paying for a full
                // GetPreferredValues layout pass on every pulse frame.
                if(textChanged || prefPerPoint == Vector2.zero) {
                    Vector2 pref = valueText.GetPreferredValues(valueText.text);
                    prefPerPoint = valueSize > 0f ? pref / valueSize : Vector2.zero;
                }
                Vector2 scaled = prefPerPoint * valueSize;
                valueText.rectTransform.sizeDelta = new Vector2(Mathf.Max(scaled.x, 200f), scaled.y);
                ApplyValueMaterial();

                lastValueSize = valueSize;
                lastColor = color;
            }

            bool captionShown = captionText != null && Conf.ShowCaption;
            float captionSize = valueSize * Conf.CaptionScale;
            float labelKick = pulseIntensity * Conf.LabelPulseOffsetY;

            if(captionText != null) {
                if(captionShown) {
                    bool captionChanged = captionFontChanged
                        || valueChanged
                        || !lastCaptionShown
                        || captionSize != lastCaptionSize
                        || labelKick != lastLabelKick
                        // Without this a live CaptionOffsetY edit didn't move the
                        // caption until the next tile hit flipped valueChanged.
                        || Conf.CaptionOffsetY != lastCaptionOffsetY;
                    if(captionChanged) {
                        captionText.fontSize = captionSize;
                        captionText.color = Color.white;
                        // TMP preferred size is linear in point size and the caption
                        // string is constant through a pulse (only captionSize moves),
                        // so only re-measure when the string or font actually changes —
                        // mirroring the value text's prefPerPoint cache above.
                        if(captionFontChanged
                            || captionText.text != lastCaptionText
                            || captionPrefPerPoint == Vector2.zero
                        ) {
                            Vector2 capMeasured = captionText.GetPreferredValues(captionText.text);
                            captionPrefPerPoint = captionSize > 0f ? capMeasured / captionSize : Vector2.zero;
                            lastCaptionText = captionText.text;
                        }
                        Vector2 capPref = captionPrefPerPoint * captionSize;
                        captionText.rectTransform.sizeDelta = new Vector2(Mathf.Max(capPref.x, 200f), capPref.y);
                        // Caption sits below the value; LabelPulseOffsetY kicks it up
                        // by the pulse intensity (0 by default = no kick).
                        captionText.rectTransform.anchoredPosition = new Vector2(
                            0f,
                            -(valueText.rectTransform.sizeDelta.y + CaptionGap + Conf.CaptionOffsetY) + labelKick
                        );
                        ApplyCaptionMaterial();
                        lastCaptionSize = captionSize;
                        lastLabelKick = labelKick;
                        lastCaptionOffsetY = Conf.CaptionOffsetY;
                    }
                } else if(lastCaptionShown || captionFontChanged) {
                    // Caption just turned off (or font swapped while off): one apply
                    // to settle its shadow, then leave it untouched each frame.
                    ApplyCaptionMaterial();
                }
                lastCaptionShown = captionShown;
            }

            float blockH = valueText.rectTransform.sizeDelta.y;
            if(captionShown) {
                blockH += CaptionGap + captionText.rectTransform.sizeDelta.y + Conf.CaptionOffsetY;
            }
            if(blockH != lastBlockH) {
                root.sizeDelta = new Vector2(768f, blockH);
                lastBlockH = blockH;
            }
        }
    }
}
