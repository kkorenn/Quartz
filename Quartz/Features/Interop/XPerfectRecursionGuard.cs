using System.Reflection;
using HarmonyLib;
using Quartz.Core;

namespace Quartz.Features.Interop;

// Reentry guard on XPerfect.HitMarginPatch.Postfix, ported from the original
// KorenResourcePack (src/Compatibility/XPerfectRecursionGuard.cs).
//
// XPerfect re-grades the hit inside its own scrMarginTracker.AddHit postfix.
// Quartz also patches AddHit (Combo / Judgement), and some paths re-enter AddHit
// — which would re-run XPerfect's postfix recursively and double-count / stack-
// overflow. This wraps XPerfect's postfix with a thread-static depth guard so it
// runs at most once per top-level hit. No-op (and harmless) when XPerfect isn't
// installed; idempotent once applied.
internal static class XPerfectRecursionGuard {
    [ThreadStatic] private static int depth;

    private static bool applied;

    // Safe to call repeatedly (e.g. every scene load): installs the guard the
    // first time XPerfect's patch type exists, then no-ops. That covers XPerfect
    // loading after Quartz, without a hard ordering dependency.
    public static void TryApply(HarmonyLib.Harmony harmony) {
        if(applied || harmony == null) {
            return;
        }
        try {
            Type patchType = AccessTools.TypeByName("XPerfect.HitMarginPatch");
            if(patchType == null) {
                return;
            }

            MethodInfo target = AccessTools.Method(patchType, "Postfix");
            if(target == null) {
                MainCore.Log.Msg("[XPerfectGuard] XPerfect.HitMarginPatch.Postfix not found; guard not installed.");
                applied = true; // type exists but no postfix — stop retrying.
                return;
            }

            MethodInfo prefix = typeof(XPerfectRecursionGuard).GetMethod(
                nameof(GuardPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo finalizer = typeof(XPerfectRecursionGuard).GetMethod(
                nameof(GuardFinalizer), BindingFlags.Static | BindingFlags.NonPublic);

            PatchCompat(harmony, target, new HarmonyMethod(prefix), new HarmonyMethod(finalizer));

            applied = true;
            MainCore.Log.Msg("[XPerfectGuard] Installed reentry guard on XPerfect.HitMarginPatch.Postfix.");
        } catch (Exception ex) {
            MainCore.Log.Msg("[XPerfectGuard] Install failed: " + ex.Message);
        }
    }

    // Calls Harmony.Patch by reflection instead of a direct call. HarmonyX
    // (MelonLoader) and plain Harmony 2.x (vanilla UnityModManager) have
    // DIFFERENT Patch signatures — HarmonyX's takes a 6th HarmonyMethod
    // (ilmanipulator), vanilla's has only 5 params. A compiled direct call binds
    // to whichever we built against (HarmonyX) and throws MissingMethodException
    // when JIT'd on the other, aborting the whole mod. Reflection picks the
    // overload that actually exists at runtime and fills the trailing param(s)
    // with null. Param order after the target is prefix, postfix, transpiler,
    // finalizer, [ilmanipulator].
    private static void PatchCompat(HarmonyLib.Harmony harmony, MethodBase target,
                                    HarmonyMethod prefix, HarmonyMethod finalizer) {
        MethodInfo patch = null;
        foreach(MethodInfo m in harmony.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
            ParameterInfo[] p = m.Name == "Patch" ? m.GetParameters() : null;
            if(p != null && p.Length >= 5 && typeof(MethodBase).IsAssignableFrom(p[0].ParameterType)) {
                patch = m;
                break;
            }
        }
        if(patch == null) {
            throw new MissingMethodException("HarmonyLib.Harmony.Patch not found");
        }

        ParameterInfo[] ps = patch.GetParameters();
        object[] args = new object[ps.Length];
        args[0] = target;
        // prefix, postfix, transpiler, finalizer, [ilmanipulator]
        HarmonyMethod[] ordered = { prefix, null, null, finalizer, null };
        for(int i = 1; i < ps.Length; i++) {
            args[i] = i - 1 < ordered.Length ? ordered[i - 1] : null;
        }
        patch.Invoke(harmony, args);
    }

    private static bool GuardPrefix(ref bool __state) {
        __state = false;
        if(depth > 0) {
            return false; // already inside XPerfect's postfix — skip the reentry.
        }
        depth++;
        __state = true;
        return true;
    }

    private static Exception GuardFinalizer(bool __state, Exception __exception) {
        if(__state && depth > 0) {
            depth--;
        }
        return __exception;
    }
}
