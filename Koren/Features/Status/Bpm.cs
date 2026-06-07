namespace Koren.Features.Status;

// BPM math ported from the original KorenResourcePack.
// TBPM = chart-defined bpm * song pitch * planetarySystem.speed
// CBPM = derived from the current floor's nextfloor entry time (the "real" bpm
//        at this moment, including BPM-changing tiles), * song pitch.
// If the current floor has no nextfloor, CBPM falls back to TBPM.
internal static class Bpm {
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
            actualBpm = floor.nextfloor
                ? (float)(60.0 / (floor.nextfloor.entryTime - floor.entryTime) * conductor.song.pitch)
                : tileBpm;
        } catch {
            tileBpm = 0f;
            actualBpm = 0f;
        }
    }
}
