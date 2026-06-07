using HarmonyLib;
using Koren.Core;
using UnityEngine;

namespace Koren.Features.Status;

// Tracks where the current run started — first tile (0%) vs. resumed from
// checkpoint (n%). The Status HUD uses this to render Progress as a range
// ("12.34% - 56.78%") for partial-checkpoint runs instead of just the
// instantaneous value.
//
// Captured on three game events via the Harmony patches below; all are gated
// on the mod being enabled so nothing fires when the user has the mod off.
internal static class ProgressTracker {
    internal static float RunStartProgress;
    internal static bool RunStartedFromFirstTile = true;

    internal static bool IsFirstTileRunStart() {
        try {
            if(scnGame.instance != null) {
                return scnGame.instance.checkpointsUsed == 0;
            }
        } catch {
        }

        try {
            return scrController.checkpointsUsed == 0;
        } catch {
            return false;
        }
    }

    internal static void CaptureRunStart() {
        try {
            scrController c = scrController.instance;
            float progress = c != null ? c.percentComplete : 0f;
            RunStartedFromFirstTile = IsFirstTileRunStart();
            RunStartProgress = RunStartedFromFirstTile ? 0f : Mathf.Clamp01(progress);
        } catch {
            RunStartedFromFirstTile = true;
            RunStartProgress = 0f;
        }
    }

    [HarmonyPatch(typeof(scrController), "RestartProgress")]
    private static class RestartProgressPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }
            CaptureRunStart();
        }
    }

    [HarmonyPatch(typeof(scrController), "Restart", typeof(bool))]
    private static class RestartPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }
            CaptureRunStart();
        }
    }

    [HarmonyPatch(typeof(scrMistakesManager), "RevertToLastCheckpoint")]
    private static class RevertCheckpointPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }
            CaptureRunStart();
        }
    }
}
