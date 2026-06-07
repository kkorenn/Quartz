namespace Koren.Features.Status;

// Live ADOFAI gameplay stats. All game access is guarded so it's safe to poll
// every frame; off the dance floor InGame is false and the HUD hides.
// Stat math (XAccuracyCalc.MaxRatio) + accessors ported from the original
// KorenResourcePack. Game types (scrController, scrMistakesManager) live in the
// global namespace inside Assembly-CSharp.
public static class GameStats {
    // True only while actually playing a level (the dance-floor world is live).
    public static bool InGame {
        get {
            try {
                scrController c = scrController.instance;
                if (c == null || !c.gameworld || c.paused) return false;

                if (ADOBase.isLevelEditor) {
                    scnEditor ed = scnEditor.instance;
                    if (ed != null && ed.inStrictlyEditingMode) return false;
                }

                return true;
            } catch {
                return false;
            }
        }
    }

    public static float Progress {
        get {
            try {
                scrController c = scrController.instance;
                if (c == null) return 0f;
                if (c.currentSeqID == 0) return 0f;
                return c.percentComplete;
            } catch {
                return 0f;
            }
        }
    }

    public static float Accuracy {
        get {
            try {
                return MistakesAccess.PercentAcc(MistakesAccess.Get());
            } catch {
                return 1f;
            }
        }
    }

    public static float XAccuracy {
        get {
            try {
                return MistakesAccess.PercentXAcc(MistakesAccess.Get());
            } catch {
                return 1f;
            }
        }
    }

    public static float MaxXAccuracy {
        get {
            try {
                return XAccuracyCalc.MaxRatio();
            } catch {
                return 1f;
            }
        }
    }
}
