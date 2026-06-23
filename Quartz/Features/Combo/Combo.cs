using HarmonyLib;
using Quartz.Core;
using Quartz.Features.Interop;
using UnityEngine;

namespace Quartz.Features.Combo;

// Counts consecutive Perfect (and optionally Auto) hits. Resets on any non-
// Perfect/non-Auto hit. Drives the center-screen ComboOverlay pulse animation
// on increment — ported from the original KorenResourcePack.
internal static class Combo {
    internal static int Count;
    internal static float PulseStartTime = -1f;

    // Fraction of the pulse spent growing to the peak; the remainder settles
    // back to rest. Mirrors the original 0.075 out / 0.18 settle split.
    private const float OutFraction = 0.3f;

    // Evaluates the pop animation. peakDelta is the extra scale at the peak
    // (so the count grows to 1 + peakDelta); duration is the full length in
    // seconds. Returns the count scale and a 0..1 pulse intensity (used to
    // drive the caption kick). NoPopAnim or a non-positive duration disables it.
    internal static (float scale, float intensity) EvaluatePulse(float peakDelta, float duration) {
        ComboSettings conf = ComboOverlay.Conf;
        if(conf != null && conf.NoPopAnim) {
            PulseStartTime = -1f;
            return (1f, 0f);
        }

        if(PulseStartTime < 0f || duration <= 0f) {
            return (1f, 0f);
        }

        float peak = 1f + Mathf.Max(0f, peakDelta);
        float outDur = duration * OutFraction;
        float settleDur = duration - outDur;

        float elapsed = Time.realtimeSinceStartup - PulseStartTime;
        if(elapsed <= outDur) {
            float t = outDur <= 0f ? 1f : elapsed / outDur;
            float eased = t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
            return (Mathf.LerpUnclamped(1f, peak, eased), eased);
        }

        float settleElapsed = elapsed - outDur;
        if(settleDur <= 0f || settleElapsed >= settleDur) {
            PulseStartTime = -1f;
            return (1f, 0f);
        }

        float k = settleElapsed / settleDur;
        return (Mathf.Lerp(peak, 1f, k), 1f - k);
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

    // Built-in/official levels never instantiate scnGame, so scnGame.Play (the
    // custom-level run-start) never fires for them — their run begins in
    // scrController.Start via WaitForStartCo. Without this the count carried
    // over across restarts of a main level. Start runs on every scene (re)load
    // and Restart reloads the scene, so this covers first play and every retry.
    [HarmonyPatch(typeof(scrController), "Start")]
    private static class ResetOnControllerStartPatch {
        private static void Postfix(scrController __instance) {
            if(!MainCore.IsModEnabled) {
                return;
            }
            if(__instance.gameworld) {
                Count = 0;
                PulseStartTime = -1f;
            }
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

            // XPerfect combo: only a dead-center X perfect keeps the combo; a
            // +/- perfect breaks it. Otherwise every Perfect counts as before.
            bool xpComboMode = conf.XPerfectComboEnabled && XPerfectBridge.Active;
            bool incPerfect = xpComboMode && hit == HitMargin.Perfect
                ? XPerfectBridge.LastJudge() == XPerfectBridge.Judge.X
                : hit == HitMargin.Perfect;
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
