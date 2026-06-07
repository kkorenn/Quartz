using HarmonyLib;
using Koren.Core;

namespace Koren.Features.Status;

// Counts consecutive Perfect (and optionally Auto) hits. Resets on any non-
// Perfect/non-Auto hit. The original pack also drives a center-screen pulse
// animation on increment; v2 just exposes the counter as a Status HUD line.
//
// XPerfect / coop / no-fail edge cases from the original are intentionally
// dropped here — they depend on XPerfectBridge and other helpers we're not
// porting yet. Easy to layer back in if those land.
internal static class Combo {
    internal static int Count;

    // Wipe the counter whenever the player enters a level. Without this, the
    // count carries over across level changes and re-plays — the user
    // expectation is that combo is per-run, not lifetime.
    [HarmonyPatch(typeof(scnGame), "Play")]
    private static class ResetOnRunStartPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }
            Count = 0;
        }
    }

    // Also reset when leaving the level — covers exiting to the song select
    // screen before re-entering.
    [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
    private static class ResetOnRunExitPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }
            Count = 0;
        }
    }

    [HarmonyPatch(typeof(scrMarginTracker), "AddHit", typeof(HitMargin))]
    private static class AddHitPatch {
        private static void Postfix(HitMargin hit) {
            if(!MainCore.IsModEnabled) {
                return;
            }

            StatusSettings conf = StatusOverlay.Conf;
            if(conf == null) {
                return;
            }

            bool incPerfect = hit == HitMargin.Perfect;
            bool incAuto = conf.ComboCountAuto && hit == HitMargin.Auto;

            if(incPerfect || incAuto) {
                Count++;
            } else if(conf.ComboCountAuto || hit != HitMargin.Auto) {
                Count = 0;
            }
        }
    }
}
