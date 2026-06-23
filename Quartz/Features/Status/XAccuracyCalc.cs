using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quartz.Features.Status
{
    internal static class XAccuracyCalc
    {
        private const double CheckpointPenalty = 0.9875;

        private static List<scrFloor> cachedFloors;
        private static int cachedFloorCount = -1;
        private static int[] prefixHittable;
        private static int cachedHittableTotal;

        private static void EnsurePrefix(List<scrFloor> floors)
        {
            int count = floors.Count;
            if (ReferenceEquals(floors, cachedFloors) && count == cachedFloorCount && prefixHittable != null)
                return;

            int[] prefix = new int[count];
            int running = 0;
            for (int i = 0; i < count; i++)
            {
                scrFloor f = floors[i];
                if (f != null && !f.auto && !f.midSpin) running++;
                prefix[i] = running;
            }

            cachedFloors = floors;
            cachedFloorCount = count;
            prefixHittable = prefix;
            cachedHittableTotal = running;
        }

        private static int RemainingHittable()
        {
            scrLevelMaker lm = scrLevelMaker.instance;
            scrController ctrl = scrController.instance;
            if (lm == null || lm.listFloors == null || ctrl == null) return 0;

            List<scrFloor> floors = lm.listFloors;
            int count = floors.Count;
            if (count == 0) return 0;
            EnsurePrefix(floors);

            int seq = ctrl.currentSeqID;
            if (seq < 0) seq = 0;
            if (seq > count - 1) seq = count - 1;

            int consumedHittable = prefixHittable[seq];
            int remaining = cachedHittableTotal - consumedHittable;
            return remaining < 0 ? 0 : remaining;
        }

        private static int Hits(scrMarginTracker t, HitMargin m)
        {
            int i = (int)m;
            int[] counts = t.hitMarginsCount;
            return (counts != null && i >= 0 && i < counts.Length) ? counts[i] : 0;
        }

        internal static float MaxRatio()
        {
            return MaxRatio(0);
        }

        internal static float MaxRatio(int playerID)
        {
            try
            {
                scrController ctrl = scrController.instance;
                if (ctrl == null) return 1f;
                scrMarginTracker t = MistakesAccess.Tracker(playerID);
                if (t == null || t.hitMargins == null) return 1f;

                double checkpointFactor = Math.Pow(CheckpointPenalty, scrController.checkpointsUsed);

                int deadTiles = t.deadTiles;

                double weightedSum =
                      1.0  * (Hits(t, HitMargin.Perfect) + Hits(t, HitMargin.Auto))
                    + 0.75 * (Hits(t, HitMargin.EarlyPerfect) + Hits(t, HitMargin.LatePerfect))
                    + 0.4  * (Hits(t, HitMargin.VeryEarly) + Hits(t, HitMargin.VeryLate))
                    + 0.2  * (Hits(t, HitMargin.TooEarly) + Hits(t, HitMargin.TooLate))
                    + 0.2  * deadTiles;

                double denom = t.hitMargins.Count + deadTiles;

                int remaining = RemainingHittable();
                int playerCount = MistakesAccess.PlayerCount();
                if (playerCount > 1) remaining = remaining / playerCount;

                double finalNumerator = weightedSum + remaining;
                double finalDenominator = denom + remaining;
                if (finalDenominator <= 0.0) return Mathf.Clamp01((float)checkpointFactor);

                double max = checkpointFactor * (finalNumerator / finalDenominator);
                if (double.IsNaN(max) || double.IsInfinity(max)) return 1f;
                return Mathf.Clamp01((float)max);
            }
            catch { return 1f; }
        }
    }
}
