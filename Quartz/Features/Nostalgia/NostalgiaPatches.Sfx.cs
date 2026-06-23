using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Quartz.Features.Nostalgia;

// SFX-category patches ported from BackToThePast's NoSfxSound: mute the
// pure-perfect chime, wind/screen-wipe, countdown, ending cymbal and new-best
// jingle. (The death-sound mute uses GCS.playDeathSound directly — see
// Nostalgia.ApplyDeathSound.)
public static partial class Nostalgia {

    // --- PlaySfx: drop or downgrade specific one-shot sounds ---
    [HarmonyPatch]
    private static class PlaySfxPatch {
        private static MethodBase TargetMethod() =>
            typeof(scrSfx).GetMethods().FirstOrDefault(m =>
                m.Name == "PlaySfx"
                && m.GetParameters().Length != 0
                && m.GetParameters()[0].ParameterType == typeof(SfxSound));

        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix(ref SfxSound __0) {
            switch(__0) {
                case SfxSound.PurePerfect:
                    return !ShouldDisablePurePerfectSound;
                case SfxSound.ScreenWipeIn:
                case SfxSound.ScreenWipeOut:
                    return !ShouldDisableWindSound;
                case SfxSound.PlanetExplosionHighscore:
                    if(ShouldDisableNewBestSound) {
                        __0 = SfxSound.PlanetExplosion;
                    }
                    break;
            }
            return true;
        }
    }

    // --- PlayHitTimes: suppress the countdown takeoff and ending cymbal ---
    [HarmonyPatch(typeof(scrConductor), "PlayHitTimes")]
    private static class PlayHitTimesPatch {
        private static bool prevFastTakeoff;
        private static bool prevEndingCymbal;

        private static void Prefix(scrConductor __instance) {
            prevFastTakeoff = __instance.fastTakeoff;
            if(ShouldDisableCountdownSound) {
                __instance.fastTakeoff = true;
            }
            prevEndingCymbal = __instance.playEndingCymbal;
            if(ShouldDisableEndingSound) {
                __instance.playEndingCymbal = false;
            }
        }

        private static void Postfix(scrConductor __instance) {
            __instance.fastTakeoff = prevFastTakeoff;
            __instance.playEndingCymbal = prevEndingCymbal;
        }
    }
}
