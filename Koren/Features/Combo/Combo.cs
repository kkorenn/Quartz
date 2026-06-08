using HarmonyLib;
using Koren.Core;
using UnityEngine;

namespace Koren.Features.Combo;

// Counts consecutive Perfect (and optionally Auto) hits. Resets on any non-
// Perfect/non-Auto hit. Drives the center-screen ComboOverlay pulse animation
// on increment — ported from the original KorenResourcePack.
internal static class Combo {
    internal static int Count;

    internal static float PulseStartTime = -1f;
    private const float PulsePeakScale = 1.24f;
    private const float PulseOutDuration = 0.075f;
    private const float PulseSettleDuration = 0.18f;

    internal static float EvaluatePulseScale() {
        ComboSettings conf = ComboOverlay.Conf;
        if(conf != null && conf.NoPopAnim) {
            PulseStartTime = -1f;
            return 1f;
        }

        if(PulseStartTime < 0f) {
            return 1f;
        }

        float snap = conf != null && conf.FastAnim ? 0.35f : 1f;
        float outDur = PulseOutDuration * snap;
        float settleDur = PulseSettleDuration * snap;

        float elapsed = Time.realtimeSinceStartup - PulseStartTime;
        if(elapsed <= outDur) {
            float t = elapsed / outDur;
            float eased = t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
            return Mathf.LerpUnclamped(1f, PulsePeakScale, eased);
        }

        float settleElapsed = elapsed - outDur;
        if(settleElapsed >= settleDur) {
            PulseStartTime = -1f;
            return 1f;
        }

        return Mathf.Lerp(PulsePeakScale, 1f, settleElapsed / settleDur);
    }

    [HarmonyPatch(typeof(scnGame), "Play")]
    private static class ResetOnRunStartPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }
            Count = 0;
            PulseStartTime = -1f;
        }
    }

    [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
    private static class ResetOnRunExitPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }
            Count = 0;
            PulseStartTime = -1f;
        }
    }

    [HarmonyPatch(typeof(scrMarginTracker), "AddHit", typeof(HitMargin))]
    private static class AddHitPatch {
        private static void Postfix(HitMargin hit) {
            if(!MainCore.IsModEnabled) {
                return;
            }

            ComboSettings conf = ComboOverlay.Conf;
            if(conf == null) {
                return;
            }

            bool incPerfect = hit == HitMargin.Perfect;
            bool incAuto = conf.CountAuto && hit == HitMargin.Auto;

            if(incPerfect || incAuto) {
                Count++;
                PulseStartTime = conf.NoPopAnim ? -1f : Time.realtimeSinceStartup;
            } else if(conf.CountAuto || hit != HitMargin.Auto) {
                Count = 0;
                PulseStartTime = -1f;
            }
        }
    }
}
