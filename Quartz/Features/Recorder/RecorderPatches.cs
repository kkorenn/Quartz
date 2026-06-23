using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Quartz.Core;
using MonsterLove.StateMachine;

namespace Quartz.Features.Recorder;

// Harmony hooks that bind the renderer to a level run. All gate on the mod being
// enabled and on the recorder's own state, so they're inert unless a render is
// armed or in progress.
internal static class RecorderPatches {
    // Level starts playing — if armed, this is frame zero of the render.
    [HarmonyPatch(typeof(scnGame), "Play")]
    private static class ScnGamePlayPatch {
        private static void Postfix() {
            if(MainCore.IsModEnabled && Recorder.IsArmed) {
                Recorder.BeginSession();
            }
        }
    }

    // Landed on the end portal — the run is complete, finalize the file.
    [HarmonyPatch(typeof(scrController), "OnLandOnPortal")]
    private static class OnLandOnPortalPatch {
        private static void Postfix() {
            if(MainCore.IsModEnabled && Recorder.IsRecording) {
                Recorder.NotifyComplete();
            }
        }
    }

    // Won is a redundant safety net for completion; Fail2 (death) ends the run, so
    // save what we have rather than leaving the player locked in a dead level.
    [HarmonyPatch(typeof(StateBehaviour), "ChangeState", new[] { typeof(Enum) })]
    private static class StateChangePatch {
        private static void Postfix(Enum newState) {
            if(!MainCore.IsModEnabled || !Recorder.IsRecording) {
                return;
            }
            if(newState is not States state) {
                return;
            }
            if(state == States.Won) {
                Recorder.NotifyComplete();
            } else if(state == States.Fail2) {
                Recorder.NotifyFailed();
            }
        }
    }

    // Lock the level while recording: swallow every pause/exit toggle so the run
    // can't be interrupted (Esc-to-cancel is handled in RecorderSession instead).
    [HarmonyPatch(typeof(scrController), "TogglePauseGame")]
    private static class LockPausePatch {
        private static bool Prefix(ref bool __result) {
            if(MainCore.IsModEnabled && Recorder.Locked) {
                __result = false;
                return false;
            }
            return true;
        }
    }

    // Redirect the conductor's audio-clock reads to the render's controlled clock.
    // scrConductor.Update reads AudioSettings.dspTime and from it advances song
    // position, fires beat events and steps tile progression; rewriting those
    // reads to ControlledDspTime makes the whole conductor advance one frame per
    // captured frame. Gated at runtime by DrivingClock, so it's a no-op (returns
    // the real clock) outside a render. Start/Rewind are included so the song
    // anchor is set against the same clock.
    [HarmonyPatch]
    internal static class ConductorClockPatch {
        private static IEnumerable<MethodBase> TargetMethods() {
            foreach(string name in new[] { "Update", "Start", "Rewind" }) {
                MethodBase m = AccessTools.Method(typeof(scrConductor), name);
                if(m != null) {
                    yield return m;
                }
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            MethodInfo dspTime = AccessTools.PropertyGetter(typeof(UnityEngine.AudioSettings), "dspTime");
            MethodInfo replacement = AccessTools.Method(typeof(Recorder), nameof(Recorder.ControlledDspTime));

            foreach(CodeInstruction ci in instructions) {
                if(dspTime != null && replacement != null && ci.Calls(dspTime)) {
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                } else {
                    yield return ci;
                }
            }
        }
    }
}
