using HarmonyLib;
using Quartz.Core;
using UnityEngine;

namespace Quartz.Features.Status;

// BPM math ported from the original KorenResourcePack.
// TBPM = chart-defined bpm * song pitch * planetarySystem.speed
// CBPM = derived from the current floor's nextfloor entry time (the "real" bpm
//        at this moment, including BPM-changing tiles), * song pitch.
// If the current floor has no nextfloor, CBPM falls back to TBPM.
internal static class Bpm {
    // Autoplay KPS (v1 Bpm.RegisterAutoTile/GetAutoKps): autoplay tile-hits
    // inside a sliding 1-second window. Real key presses don't fire during
    // autoplay, so a key-based KPS reads 0 — this counts the tiles the game
    // plays for you instead.
    private static readonly Queue<float> autoTileTimes = new();

    internal static int GetAutoKps() {
        float now = Time.time;
        while(autoTileTimes.Count > 0 && now - autoTileTimes.Peek() > 1f) {
            autoTileTimes.Dequeue();
        }
        return autoTileTimes.Count;
    }

    // One tile-hit per MoveToNextFloor, counted only while autoplay is on —
    // same hook v1 used.
    [HarmonyPatch(typeof(scrPlanet), "MoveToNextFloor")]
    private static class MoveToNextFloorPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }

            try {
                if(RDC.auto) {
                    autoTileTimes.Enqueue(Time.time);
                }
            } catch {
            }
        }
    }

    internal static void GetBpmValues(out float tileBpm, out float actualBpm) {
        tileBpm = 0f;
        actualBpm = 0f;

        try {
            scrController controller = scrController.instance;
            scrConductor conductor = scrConductor.instance;
            scrFloor floor = controller != null ? (controller.currFloor ?? controller.firstFloor) : null;

            if(controller == null || conductor == null || floor == null || conductor.song == null) {
                return;
            }

            double speed = controller.planetarySystem != null ? controller.planetarySystem.speed : 1.0;
            tileBpm = (float)(conductor.bpm * conductor.song.pitch * speed);
            // Guard a zero/near-zero tile duration (e.g. midspin): 60/dt would be
            // IEEE Infinity (no exception), which renders as garbage in the panel.
            double dt = floor.nextfloor ? floor.nextfloor.entryTime - floor.entryTime : 0.0;
            actualBpm = floor.nextfloor && dt > 1e-9
                ? (float)(60.0 / dt * conductor.song.pitch)
                : tileBpm;
        } catch {
            tileBpm = 0f;
            actualBpm = 0f;
        }
    }
}
