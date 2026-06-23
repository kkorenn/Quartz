using HarmonyLib;
using UnityEngine;

namespace Quartz.Features.UiHider;

// Harmony patches for the UI-hiding flags that must intercept the game
// mid-call (the rest is reasserted per frame from the ticker). Ported from
// v1's UiHiderPatches; string-named targets are types that have moved or
// been renamed across game versions.
public static partial class UiHider {
    [HarmonyPatch(typeof(scrHitTextMesh), "Show")]
    private static class JudgmentTextShowPatch {
        private static void Prefix(ref Vector3 position) {
            if(ShouldHideJudgementText()) {
                position = HiddenPosition;
            }
        }
    }

    [HarmonyPatch("scrMissIndicator", "Awake")]
    private static class MissIndicatorPatch {
        private static void Postfix(object __instance) {
            if(!ShouldHideMissIndicators()) {
                return;
            }

            if(__instance is Component component) {
                component.transform.position = HiddenPosition;
            }
        }
    }

    // The flashing "AUTOPLAY" text checks RDC.auto each frame; faking it off
    // around that one Update hides the text without affecting actual
    // auto-play state.
    [HarmonyPatch(typeof(scrShowIfDebug), "Update")]
    private static class HideAutoplayTextPatch {
        private static bool prevAuto;

        private static void Prefix() {
            prevAuto = RDC.auto;
            if(ShouldHideOtto()) {
                RDC.auto = false;
            }
        }

        private static void Postfix() {
            RDC.auto = prevAuto;
        }
    }

    [HarmonyPatch(typeof(scnEditor), "SwitchToEditMode")]
    private static class ReapplyOnEditModePatch {
        private static void Postfix() => ApplyNow();
    }

    private static class HideResultTextAndFlashPatches {
        private static bool shouldIgnoreFlashOnce;

        [HarmonyPatch(typeof(scrController), "OnLandOnPortal")]
        private static class HideResultTextPatch {
            private static void Prefix() {
                if(ShouldHideLastFloorFlash()) {
                    shouldIgnoreFlashOnce = true;
                }
            }

            private static void Postfix(scrController __instance) {
                if(!ShouldHideResult()) {
                    return;
                }

                SetMemberInactive(__instance, "txtCongrats");
                SetMemberInactive(__instance, "txtResults");
                SetMemberInactive(__instance, "txtAllStrictClear");
            }
        }

        // The portal-landing white flash is identified by its exact color so
        // other flashes (death, checkpoints) stay untouched.
        [HarmonyPatch("scrFlash", "Flash")]
        private static class HideLastFloorFlashPatch {
            private static bool Prefix(object[] __args) {
                if(!shouldIgnoreFlashOnce || !TryGetFlashColor(__args, out Color colorStart) || !IsLastFloorFlashColor(colorStart)) {
                    return true;
                }

                shouldIgnoreFlashOnce = false;
                return false;
            }
        }
    }

    private static class HideHitErrorMeterPatches {
        private static void HideErrorMeter() {
            if(!ShouldHideHitErrorMeter()) {
                return;
            }

            scrController controller = scrController.instance;
            if(controller == null || !controller.gameworld) {
                return;
            }

            GameObject errorMeter = GetGameObject(GetMemberValue(controller, "errorMeter"));
            if(errorMeter != null && errorMeter.activeSelf) {
                errorMeter.SetActive(false);
            }
        }

        [HarmonyPatch(typeof(scrController), "paused", MethodType.Setter)]
        private static class PausedSetterPatch {
            private static void Postfix() => HideErrorMeter();
        }

        [HarmonyPatch(typeof(scrPlanet), "MoveToNextFloor")]
        private static class MoveToNextFloorPatch {
            private static void Postfix() => HideErrorMeter();
        }

        [HarmonyPatch("TaroCutsceneScript", "DisplayText")]
        private static class TaroCutscenePatch {
            private static void Postfix() => HideErrorMeter();
        }
    }

    private static void SetMemberInactive(object owner, string memberName) {
        GameObject gameObject = GetGameObject(GetMemberValue(owner, memberName));
        if(gameObject != null) {
            gameObject.SetActive(false);
        }
    }

    private static bool IsLastFloorFlashColor(Color color) {
        return Mathf.Abs(color.r - 1f) < 0.001f
            && Mathf.Abs(color.g - 1f) < 0.001f
            && Mathf.Abs(color.b - 1f) < 0.001f
            && Mathf.Abs(color.a - 0.4f) < 0.001f;
    }

    private static bool TryGetFlashColor(object[] args, out Color color) {
        color = default;
        if(args == null || args.Length == 0 || args[0] is not Color c) {
            return false;
        }

        color = c;
        return true;
    }
}
