using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Quartz.Features.PlanetColors;

// Harmony patches for Planet Colors, ported from v1's ResourceChangerPatches
// planet-color section. Two layers:
//   - apply-on-spawn: re-color planets whenever the game (re)builds them
//   - force-in-flight: the game keeps calling its own color setters (level
//     events, revives, level-select effects); the prefixes rewrite those
//     arguments to ours so vanilla colors never flash through.
// All of it no-ops while `applying` is set — that's our own apply path
// calling the same methods.
public static partial class PlanetColors {
    private static IEnumerable<MethodBase> ExistingMethods(Type type, params string[] names) {
        for(int i = 0; i < names.Length; i++) {
            MethodInfo method = AccessTools.Method(type, names[i]);
            if(method != null) {
                yield return method;
            }
        }
    }

    [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
    private static class ClearCachesOnSceneChangePatch {
        private static void Postfix() => ClearSceneCaches();
    }

    [HarmonyPatch(typeof(scrPlanet), "Start")]
    private static class PlanetStartPatch {
        private static void Postfix(scrPlanet __instance) {
            InvalidatePlanetCache();
            if(ShouldChange) {
                ApplyPlanetColor(__instance);
                try { ApplyPlanetRing(__instance.planetRenderer); } catch { }
            }
        }
    }

    [HarmonyPatch(typeof(PlanetRenderer), "Awake")]
    private static class PlanetRendererAwakePatch {
        private static void Postfix(PlanetRenderer __instance) {
            InvalidatePlanetCache();
            if(ShouldChange) {
                ApplyPlanetRendererColor(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(PlanetRenderer), "Revive")]
    private static class PlanetRendererRevivePatch {
        private static void Postfix(PlanetRenderer __instance) {
            if(ShouldChange) {
                ApplyPlanetRendererColor(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(PlanetRenderer), "PlayParticles")]
    private static class PlanetRendererPlayParticlesPatch {
        private static void Postfix(PlanetRenderer __instance) {
            if(!ShouldChange) {
                return;
            }
            ApplyTailParticleColor(__instance, TailColor(GetPlanetSlot(__instance)));
        }
    }

    // The ring color is reasserted by the game per frame in places, so our ring
    // override (recolor or hide) rides LateUpdate like v1 did.
    [HarmonyPatch(typeof(PlanetRenderer), "LateUpdate")]
    private static class PlanetRendererLateUpdatePatch {
        private static void Postfix(PlanetRenderer __instance) {
            if(ShouldChange) {
                ApplyPlanetRing(__instance);
            }
        }
    }

    // Whole-planet recolors (skin loads, rainbow effect, SetColor from level
    // events) are replaced wholesale with our colors.
    [HarmonyPatch]
    private static class PlanetRendererColorBlockPatch {
        private static IEnumerable<MethodBase> TargetMethods()
            => ExistingMethods(typeof(PlanetRenderer), "SetRainbow", "LoadPlanetColor", "SetColor");

        private static bool Prefix(PlanetRenderer __instance) {
            if(applying || !ShouldChange) {
                return true;
            }
            ApplyPlanetRendererColor(__instance);
            ApplyLogoColor(scrLogoText.instance);
            return false;
        }
    }

    // Targeted setters keep running (other mods/effects call them) but with
    // the color argument swapped for ours.
    [HarmonyPatch]
    private static class PlanetRendererForceColorPatch {
        private static IEnumerable<MethodBase> TargetMethods()
            => ExistingMethods(typeof(PlanetRenderer), "SetPlanetColor", "SetCoreColor", "SetTailColor", "SetFaceColor");

        private static void Prefix(PlanetRenderer __instance, MethodBase __originalMethod, ref Color __0) {
            if(applying || !ShouldChange) {
                return;
            }

            int slot = GetPlanetSlot(__instance);
            __0 = __originalMethod != null && __originalMethod.Name == "SetTailColor"
                ? TailColor(slot)
                : BallColor(slot);
        }

        private static void Postfix(PlanetRenderer __instance, MethodBase __originalMethod) {
            if(applying || !ShouldChange || __originalMethod == null || __originalMethod.Name != "SetTailColor") {
                return;
            }

            ApplyTailParticleColor(__instance, TailColor(GetPlanetSlot(__instance)));
        }
    }

    // Title-screen logo: the FIRE/ICE words are tinted with the planet
    // colors by the game; while custom colors are on they get ours instead
    // (both words use Planet 1's color, like v1).
    [HarmonyPatch(typeof(scrLogoText), "Awake")]
    private static class LogoAwakePatch {
        private static void Postfix(scrLogoText __instance) {
            if(ShouldChange) {
                ApplyLogoColor(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(scrLogoText), "UpdateColors")]
    private static class LogoUpdateColorsPatch {
        private static bool Prefix(scrLogoText __instance) {
            if(!ShouldChange) {
                return true;
            }
            ApplyLogoColor(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(scrLogoText), "LateUpdate")]
    private static class LogoLateUpdatePatch {
        private static bool Prefix() => !ShouldChange;
    }

    // Level-select planets cycle rainbow/enby colors; blocked while custom
    // colors are on, like v1.
    [HarmonyPatch(typeof(PlanetarySystem), "RainbowMode")]
    private static class LevelSelectRainbowPatch {
        private static bool Prefix() => !ShouldChange;
    }

    [HarmonyPatch(typeof(PlanetarySystem), "EnbyMode")]
    private static class LevelSelectEnbyPatch {
        private static bool Prefix() => !ShouldChange;
    }
}
