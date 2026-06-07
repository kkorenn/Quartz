using Koren.Core;
using Koren.Features.Status;
using Koren.IO;
using Koren.Resource;
using Koren.UI;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Koren.Features.ProgressBar;

// Top-of-screen progress bar HUD. Mirrors the original KorenResourcePack's
// ProgressBar visual but ported to UGUI Image components instead of the
// original's IMGUI GUI.DrawTexture path.
//
// Layout (children of `bar`):
//   border  — full-stretch image, expanded by OutlineThickness on each edge,
//             drawn first so it sits behind the fill (acts as a real outer
//             border). Hidden when OutlineThickness == 0.
//   back    — full-stretch background fill
//   fill    — sized per-frame from ProgressTracker.RunStartProgress to
//             scrController.percentComplete, so partial-checkpoint runs
//             fill from the checkpoint anchor rather than 0%.
//   drag    — full-stretch transparent EmptyGraphic + DragHandler, only
//             active in Reorganize mode (matches StatusOverlay's pattern).
//
// Rounding is achieved by giving the back/fill/border images the same sliced
// circle sprite the rest of the UI uses and scaling pixelsPerUnitMultiplier
// inversely with the user-chosen radius. At Rounding == 0 the sprite is
// dropped entirely so corners are perfectly sharp.
public static class ProgressBarOverlay {
    public static SettingsFile<ProgressBarSettings> ConfMgr { get; private set; }
    public static ProgressBarSettings Conf => ConfMgr.Data;

    private static GameObject canvasObj;
    private static RectTransform bar;
    private static RectTransform border;
    private static Image borderImg;
    private static Image back;
    private static RectTransform fillContainer;
    private static Image fill;
    private static GameObject dragObj;
    private static Updater updater;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<ProgressBarSettings>(
            Path.Combine(MainCore.Paths.RootPath, "ProgressBar.json")
        );
        ConfMgr.Load();
    }

    public static void Initialize(GameObject root) {
        if(canvasObj != null) {
            return;
        }

        EnsureConf();

        canvasObj = new GameObject("KorenProgressBarCanvas");
        canvasObj.transform.SetParent(root.transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Below the settings panel (32767) and Status HUD (32760).
        canvas.sortingOrder = 32755;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject barObj = new("Bar");
        barObj.transform.SetParent(canvasObj.transform, false);
        bar = barObj.AddComponent<RectTransform>();
        bar.anchorMin = new Vector2(0.5f, 1f);
        bar.anchorMax = new Vector2(0.5f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);

        GameObject borderObj = new("Border");
        borderObj.transform.SetParent(bar, false);
        border = borderObj.AddComponent<RectTransform>();
        border.anchorMin = Vector2.zero;
        border.anchorMax = Vector2.one;
        borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;

        back = barObj.AddComponent<Image>();
        back.raycastTarget = false;

        GameObject containerObj = new("FillContainer");
        containerObj.transform.SetParent(bar, false);
        fillContainer = containerObj.AddComponent<RectTransform>();
        fillContainer.anchorMin = new Vector2(0f, 0f);
        fillContainer.anchorMax = new Vector2(0f, 1f);
        fillContainer.pivot = new Vector2(0f, 0.5f);

        GameObject fillObj = new("Fill");
        fillObj.transform.SetParent(fillContainer, false);
        RectTransform fillRect = fillObj.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        fill = fillObj.AddComponent<Image>();
        fill.raycastTarget = false;

        // Drag overlay — full-stretch transparent surface that captures pointer
        // input while Reorganize mode is active.
        GameObject drag = new("Drag");
        dragObj = drag;
        drag.transform.SetParent(bar, false);
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

    public static void Apply() {
        if(bar == null) {
            return;
        }

        bar.sizeDelta = new Vector2(Conf.Width, Conf.Height);
        bar.anchoredPosition = new Vector2(Conf.OffsetX, -Conf.TopOffset);

        ApplyRounding(back, Conf.Rounding);
        ApplyRounding(fill, Conf.Rounding);

        if(back != null) {
            back.color = Conf.GetBackColor();
        }

        if(fill != null) {
            fill.color = Conf.GetFillColor();
        }

        ApplyOutline();
    }

    public static void Save() => ConfMgr?.Save();

    public static void ResetPosition() {
        ProgressBarSettings def = new();
        Conf.OffsetX = def.OffsetX;
        Conf.TopOffset = def.TopOffset;
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
        bar = null;
        border = null;
        borderImg = null;
        back = null;
        fillContainer = null;
        fill = null;
        dragObj = null;
        updater = null;
    }

    private static void ApplyRounding(Image img, float rounding) {
        if(img == null) {
            return;
        }

        if(rounding <= 0.5f) {
            img.sprite = null;
            img.type = Image.Type.Simple;
        } else {
            img.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P1024);
            img.type = Image.Type.Sliced;
            // Circle256P1024 draws ~8px corners at multiplier=1 on a
            // 1920x1080 reference; invert so the slider reads as a px radius.
            img.pixelsPerUnitMultiplier = Mathf.Max(0.05f, 8f / rounding);
        }
    }

    private static void ApplyOutline() {
        if(border == null || borderImg == null) {
            return;
        }

        float t = Mathf.Max(0f, Conf.OutlineThickness);
        bool on = t > 0.01f;
        if(border.gameObject.activeSelf != on) {
            border.gameObject.SetActive(on);
        }

        if(!on) {
            return;
        }

        border.offsetMin = new Vector2(-t, -t);
        border.offsetMax = new Vector2(t, t);
        borderImg.color = Conf.GetOutlineColor();
        ApplyRounding(borderImg, Conf.Rounding + t);
    }

    private sealed class Updater : MonoBehaviour {
        private void Update() {
            if(bar == null) {
                return;
            }

            bool isReorganizing = UICore.IsOpen && UICore.CurrentMenuState == (int)OriginalMenuState.Reorganize;
            bool show = (Conf.Enabled && GameStats.InGame) || isReorganizing;
            if(bar.gameObject.activeSelf != show) {
                bar.gameObject.SetActive(show);
            }

            if(dragObj != null && dragObj.activeSelf != isReorganizing) {
                dragObj.SetActive(isReorganizing);
            }

            if(!show) {
                return;
            }

            // Persist live drag position so Apply()-driven repositions don't
            // fight the user. Mirrors StatusOverlay's pattern.
            Conf.OffsetX = bar.anchoredPosition.x;
            Conf.TopOffset = -bar.anchoredPosition.y;

            float start = Mathf.Clamp01(GameStats.RunStartProgress);
            float now = Mathf.Clamp01(GameStats.Progress);
            if(now < start) {
                now = start;
            }

            float totalW = Conf.Width;
            float startX = totalW * start;
            float fillW = Mathf.Clamp(totalW * (now - start), 0f, totalW);

            fillContainer.anchoredPosition = new Vector2(startX, 0f);
            fillContainer.sizeDelta = new Vector2(fillW, 0f);
        }
    }
}
