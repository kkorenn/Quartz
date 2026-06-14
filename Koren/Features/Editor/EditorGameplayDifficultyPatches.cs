using HarmonyLib;
using UiHiderFeature = Koren.Features.UiHider.UiHider;

namespace Koren.Features.Editor;

public static partial class EditorFeature {
    private static bool IsEditorLoaded() => scnEditor.instance != null;

    [HarmonyPatch(typeof(scrUIController), "ShowDifficultyContainer")]
    private static class GameplayDifficultyShowPatch {
        private static bool Prefix(scrUIController __instance) {
            if(!IsEditorLoaded()) {
                return true;
            }

            UiHiderFeature.HideGameplayDifficultyContainer(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(scrUIController), "DifficultyArrowPressed")]
    private static class GameplayDifficultyArrowPatch {
        private static bool Prefix(scrUIController __instance) {
            if(!IsEditorLoaded()) {
                return true;
            }

            UiHiderFeature.HideGameplayDifficultyContainer(__instance);
            return false;
        }
    }
}
