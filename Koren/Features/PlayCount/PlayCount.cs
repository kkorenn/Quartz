using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Koren.Compat.Interface;
using Koren.Core;
using MonsterLove.StateMachine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Koren.Features.PlayCount;

// Simplified port of the original KorenResourcePack PlayCount.
// Tracks two things per map:
//   • TotalAttempts — lifetime starts on this map, persisted
//   • BestProgress  — highest percentComplete ever reached, persisted
// And one in-memory counter:
//   • SessionAttempts — starts of the currently-loaded map this session
//
// Persistence: UserData/Koren/PlayCount.json (a flat map_key -> PlayData
// dictionary). Marked dirty on every meaningful update; flushed on Dispose
// and via the IRuntimeService lifecycle.
//
// Patches gate on MainCore.IsModEnabled per the project convention. Best is
// also observed every frame via ObserveProgress() called from GameStats so
// near-miss runs ending in a wipe-to-black still capture the high-water mark.
public sealed class PlayCount : IRuntimeService, IRuntimeTick {
    private static readonly Dictionary<string, PlayData> playDatas = new();
    private static string currentMapKey = "";
    private static int sessionAttempts;
    private static float bestObservedThisRun;
    private static bool dirty;

    public void Initialize() => Load();
    public void Dispose() => Save();

    // Per-frame: observe live progress so the Best line tracks the in-run
    // high-water mark, not just the value at death/clear. Gated on InGame
    // and IsModEnabled so non-play scenes don't drift the counter.
    public void Tick() {
        if(!MainCore.IsModEnabled) {
            return;
        }

        if(!Status.GameStats.InGame) {
            return;
        }

        ObserveProgress(Status.GameStats.Progress);
    }

    public static PlayData For(string key) {
        if(!playDatas.TryGetValue(key, out PlayData d)) {
            d = new PlayData();
            playDatas[key] = d;
        }
        return d;
    }

    public static int SessionAttempts => sessionAttempts;

    public static int TotalAttemptsForCurrentMap() {
        if(string.IsNullOrEmpty(currentMapKey)) {
            return 0;
        }

        return playDatas.TryGetValue(currentMapKey, out PlayData d) ? d.TotalAttempts : 0;
    }

    public static float BestForCurrentMap() {
        if(string.IsNullOrEmpty(currentMapKey)) {
            return 0f;
        }

        float stored = playDatas.TryGetValue(currentMapKey, out PlayData d) ? d.BestProgress : 0f;
        return Mathf.Max(stored, bestObservedThisRun);
    }

    // Called from the HUD per frame so the live in-run high-water mark is
    // visible on the Best line even before a death/clear writes it back.
    public static void ObserveProgress(float progress) {
        if(float.IsNaN(progress) || float.IsInfinity(progress)) {
            return;
        }

        progress = Mathf.Clamp01(progress);
        if(progress > bestObservedThisRun) {
            bestObservedThisRun = progress;
        }
    }

    private static void OnRunStart() {
        string key = ComputeMapKey();
        if(key != currentMapKey) {
            currentMapKey = key;
            sessionAttempts = 0;
        }

        sessionAttempts++;
        bestObservedThisRun = 0f;

        PlayData d = For(key);
        d.TotalAttempts++;
        dirty = true;
    }

    private static void OnRunDeath() {
        if(string.IsNullOrEmpty(currentMapKey)) {
            return;
        }

        float progress = CurrentProgress();
        if(progress > bestObservedThisRun) {
            bestObservedThisRun = progress;
        }

        PlayData d = For(currentMapKey);
        if(bestObservedThisRun > d.BestProgress) {
            d.BestProgress = bestObservedThisRun;
            dirty = true;
        }

        FlushIfDirty();
    }

    private static void OnRunClear() {
        if(string.IsNullOrEmpty(currentMapKey)) {
            return;
        }

        PlayData d = For(currentMapKey);
        if(1f > d.BestProgress) {
            d.BestProgress = 1f;
            dirty = true;
        }

        bestObservedThisRun = 1f;
        FlushIfDirty();
    }

    private static float CurrentProgress() {
        try {
            scrController c = scrController.instance;
            return c != null ? Mathf.Clamp01(c.percentComplete) : 0f;
        } catch {
            return 0f;
        }
    }

    // Map identity. Tries (in order):
    //   1. ADOBase.currentLevel (official-level path / name)
    //   2. MD5 of scrLevelMaker's level-data string (old format) or
    //      a stable fingerprint of floor angles (new format)
    // Falls back to "unknown" so multiple unknown maps share one bucket
    // rather than crashing.
    private static string ComputeMapKey() {
        try {
            string official = ADOBase.currentLevel;
            if(!string.IsNullOrEmpty(official)) {
                return "official:" + official;
            }
        } catch {
        }

        try {
            scrLevelMaker lm = scrLevelMaker.instance;
            if(lm != null) {
                if(lm.isOldLevel && !string.IsNullOrEmpty(lm.leveldata)) {
                    return "old:" + Md5(lm.leveldata);
                }

                if(lm.floorAngles != null) {
                    var arr = System.Linq.Enumerable.ToArray(lm.floorAngles);
                    StringBuilder sb = new();
                    sb.Append("angles:").Append(arr.Length);
                    int step = Mathf.Max(1, arr.Length / 32);
                    for(int i = 0; i < arr.Length; i += step) {
                        sb.Append(':').Append(arr[i].ToString("0.###", CultureInfo.InvariantCulture));
                    }
                    return "new:" + Md5(sb.ToString());
                }
            }
        } catch {
        }

        return "unknown";
    }

    private static string Md5(string s) {
        using MD5 md5 = MD5.Create();
        byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
        StringBuilder sb = new(32);
        for(int i = 0; i < bytes.Length; i++) {
            sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string FilePath => Path.Combine(MainCore.Paths.RootPath, "PlayCount.json");

    private static void Load() {
        playDatas.Clear();
        currentMapKey = "";
        sessionAttempts = 0;
        bestObservedThisRun = 0f;
        dirty = false;

        try {
            string path = FilePath;
            if(!File.Exists(path)) {
                return;
            }

            string raw = File.ReadAllText(path);
            JObject root = JObject.Parse(raw);
            JObject maps = root["maps"] as JObject;
            if(maps == null) {
                return;
            }

            foreach(KeyValuePair<string, JToken> kv in maps) {
                playDatas[kv.Key] = PlayData.Deserialize(kv.Value);
            }
        } catch(Exception e) {
            MainCore.Log.Wrn("PlayCount load failed: " + e.Message);
        }
    }

    private static void FlushIfDirty() {
        if(dirty) {
            Save();
        }
    }

    public static void Save() {
        try {
            JObject maps = new();
            foreach(KeyValuePair<string, PlayData> kv in playDatas) {
                maps[kv.Key] = kv.Value.Serialize();
            }

            JObject root = new() {
                ["maps"] = maps,
            };

            File.WriteAllText(FilePath, root.ToString(Formatting.Indented));
            dirty = false;
        } catch(Exception e) {
            MainCore.Log.Err("PlayCount save failed: " + e.Message);
        }
    }

    [HarmonyPatch(typeof(scnGame), "Play")]
    private static class ScnGamePlayPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }
            OnRunStart();
        }
    }

    // scrController flips state through StateBehaviour.ChangeState(Enum).
    // Fail2 = death, Won = level cleared. Both are scrController.States values;
    // the `is States` check filters out other state machines that happen to
    // pump through the same base call.
    [HarmonyPatch(typeof(StateBehaviour), "ChangeState", new[] { typeof(Enum) })]
    private static class StateChangePatch {
        private static void Postfix(Enum newState) {
            if(!MainCore.IsModEnabled) {
                return;
            }

            if(newState is not States state) {
                return;
            }

            if(state == States.Fail2) {
                OnRunDeath();
            } else if(state == States.Won) {
                OnRunClear();
            }
        }
    }
}
