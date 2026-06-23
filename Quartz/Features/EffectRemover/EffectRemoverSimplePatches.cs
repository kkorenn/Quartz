using System;
using System.Collections;
using HarmonyLib;
using Quartz.Core;
using UnityEngine;

namespace Quartz.Features.EffectRemover;

// Simple-mode patches — ported from PizzaLovers007's AdofaiTweaks
// "DisableEffects" tweak. Instead of stripping events from the chart (the
// Enhanced mode), these disable the live effect components at runtime, so the
// level data is never modified. All gate on EffectRemover.SimpleActive (mod on
// + On + Mode == simple) plus the individual toggle, and each Prepare()s on its
// target existing so a missing ffx type on a future game build just skips.
public static partial class EffectRemover {
    private static bool SimpleFilterActive => SimpleActive && Conf.SimpleFilter;
    private static bool SimpleBloomActive => SimpleActive && Conf.SimpleBloom;
    private static bool SimpleFlashActive => SimpleActive && Conf.SimpleFlash;
    private static bool SimpleHomActive => SimpleActive && Conf.SimpleHallOfMirrors;
    private static bool SimpleShakeActive => SimpleActive && Conf.SimpleScreenShake;
    private static bool SimpleMoveActive =>
        SimpleActive && Conf.SimpleMoveTrackMax <= EffectRemoverSettings.MoveTrackUpperBound;

    // --- Filters: report every filter as disabled ---
    [HarmonyPatch(typeof(ffxSetFilterPlus), "SetFilter")]
    private static class SimpleFilterPatch {
        private static bool Prepare() => AccessTools.Method(typeof(ffxSetFilterPlus), "SetFilter") != null;
        private static void Prefix(ref bool __0) {
            if(SimpleFilterActive) {
                __0 = false;
            }
        }
    }

    // --- Filters: also switch off the live filter components on level start ---
    [HarmonyPatch(typeof(scrController), "WaitForStartCo")]
    private static class SimpleFilterStartPatch {
        private static bool Prepare() => AccessTools.Method(typeof(scrController), "WaitForStartCo") != null;
        private static void Postfix() {
            if(!SimpleFilterActive) {
                return;
            }
            try {
                object vfx = scrVfxPlus.instance;
                if(vfx == null) {
                    return;
                }
                if(Traverse.Create(vfx).Field("filterToComp").GetValue() is IDictionary map) {
                    foreach(object v in map.Values) {
                        if(v is Behaviour b) {
                            b.enabled = false;
                        }
                    }
                }
            } catch(Exception e) {
                MainCore.Log.Wrn($"[EffectRemover] simple filter start failed: {e.Message}");
            }
        }
    }

    // --- Bloom: skip the effect ---
    [HarmonyPatch(typeof(ffxBloomPlus), "StartEffect")]
    private static class SimpleBloomPatch {
        private static bool Prepare() => AccessTools.Method(typeof(ffxBloomPlus), "StartEffect") != null;
        private static bool Prefix() => !SimpleBloomActive;
    }

    // --- Flash: neutralise the flash colours ---
    [HarmonyPatch(typeof(ffxFlashPlus), "StartEffect")]
    private static class SimpleFlashPatch {
        private static bool Prepare() => AccessTools.Method(typeof(ffxFlashPlus), "StartEffect") != null;
        private static void Prefix(ffxFlashPlus __instance) {
            if(SimpleFlashActive) {
                __instance.startColor = Color.clear;
                __instance.endColor = Color.clear;
            }
        }
    }

    // --- Hall of Mirrors: skip the effect (the old cam field is gone) ---
    [HarmonyPatch(typeof(ffxHallOfMirrorsPlus), "StartEffect")]
    private static class SimpleHomPatch {
        private static bool Prepare() => AccessTools.Method(typeof(ffxHallOfMirrorsPlus), "StartEffect") != null;
        private static bool Prefix() => !SimpleHomActive;
    }

    // --- Screen shake: skip the effect ---
    [HarmonyPatch(typeof(ffxShakeScreenPlus), "StartEffect")]
    private static class SimpleShakePatch {
        private static bool Prepare() => AccessTools.Method(typeof(ffxShakeScreenPlus), "StartEffect") != null;
        private static bool Prefix() => !SimpleShakeActive;
    }

    // --- Move Track: clamp the moved tile range around the current tile ---
    [HarmonyPatch(typeof(ffxMoveFloorPlus), "StartEffect")]
    private static class SimpleMovePatch {
        private static bool Prepare() => AccessTools.Method(typeof(ffxMoveFloorPlus), "StartEffect") != null;
        private static int origStart;
        private static int origEnd;

        private static void Prefix(ffxMoveFloorPlus __instance) {
            if(!SimpleMoveActive) {
                return;
            }
            int max = Conf.SimpleMoveTrackMax;
            int index = scrController.instance.currFloor.seqID;
            origStart = __instance.start;
            origEnd = __instance.end;
            if(origEnd < index + max / 2) {
                __instance.start = Math.Max(origEnd - max - 1, origStart);
            } else if(origStart > index - max / 2) {
                __instance.end = Math.Min(origStart + max - 1, origEnd);
            } else {
                __instance.start = Math.Max(index - max / 2, origStart);
                __instance.end = Math.Min(index + max / 2, origEnd);
            }
        }

        private static void Postfix(ffxMoveFloorPlus __instance) {
            if(!SimpleMoveActive) {
                return;
            }
            __instance.start = origStart;
            __instance.end = origEnd;
        }
    }
}
