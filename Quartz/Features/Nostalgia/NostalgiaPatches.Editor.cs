using HarmonyLib;
using UnityEngine;

namespace Quartz.Features.Nostalgia;

// Editor-category patches ported from BackToThePast: space360Tile, weakAuto,
// whiteAuto, legacyEditorButtons (positions + design), legacyTexts.
public static partial class Nostalgia {

    // --- Space 360 Tile: Space adds a reversed-twice 360 spin tile ---
    [HarmonyPatch(typeof(scnEditor), "Play")]
    private static class Space360TilePatch {
        private static bool Prefix(scnEditor __instance) {
            if(ShouldSpace360Tile
               && !Input.GetKeyDown(KeyCode.P)
               && !Input.GetKey(KeyCode.LeftShift)
               && !Input.GetKey(KeyCode.RightShift)
               && Input.GetKeyDown(KeyCode.Space)) {
                if(__instance.SelectionIsSingle()) {
                    var lm = scrLevelMaker.instance;
                    object floatDir = lm.GetRotDirection(lm.GetRotDirection(__instance.selectedFloors[0].floatDirection, true), true);
                    object stringDir = lm.GetRotDirection(lm.GetRotDirection(__instance.selectedFloors[0].stringDirection, true), true);
                    Traverse.Create(__instance).Method(
                        "CreateFloorWithCharOrAngle",
                        floatDir, stringDir, true, true).GetValue();
                }
                return false;
            }
            return true;
        }
    }

    // --- Weak Auto: force the old auto path off while the debug flag ticks ---
    // RDC.useOldAuto is held true globally while the setting is on (see
    // Refresh/Restore); this patch flips it off only for scrShowIfDebug.Update.
    [HarmonyPatch(typeof(scrShowIfDebug), "Update")]
    private static class WeakAutoPatch {
        private static void Prefix() {
            if(ShouldWeakAuto) {
                RDC.useOldAuto = false;
            }
        }
        private static void Postfix() {
            if(ShouldWeakAuto) {
                RDC.useOldAuto = true;
            }
        }
    }

    // --- White Auto: report not-high-BPM so the auto icon stays white ---
    [HarmonyPatch(typeof(scnEditor), "get_highBPM")]
    private static class WhiteAutoHighBpmPatch {
        private static void Postfix(ref bool __result) {
            if(ShouldWhiteAuto) {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(scnGame), "ResetScene")]
    private static class WhiteAutoResetPatch {
        private static void Postfix() {
            if(ShouldWhiteAuto && scnEditor.instance != null) {
                scnEditor.instance.autoFailed = false;
            }
        }
    }

    // --- Legacy Editor Buttons: re-apply layout/design on editor build ---
    [HarmonyPatch(typeof(scnEditor), "Awake")]
    private static class LegacyEditorButtonsPatch {
        private static void Postfix() {
            ChangeEditorButtons(Enabled && Conf.LegacyEditorButtonsPositions);
            RemoveShadowAddOutline(Enabled && Conf.LegacyEditorButtonsDesigns);
        }
    }

    // --- Legacy Texts: revert a few Korean editor strings ---
    [HarmonyPatch(typeof(RDString), "GetWithCheck")]
    private static class LegacyTextsPatch {
        private static void Postfix(ref string __result) {
            if(!ShouldLegacyTexts) {
                return;
            }
            switch(__result) {
                case "눈폭풍":
                    __result = "눈폭충";
                    break;
                case "세피아":
                    __result = "소피아";
                    break;
                case "작곡가":
                    __result = "아티스트";
                    break;
            }
        }
    }
}
