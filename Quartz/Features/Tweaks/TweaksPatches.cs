using HarmonyLib;
using UnityEngine;

namespace Quartz.Features.Tweaks;

// Harmony patches for the Tweaks feature, ported from v1's TweaksPatches.cs.
// Apply-on-spawn patches re-run the relevant tweak whenever the game builds
// or revives the objects it touches; the Awake/Decode/Start postfixes also
// invalidate the scene-object caches those tweaks iterate.
public static partial class Tweaks {
    [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
    private static class ClearCachesOnSceneChangePatch {
        private static void Postfix() => ClearSceneCaches();
    }

    [HarmonyPatch(typeof(ffxCheckpoint), "get_runOnHit")]
    private static class CheckpointRunOnHitPatch {
        private static bool Prefix(ref bool __result) {
            if(!ShouldRemoveCheckpoints) {
                return true;
            }
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ffxCheckpoint), "Awake")]
    private static class CheckpointAwakePatch {
        private static void Postfix(ffxCheckpoint __instance) {
            InvalidateCheckpointCache();
            if(ShouldRemoveCheckpoints) {
                RemoveCheckpointVisual(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ffxCheckpoint), "Decode")]
    private static class CheckpointDecodePatch {
        private static void Postfix(ffxCheckpoint __instance) {
            InvalidateCheckpointCache();
            if(ShouldRemoveCheckpoints) {
                RemoveCheckpointVisual(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ffxCheckpoint), "StartEffect")]
    private static class CheckpointStartEffectPatch {
        private static bool Prefix(ffxCheckpoint __instance) {
            if(!ShouldRemoveCheckpoints) {
                return true;
            }
            RemoveCheckpointVisual(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(scrMistakesManager), "MarkCheckpoint")]
    private static class MistakesMarkCheckpointPatch {
        private static bool Prefix() => !ShouldRemoveCheckpoints;
    }

    // LightUp is what tints a floor when the planet hits it; disableGlow is
    // forced for the duration of the call, then the glow objects/colors it
    // still spawned are cleaned up. SetToRandomColor re-runs from inside
    // LightUp (tracked via lightUpDepth) or right after it (tracked via the
    // one-shot suppress set), and gets the same cleanup.
    [HarmonyPatch(typeof(scrFloor), "LightUp")]
    private static class FloorLightUpPatch {
        private static void Prefix(scrFloor __instance) {
            if(!ShouldDisableTileHitGlow || __instance == null) {
                return;
            }

            lightUpDepth++;
            try {
                int id = __instance.GetInstanceID();
                if(!lightUpDisableGlowStates.ContainsKey(id)) {
                    lightUpDisableGlowStates[id] = __instance.disableGlow;
                }
                __instance.disableGlow = true;
            } catch {
            }
        }

        private static void Postfix(scrFloor __instance) {
            if(__instance == null) {
                return;
            }

            if(lightUpDepth > 0) {
                lightUpDepth--;
            }

            int id;
            try { id = __instance.GetInstanceID(); }
            catch { return; }

            try {
                if(lightUpDisableGlowStates.TryGetValue(id, out bool wasDisabled)) {
                    __instance.disableGlow = wasDisabled;
                    lightUpDisableGlowStates.Remove(id);
                }
            } catch {
            }

            if(!ShouldDisableTileHitGlow) {
                return;
            }

            suppressNextRandomColorFloorIds.Add(id);
            SuppressFloorHitGlow(__instance);
        }
    }

    [HarmonyPatch(typeof(scrFloor), "SetToRandomColor")]
    private static class FloorSetToRandomColorPatch {
        private static bool Prefix(scrFloor __instance) {
            if(!ShouldDisableTileHitGlow || __instance == null) {
                return true;
            }

            int id;
            try { id = __instance.GetInstanceID(); }
            catch { return true; }

            if(lightUpDepth <= 0 && !suppressNextRandomColorFloorIds.Remove(id)) {
                return true;
            }

            SuppressFloorHitGlow(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlanetRenderer), "Awake")]
    private static class PlanetRendererAwakePatch {
        private static void Postfix(PlanetRenderer __instance) {
            InvalidateRendererCache();
            ApplyBallCoreParticlesTweak(__instance);
        }
    }

    [HarmonyPatch(typeof(PlanetRenderer), "Revive")]
    private static class PlanetRendererRevivePatch {
        private static void Postfix(PlanetRenderer __instance) {
            InvalidateRendererCache();
            ApplyBallCoreParticlesTweak(__instance);
        }
    }

    [HarmonyPatch(typeof(PlanetRenderer), "PlayParticles")]
    private static class PlanetRendererPlayParticlesPatch {
        private static void Postfix(PlanetRenderer __instance) {
            ApplyBallCoreParticlesTweak(__instance);
        }
    }

    [HarmonyPatch(typeof(PlanetRenderer), "LateUpdate")]
    private static class PlanetRendererLateUpdatePatch {
        private static void Postfix(PlanetRenderer __instance) {
            if(ShouldRemoveBallCoreParticles) {
                ApplyBallCoreParticlesTweak(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(PlanetRenderer), "SetCoreColor")]
    private static class PlanetRendererSetCoreColorPatch {
        private static bool Prefix(PlanetRenderer __instance) {
            if(!ShouldRemoveBallCoreParticles) {
                return true;
            }
            ApplyBallCoreParticlesTweak(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlanetRenderer), "SetParticleSystemColor")]
    private static class PlanetRendererSetParticleSystemColorPatch {
        private static bool Prefix(PlanetRenderer __instance, ParticleSystem particleSystem) {
            if(!ShouldRemoveBallCoreParticles || !IsRemovedPlanetParticle(__instance, particleSystem)) {
                return true;
            }

            ApplyPlanetParticleTweak(particleSystem, false);
            return false;
        }
    }

    [HarmonyPatch(typeof(scrPlanet), "Start")]
    private static class PlanetStartPatch {
        private static void Postfix(scrPlanet __instance) {
            InvalidateRendererCache();
            try { ApplyBallCoreParticlesTweak(__instance.planetRenderer); } catch { }
            try { ApplyPlanetGlowTweak(__instance.planetRenderer); } catch { }
        }
    }

    // Auto-play pauses itself when the window loses focus; with the tweak on,
    // that unsolicited pause is swallowed while pauses the player or the
    // editor actually requested (checked by call site) still go through.
    [HarmonyPatch(typeof(scrController), "TogglePauseGame")]
    private static class DisableAutoPauseTogglePatch {
        private static bool Prefix(scrController __instance, ref bool __result) {
            if(!ShouldDisableAutoPause || __instance == null) {
                return true;
            }

            bool autoOn;
            try { autoOn = RDC.auto; }
            catch { return true; }
            if(!autoOn) {
                return true;
            }

            bool currentlyPaused;
            try { currentlyPaused = __instance.paused; }
            catch { return true; }
            if(currentlyPaused) {
                return true;
            }

            if(IsSafePauseCallSite()) {
                return true;
            }

            // In the level editor the play-mode space-pause sets
            // scnEditor.pausedInPlayMode (and greys the autoplay button) before
            // it ever calls TogglePauseGame, so swallowing the pause alone would
            // wedge the "Autoplay + Paused" text on with the game still running.
            // Roll that back so suppression keeps auto-play cleanly running.
            ResetEditorPlayModePauseState();

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(RDInput), "get_mouseScrollDelta")]
    private static class BlockMouseWheelScrollPatch {
        private static void Postfix(ref Vector2 __result) {
            if(ShouldBlockMouseWheelScroll) {
                __result = Vector2.zero;
            }
        }
    }

    [HarmonyPatch(typeof(scrConductor), "Update")]
    private static class DisableMenuMusicPatch {
        private static void Postfix(scrConductor __instance) => ApplyMenuMusicMute(__instance);
    }

    // Custom main-menu BPM: the rabbit floor only exists on the menu, so these
    // patches are menu-only. Start sets the resting (slow) speed; StartEffect's
    // prefix replaces the 1x/2x toggle with our slow/high BPM speeds.
    [HarmonyPatch(typeof(ffxMenuPlanetSpeedChange), "Start")]
    private static class MenuBpmInitPatch {
        private static void Postfix() {
            try {
                ApplyInitialMenuBpm();
            } catch {
            }
        }
    }

    [HarmonyPatch(typeof(ffxMenuPlanetSpeedChange), "StartEffect", new[] { typeof(scrPlanet) })]
    private static class MenuBpmTogglePatch {
        private static bool Prefix(ffxMenuPlanetSpeedChange __instance) {
            try {
                // Skip the original only when we handled the toggle.
                return !HandleMenuBpmToggle(__instance.floor);
            } catch {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(DetailedResults), "GenerateResults")]
    private static class DetailedResultsGeneratePatch {
        private static void Postfix(ref string __result) {
            __result = FilterDetailedResults(__result);
        }
    }
}
