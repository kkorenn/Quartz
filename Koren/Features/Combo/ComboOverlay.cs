using System.Globalization;
using Koren.Core;
using Koren.Features.ProgressBar;
using Koren.Features.Status;
using Koren.IO;
using Koren.Resource;
using Koren.UI;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

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
        drag.AddComponent<DragHandler>();
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

        captionText.text = Conf.CaptionText ?? "Combo";
        captionText.gameObject.SetActive(Conf.ShowCaption);
    }

    private static Vector2 GetDefaultPosition() {
        ProgressBarOverlay.EnsureConf();
        ProgressBarSettings bar = ProgressBarOverlay.Conf;
        float y = -(bar.TopOffset + bar.Height + VerticalGap + Conf.OffsetY);
        return new Vector2(Conf.OffsetX, y);
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
            Conf.OffsetX = root.anchoredPosition.x;
            Conf.OffsetY = GetOffsetYFromPosition(root.anchoredPosition.y);
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
        return anchoredY + bar.TopOffset + bar.Height + VerticalGap;
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

            Conf.OffsetX = root.anchoredPosition.x;
            Conf.OffsetY = GetOffsetYFromPosition(root.anchoredPosition.y);

            // Only reassign the font when it actually changes — setting .font
            // rebuilds the material instance and would wipe the shadow/thickness
            // we baked in, so re-apply those right after.
            TMP_FontAsset font = FontManager.Current;
            if(valueText.font != font) {
                valueText.font = font;
                ApplyValueMaterial();
            }
            if(captionText != null && captionText.font != font) {
                captionText.font = font;
                ApplyCaptionMaterial();
            }

            int count = isReorganizing && Combo.Count <= 0 ? 42 : Combo.Count;
            if(count != cachedCount) {
                cachedCount = count;
                valueText.text = count.ToString(CultureInfo.InvariantCulture);
            }

            (float pulse, float pulseIntensity) = Combo.EvaluatePulse(Conf.CountPulseScale, Conf.PulseDuration);
            float valueSize = Conf.FontSize * pulse;
            valueText.fontSize = valueSize;
            valueText.color = Conf.GetComboColor(count);

            Vector2 pref = valueText.GetPreferredValues(valueText.text);
            valueText.rectTransform.sizeDelta = new Vector2(Mathf.Max(pref.x, 200f), pref.y);
            ApplyValueMaterial();

            float captionSize = valueSize * Conf.CaptionScale;
            if(captionText != null && Conf.ShowCaption) {
                captionText.fontSize = captionSize;
                captionText.color = Color.white;
                Vector2 capPref = captionText.GetPreferredValues(captionText.text);
                captionText.rectTransform.sizeDelta = new Vector2(Mathf.Max(capPref.x, 200f), capPref.y);
                // Caption sits below the value; LabelPulseOffsetY kicks it up by
                // the pulse intensity (0 by default = no kick).
                float labelKick = pulseIntensity * Conf.LabelPulseOffsetY;
                captionText.rectTransform.anchoredPosition = new Vector2(
                    0f,
                    -(valueText.rectTransform.sizeDelta.y + CaptionGap + Conf.CaptionOffsetY) + labelKick
                );
                ApplyCaptionMaterial();
            } else if(captionText != null) {
                ApplyCaptionMaterial();
            }

            float blockH = valueText.rectTransform.sizeDelta.y;
            if(captionText != null && Conf.ShowCaption) {
                blockH += CaptionGap + captionText.rectTransform.sizeDelta.y + Conf.CaptionOffsetY;
            }

            root.sizeDelta = new Vector2(768f, blockH);
        }
    }
}
