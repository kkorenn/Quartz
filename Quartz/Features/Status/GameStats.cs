using System.Globalization;
using UnityEngine;

namespace Quartz.Features.Status;

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

    // Original-pack formula: acc * prog + (1 - prog). Treats unplayed remainder
    // as still-recoverable to 100%, so this is the ceiling, not the current run.
    public static float MaxAccuracy {
        get {
            try {
                scrMistakesManager m = MistakesAccess.Get();
                float acc = MistakesAccess.PercentAcc(m);
                float prog = MistakesAccess.PercentComplete(m);
                float r = acc * prog + (1f - prog);
                if(float.IsNaN(r) || float.IsInfinity(r)) {
                    return 1f;
                }

                return Mathf.Clamp01(r);
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

    public static int CheckpointCount {
        get {
            try {
                return scnGame.instance != null ? scnGame.instance.checkpointsUsed : 0;
            } catch {
                return 0;
            }
        }
    }

    // Tile BPM (chart tempo * pitch * speed) and Current BPM (derived from the
    // current floor's nextfloor entry time). See Bpm.cs for details.
    public static void GetBpm(out float tileBpm, out float currentBpm) {
        Bpm.GetBpmValues(out tileBpm, out currentBpm);
    }

    public static string HoldBehaviorLabel => Hold.GetHoldBehaviorLabel();

    // Tiles the game played in the last second while autoplay is on (v1's
    // "Auto KPS" line).
    public static int AutoKps => Bpm.GetAutoKps();

    public static float MarginScale => TimingScale.CurrentMarginScale;

    // Song playback pitch (1.0 = 100%). This is the same audio pitch the BPM math
    // multiplies in, i.e. the speed the song is actually playing at (speed trials,
    // pitch-change events). Defaults to 1 off the floor / before the song loads.
    public static float Pitch {
        get {
            try {
                scrConductor c = scrConductor.instance;
                return c != null && c.song != null ? c.song.pitch : 1f;
            } catch {
                return 1f;
            }
        }
    }

    // XPerfect (UMM mod) per-run perfect breakdown — dead-center X, late +, and
    // early - counts. All 0 when XPerfect isn't installed.
    public static int XPerfectX => Interop.XPerfectBridge.XCount();
    public static int XPerfectPlus => Interop.XPerfectBridge.PlusCount();
    public static int XPerfectMinus => Interop.XPerfectBridge.MinusCount();

    public static int Combo => Quartz.Features.Combo.Combo.Count;

    // Current level's song artist and title from the custom-level metadata.
    // levelData.artist is the *song* artist (what the game shows in the title);
    // levelData.author is the chart creator — not what we want here. Empty for
    // built-in levels (no scnGame) — callers fall back to SongTitleRaw. Safe to
    // poll every frame.
    public static string SongArtist {
        get {
            try {
                var g = scnGame.instance;
                return g != null && g.levelData != null ? g.levelData.artist ?? "" : "";
            } catch {
                return "";
            }
        }
    }

    public static string SongTitle {
        get {
            try {
                var g = scnGame.instance;
                return g != null && g.levelData != null ? g.levelData.song ?? "" : "";
            } catch {
                return "";
            }
        }
    }

    // The game's own combined title text (e.g. "artist - title"), used as a
    // fallback when the separate author/song metadata isn't available.
    public static string SongTitleRaw {
        get {
            try {
                scrController c = scrController.instance;
                return c != null && c.txtLevelName != null ? c.txtLevelName.text ?? "" : "";
            } catch {
                return "";
            }
        }
    }

    // True when the current run started mid-level (via checkpoint). The HUD
    // can use this to render Progress as a "start% - now%" range.
    public static bool RunHasStartProgress => !ProgressTracker.RunStartedFromFirstTile
        && ProgressTracker.RunStartProgress > 0f;

    public static float RunStartProgress => ProgressTracker.RunStartProgress;

    public static int SessionAttempts => PlayCount.PlayCount.SessionAttempts;
    public static int TotalAttempts => PlayCount.PlayCount.TotalAttemptsForCurrentMap();
    public static float Best => PlayCount.PlayCount.BestForCurrentMap();
    public static float BestStart => PlayCount.PlayCount.BestStartForCurrentMap();

    // The time texts only change once a second (FormatTime truncates to whole
    // seconds), but the getters are polled every frame per panel. Cache the
    // formatted string keyed on the truncated seconds so the ~5-7 string allocs
    // happen ~1×/sec instead of 60-240×/sec.
    private static int musicTimeCurSec = -1, musicTimeLenSec = -1;
    private static string musicTimeCache = "0:00 / 0:00";
    private static int mapTimeSec = -1, mapTimeTotalSec = int.MinValue;
    private static string mapTimeCache = "0:00";

    public static string MusicTimeText {
        get {
            try {
                AudioSource a = scrConductor.instance != null ? scrConductor.instance.song : null;
                if(a == null || a.clip == null) {
                    return "0:00 / 0:00";
                }

                int curSec = (int)Mathf.Max(0f, a.time);
                int lenSec = (int)Mathf.Max(0f, a.clip.length);
                if(curSec != musicTimeCurSec || lenSec != musicTimeLenSec) {
                    musicTimeCurSec = curSec;
                    musicTimeLenSec = lenSec;
                    bool hour = a.clip.length >= 3600f;
                    musicTimeCache = FormatTime(a.time, hour) + " / " + FormatTime(a.clip.length, hour);
                }
                return musicTimeCache;
            } catch {
                return "0:00 / 0:00";
            }
        }
    }

    // How far through the song / map we are, 0..1 — drives the per-stat color
    // gradients for the time stats (v1 GetPrimaryTimeRatio / GetMapTimeRatio).
    public static float MusicTimeRatio {
        get {
            try {
                AudioSource song = scrConductor.instance != null ? scrConductor.instance.song : null;
                if(song == null || song.clip == null || song.clip.length <= 0f) {
                    return 0f;
                }
                return Mathf.Clamp01(song.time / song.clip.length);
            } catch {
                return 0f;
            }
        }
    }

    public static float MapTimeRatio {
        get {
            try {
                scrConductor cd = scrConductor.instance;
                if(cd == null) {
                    return 0f;
                }

                float time = (float)(cd.addoffset + cd.songposition_minusi);
                float total = MapTotalSeconds();
                if(total <= 0f) {
                    return 0f;
                }
                return Mathf.Clamp01(time / total);
            } catch {
                return 0f;
            }
        }
    }

    public static string MapTimeText {
        get {
            try {
                scrConductor cd = scrConductor.instance;
                if(cd == null) {
                    return "0:00";
                }

                float t = (float)(cd.addoffset + cd.songposition_minusi);
                float total = MapTotalSeconds();

                if(t < 0f) {
                    t = 0f;
                }

                if(total > 0f && t > total) {
                    t = total;
                }

                int tSec = (int)t;
                int totalSec = total > 0f ? (int)total : -1;
                if(tSec != mapTimeSec || totalSec != mapTimeTotalSec) {
                    mapTimeSec = tSec;
                    mapTimeTotalSec = totalSec;
                    if(total > 0f) {
                        bool hour = total >= 3600f;
                        mapTimeCache = FormatTime(t, hour) + " / " + FormatTime(total, hour);
                    } else {
                        mapTimeCache = FormatTime(t);
                    }
                }
                return mapTimeCache;
            } catch {
                return "0:00";
            }
        }
    }

    // Smoothed FPS — same exponential smoothing as the original pack. Adaptive
    // tau: small swings settle slowly (steady reading), big swings catch up
    // fast. Reads Time.unscaledDeltaTime so it tracks real wall-clock fps.
    // Expected to be polled once per frame from the HUD updater.
    public static int Fps {
        get {
            int frame = Time.frameCount;
            if(fpsFrame == frame) {
                return fpsFrameValue;
            }
            fpsFrame = frame;

            float dt = Time.unscaledDeltaTime;
            if(dt <= 0f) {
                fpsFrameValue = Mathf.RoundToInt(smoothedFps);
                return fpsFrameValue;
            }

            float fps = 1f / dt;
            if(smoothedFps <= 0f) {
                smoothedFps = fps;
            } else {
                float diff = Mathf.Abs(fps - smoothedFps);
                float t = Mathf.Clamp01(diff * fpsSensitivity);
                float smooth = Mathf.Lerp(fpsMinSmooth, fpsMaxSmooth, t);
                float factor = 1f - Mathf.Exp(-smooth * dt);
                smoothedFps += (fps - smoothedFps) * factor;
            }

            fpsFrameValue = Mathf.RoundToInt(smoothedFps);
            return fpsFrameValue;
        }
    }

    private static float smoothedFps;
    private static int fpsFrame = -1;
    private static int fpsFrameValue;
    private const float fpsMinSmooth = 2f;
    private const float fpsMaxSmooth = 12f;
    private const float fpsSensitivity = 0.08f;

    private static float MapTotalSeconds() {
        try {
            scrLevelMaker lm = scrLevelMaker.instance;
            if(lm == null || lm.listFloors == null || lm.listFloors.Count == 0) {
                return 0f;
            }

            scrFloor last = lm.listFloors[lm.listFloors.Count - 1];
            return last != null ? (float)last.entryTime : 0f;
        } catch {
            return 0f;
        }
    }

    private static string FormatTime(float seconds, bool forceHour = false) {
        if(seconds < 0f) {
            seconds = 0f;
        }

        int total = (int)seconds;
        if(forceHour || total >= 3600) {
            return (total / 3600).ToString(CultureInfo.InvariantCulture)
                + ":" + ((total % 3600) / 60).ToString("00", CultureInfo.InvariantCulture)
                + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        int m = total / 60;
        int s = total % 60;
        return m.ToString(CultureInfo.InvariantCulture)
            + ":" + s.ToString("00", CultureInfo.InvariantCulture);
    }
}
