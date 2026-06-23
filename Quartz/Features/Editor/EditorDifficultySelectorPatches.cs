using HarmonyLib;
using UnityEngine;

namespace Quartz.Features.Editor;

public static partial class EditorFeature {
    private const float DifficultyClickPadding = 24f;
    private static readonly Vector3[] difficultyCorners = new Vector3[4];

    private static bool PointerIsOverVisibleDifficultyIcon(EditorDifficultySelector selector) {
        RectTransform iconRect = selector != null && selector.bullseyeImage != null
            ? selector.bullseyeImage.rectTransform
            : null;
        if(iconRect == null) {
            return true;
        }

        Camera camera = GetCanvasCamera(iconRect);
        iconRect.GetWorldCorners(difficultyCorners);

        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, difficultyCorners[0]);
        Vector2 max = min;
        for(int i = 1; i < difficultyCorners.Length; i++) {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, difficultyCorners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        Vector2 mouse = Input.mousePosition;
        min -= Vector2.one * DifficultyClickPadding;
        max += Vector2.one * DifficultyClickPadding;
        return mouse.x >= min.x && mouse.x <= max.x && mouse.y >= min.y && mouse.y <= max.y;
    }

    private static Camera GetCanvasCamera(RectTransform rect) {
        Canvas canvas = rect != null ? rect.GetComponentInParent<Canvas>() : null;
        if(canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) {
            return null;
        }
        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }

    private static void HideDifficultyPopup(EditorDifficultySelector selector) {
        try {
            selector?.buttonChangeDifficulty?.gameObject.SetActive(false);
            selector?.editorDifficultyText?.gameObject.SetActive(false);
        } catch {
        }
    }

    [HarmonyPatch(typeof(EditorDifficultySelector), "OnPointerEnter")]
    private static class DifficultyPointerEnterPatch {
        private static bool Prefix(EditorDifficultySelector __instance) {
            if(PointerIsOverVisibleDifficultyIcon(__instance)) {
                return true;
            }

            HideDifficultyPopup(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(EditorDifficultySelector), "ToggleDifficulty")]
    private static class DifficultyTogglePatch {
        private static bool Prefix(EditorDifficultySelector __instance) {
            bool allowed = PointerIsOverVisibleDifficultyIcon(__instance);
            if(!allowed) {
                HideDifficultyPopup(__instance);
            }
            return allowed;
        }
    }
}
