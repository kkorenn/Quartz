using HarmonyLib;
using Quartz.Core;
using UnityEngine;

namespace Quartz.Features.Judgement;

// Tracks per-judgement hit counts for the current run, ported from the
// original KorenResourcePack. Nine display slots, symmetric around Perfect:
// Overload, Too Early, Very Early, Early Perfect, Perfect (+Auto), Late
// Perfect, Very Late, Too Late, Miss.
internal static class Judgement {
    internal const int Slots = 9;

    // Slot tint colors, same values as v1: purple at the extremes, red/orange
    // approaching center, greens around Perfect.
    internal static readonly Color[] SlotColors = [
        new(0.78f, 0.35f, 1f, 1f),
        new(1f, 0.22f, 0.22f, 1f),
        new(1f, 0.44f, 0.31f, 1f),
        new(0.63f, 1f, 0.31f, 1f),
        new(0.38f, 1f, 0.31f, 1f),
        new(0.63f, 1f, 0.31f, 1f),
        new(1f, 0.44f, 0.31f, 1f),
        new(1f, 0.22f, 0.22f, 1f),
        new(0.78f, 0.35f, 1f, 1f),
    ];

    private static readonly int[] counts = new int[16];

    internal static void Reset() => System.Array.Clear(counts, 0, counts.Length);

    internal static int SlotCount(int slot) => slot switch {
        0 => Count(HitMargin.FailOverload),
        1 => Count(HitMargin.TooEarly),
        2 => Count(HitMargin.VeryEarly),
        3 => Count(HitMargin.EarlyPerfect),
        4 => Count(HitMargin.Perfect) + Count(HitMargin.Auto),
        5 => Count(HitMargin.LatePerfect),
        6 => Count(HitMargin.VeryLate),
        7 => Count(HitMargin.TooLate),
        8 => Count(HitMargin.FailMiss),
        _ => 0,
    };

    private static int Count(HitMargin hit) {
        int idx = (int)hit;
        return idx >= 0 && idx < counts.Length ? counts[idx] : 0;
    }

    [HarmonyPatch(typeof(scrMarginTracker), "AddHit", typeof(HitMargin))]
    private static class AddHitPatch {
        private static void Postfix(HitMargin hit) {
            if(!MainCore.IsModEnabled) {
                return;
            }

            int idx = (int)hit;
            if(idx >= 0 && idx < counts.Length) {
                counts[idx]++;
            }
        }
    }

    [HarmonyPatch(typeof(scnGame), "Play")]
    private static class ResetOnRunStartPatch {
        private static void Postfix() {
            if(MainCore.IsModEnabled) {
                Reset();
            }
        }
    }

    // Built-in/official levels never instantiate scnGame, so scnGame.Play (the
    // custom-level run-start) never fires for them — their run begins in
    // scrController.Start via WaitForStartCo. Without this the counts carried
    // over across restarts of a main level. Start runs on every scene (re)load
    // and Restart reloads the scene, so this covers first play and every retry.
    [HarmonyPatch(typeof(scrController), "Start")]
    private static class ResetOnControllerStartPatch {
        private static void Postfix(scrController __instance) {
            if(MainCore.IsModEnabled && __instance.gameworld) {
                Reset();
            }
        }
    }

    [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
    private static class ResetOnRunExitPatch {
        private static void Postfix() {
            if(MainCore.IsModEnabled) {
                Reset();
            }
        }
    }
}
