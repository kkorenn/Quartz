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

    // Maps user-facing pixel-style shadow offsets to TMP underlay units (em-
    // relative). ~0.01 underlay ≈ 1px on a ~100px font; close enough that the
    // user's "X: 4px" reads visually like 4px without exposing TMP's units.
    private const float ShadowToUnderlay = 0.01f;

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
        ApplyShadow(valueText, Conf.CountShadowX, Conf.CountShadowY, Conf.GetCountShadowColor());
        ApplyShadow(captionText, Conf.LabelShadowX, Conf.LabelShadowY, Conf.GetLabelShadowColor());
        ApplyThickness(valueText, Conf.CountThickness);
    }

    private static void ApplyShadow(TextMeshProUGUI text, float x, float y, Color color) {
        if(text == null) {
            return;
        }
        Material mat = text.fontMaterial;
        if(mat == null) {
            return;
        }

        bool on = color.a > 0.001f && (Mathf.Abs(x) > 0.001f || Mathf.Abs(y) > 0.001f);
        if(on) {
            mat.EnableKeyword("UNDERLAY_ON");
        } else {
            mat.DisableKeyword("UNDERLAY_ON");
        }

        mat.SetColor("_UnderlayColor", color);
        mat.SetFloat("_UnderlayOffsetX", x * ShadowToUnderlay);
        mat.SetFloat("_UnderlayOffsetY", y * ShadowToUnderlay);
        mat.SetFloat("_UnderlaySoftness", 0f);
        mat.SetFloat("_UnderlayDilate", 0f);
    }

    private static void ApplyThickness(TextMeshProUGUI text, float dilate) {
        if(text == null) {
            return;
        }
        Material mat = text.fontMaterial;
        if(mat == null) {
            return;
        }
        mat.SetFloat("_FaceDilate", Mathf.Clamp(dilate, -1f, 1f));
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

    private sealed class Updater : MonoBehaviour {
        private int cachedCount = -1;

        private void Update() {
            if(root == null || valueText == null) {
                return;
            }

            bool isReorganizing = UICore.IsReorganizing;
            // Gated by the master Overlay enable as well as Combo's own toggle.
            bool show = (StatusOverlay.IsEnabled && Conf.Enabled && GameStats.InGame) || isReorganizing;
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

            valueText.font = FontManager.Current;
            if(captionText != null) {
                captionText.font = FontManager.Current;
            }

            int count = isReorganizing && Combo.Count <= 0 ? 42 : Combo.Count;
            if(count != cachedCount) {
                cachedCount = count;
                valueText.text = count.ToString(CultureInfo.InvariantCulture);
            }

            // MasterSize scales the whole root so drag-handle and visuals stay
            // in lockstep; it's applied here every frame so live edits from
            // the settings page take effect without a re-Apply().
            root.localScale = Vector3.one * Mathf.Max(0.01f, Conf.MasterSize);

            float pulseT = Combo.EvaluatePulseT();
            float pulse = Combo.EvaluatePulseScale();
            float valueSize = Conf.FontSize * pulse;
            valueText.fontSize = valueSize;
            valueText.color = Conf.GetComboColor(count);

            Vector2 pref = valueText.GetPreferredValues(valueText.text);
            valueText.rectTransform.sizeDelta = new Vector2(Mathf.Max(pref.x, 200f), pref.y);

            float captionSize = valueSize * Mathf.Max(0.01f, Conf.LabelSize);
            if(captionText != null && Conf.ShowCaption) {
                captionText.fontSize = captionSize;
                captionText.color = Color.white;
                Vector2 capPref = captionText.GetPreferredValues(captionText.text);
                captionText.rectTransform.sizeDelta = new Vector2(Mathf.Max(capPref.x, 200f), capPref.y);
                // Caption rests below the value; LabelPulseOffsetY kicks it
                // farther away (down) on hit and settles back as pulseT → 0.
                captionText.rectTransform.anchoredPosition = new Vector2(
                    0f,
                    -(valueText.rectTransform.sizeDelta.y + CaptionGap + Conf.CaptionOffsetY)
                    - Conf.LabelPulseOffsetY * pulseT
                );
            }

            float blockH = valueText.rectTransform.sizeDelta.y;
            if(captionText != null && Conf.ShowCaption) {
                blockH += CaptionGap + captionText.rectTransform.sizeDelta.y + Conf.CaptionOffsetY;
            }

            root.sizeDelta = new Vector2(768f, blockH);
        }
    }
}
