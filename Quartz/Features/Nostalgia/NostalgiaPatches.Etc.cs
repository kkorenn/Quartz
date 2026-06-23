using HarmonyLib;

namespace Quartz.Features.Nostalgia;

// Etc-category patches ported from BackToThePast: hide the alpha-build warning
// splash, hide the lobby announcement sign, and swap to the old lobby
// background. Also the level-select scene hook that re-applies the
// scene-dependent tweaks (old background, announce sign, death sound).
public static partial class Nostalgia {

    // --- Hide Alpha Warning: skip the splash straight to the menu ---
    [HarmonyPatch(typeof(scnSplash), "Start")]
    private static class HideAlphaWarningPatch {
        private static bool Prefix(scnSplash __instance) {
            if(ShouldDisableAlphaWarning) {
                Traverse.Create(__instance).Method("GoToMenu").GetValue();
                return false;
            }
            return true;
        }
    }

    // --- Hide Announce Sign: hide each NewsSign as it spawns ---
    [HarmonyPatch(typeof(NewsSign), "Awake")]
    private static class HideAnnounceSignPatch {
        private static void Postfix() {
            if(ShouldDisableAnnounceSign) {
                ToggleSign(false);
            }
        }
    }

    // --- Level-select scene hook: re-apply background / sign / death sound ---
    [HarmonyPatch(typeof(scnLevelSelect), "Start")]
    private static class LevelSelectStartPatch {
        private static void Postfix() {
            if(!Enabled) {
                return;
            }
            SetBackground();
            if(ShouldDisableAnnounceSign) {
                ToggleSign(false);
            }
            ApplyDeathSound();
            try { RDC.useOldAuto = ShouldWeakAuto; } catch { }
        }
    }
}
