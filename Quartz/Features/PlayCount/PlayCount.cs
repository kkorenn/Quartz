using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Quartz.Async;
using Quartz.Compat.Interface;
using Quartz.Core;
using Quartz.IO;
using MonsterLove.StateMachine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Quartz.Features.PlayCount;

// Simplified port of the original KorenResourcePack PlayCount.
// Tracks two things per map:
//   • TotalAttempts — lifetime starts on this map, persisted
//   • BestProgress  — highest percentComplete ever reached, persisted
// And one in-memory counter:
//   • SessionAttempts — starts of the currently-loaded map this session
//
// Persistence: UserData/Quartz/PlayCount.json (a flat map_key -> PlayData
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
    // A "fail" here = a Miss or Overload hit, which is what ADOFAI counts as a
    // death. Set the instant the first fail lands (even under No Fail, where the
    // run keeps going) so Best stops advancing — Best = furthest clean progress.
    private static bool runHadFail;
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

    // Where the displayed best run began (0 = first tile). While the live run
    // is beating the stored best, that's the live run's start; otherwise the
    // start recorded with the stored best.
    public static float BestStartForCurrentMap() {
        if(string.IsNullOrEmpty(currentMapKey)) {
            return 0f;
        }

        playDatas.TryGetValue(currentMapKey, out PlayData d);
        float stored = d?.BestProgress ?? 0f;
        if(bestObservedThisRun > stored) {
            return CurrentRunStart();
        }
        return d?.BestStartProgress ?? 0f;
    }

    private static float CurrentRunStart() {
        try {
            return Status.ProgressTracker.RunStartedFromFirstTile
                ? 0f
                : Mathf.Clamp01(Status.ProgressTracker.RunStartProgress);
        } catch {
            return 0f;
        }
    }

    // Called from the HUD per frame so the live in-run high-water mark is
    // visible on the Best line even before a death/clear writes it back.
    public static void ObserveProgress(float progress) {
        // Once the run has failed, the clean high-water mark is frozen: no
        // progress past the first Miss/Overload counts toward Best.
        if(runHadFail) {
            return;
        }

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
        runHadFail = false;

        PlayData d = For(key);
        d.TotalAttempts++;
        dirty = true;
    }

    private static void OnRunDeath() {
        if(string.IsNullOrEmpty(currentMapKey)) {
            return;
        }

        // If the run never registered a Miss/Overload (e.g. a hold/timeout
        // fail), everything up to here was clean, so capture the death point.
        // If it did fail, bestObservedThisRun is already frozen at the first
        // fail — don't let the death point push it higher.
        if(!runHadFail) {
            float progress = CurrentProgress();
            if(progress > bestObservedThisRun) {
                bestObservedThisRun = progress;
            }
        }

        PlayData d = For(currentMapKey);
        if(bestObservedThisRun > d.BestProgress) {
            d.BestProgress = bestObservedThisRun;
            d.BestStartProgress = CurrentRunStart();
            dirty = true;
        }

        FlushIfDirty();
    }

    private static void OnRunClear() {
        if(string.IsNullOrEmpty(currentMapKey)) {
            return;
        }

        PlayData d = For(currentMapKey);
        if(!runHadFail) {
            // Clean clear (no Miss/Overload all run, No Fail on or off) — the
            // only way Best reaches 100%.
            if(1f > d.BestProgress) {
                d.BestProgress = 1f;
                d.BestStartProgress = CurrentRunStart();
                dirty = true;
            }
            bestObservedThisRun = 1f;
        } else {
            // Reached the end but failed along the way (e.g. No Fail carried the
            // run past a Miss). Persist only the frozen clean prefix, never 100%.
            if(bestObservedThisRun > d.BestProgress) {
                d.BestProgress = bestObservedThisRun;
                d.BestStartProgress = CurrentRunStart();
                dirty = true;
            }
        }

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
    //   1. Official campaign levels  -> ADOBase.currentLevel (the unique
    //      internal id like "1-1" / "12-X").
    //   2. Everything else (custom)  -> SHA-256 of the .adofai file's bytes.
    //   3. File unreadable           -> SHA-256 of resident level data.
    // Falls back to "unknown" so multiple unknown maps share one bucket
    // rather than crashing.
    //
    // Why the discriminator is isOfficialLevel and NOT isCLSLevel: custom levels
    // are played in two ways — CLS level-select (isCLSLevel true) AND the in-game
    // editor / editor-driven mods like TUFHelper (isLevelEditor true, isCLSLevel
    // FALSE). In BOTH, scrController.levelName collapses to the constant scene
    // name ("scnGame"), so keying on it merges every custom level's attempts into
    // one bucket — the bug. isOfficialLevel is the only flag that cleanly splits
    // "real campaign level" (scene-name is a valid id) from "custom level in any
    // context" (must hash the file). levelPath is populated + unique in all the
    // custom cases, including the editor.
    private static string ComputeMapKey() {
        bool isOfficial;
        try {
            isOfficial = ADOBase.isOfficialLevel;
        } catch {
            isOfficial = false;
        }

        // Official campaign levels: scene name is a genuine unique per-level id.
        if(isOfficial) {
            try {
                string official = ADOBase.currentLevel;
                if(!string.IsNullOrEmpty(official)) {
                    return "official:" + official;
                }
            } catch {
            }
        }

        // Custom levels (CLS select, editor, TUFHelper, …): hash the actual file
        // content. Unique per level, stable across replays, edit-sensitive.
        // Hashed at most once per file version (cached by path+mtime+size) on a
        // worker thread, so starting a large custom level cannot block a frame.
        string fileMapKey = TryGetLevelFileMapKey();
        if(!string.IsNullOrEmpty(fileMapKey)) {
            return fileMapKey;
        }

        // File couldn't be read — hash whatever level data is resident in memory.
        try {
            scrLevelMaker lm = scrLevelMaker.instance;
            if(lm != null) {
                if(lm.isOldLevel && !string.IsNullOrEmpty(lm.leveldata)) {
                    return "old:" + Sha256(lm.leveldata);
                }

                if(lm.floorAngles != null) {
                    // Hash the FULL angle list. The previous code sampled only
                    // ~32 angles on a stride, which collided distinct levels that
                    // happened to share those samples — a second merge source.
                    float[] arr = System.Linq.Enumerable.ToArray(lm.floorAngles);
                    StringBuilder sb = new();
                    sb.Append("angles:").Append(arr.Length);
                    for(int i = 0; i < arr.Length; i++) {
                        sb.Append(':').Append(arr[i].ToString("0.###", CultureInfo.InvariantCulture));
                    }
                    return "new:" + Sha256(sb.ToString());
                }
            }
        } catch {
        }

        return "unknown";
    }

    private static readonly object hashGate = new();
    private static readonly Dictionary<string, string> fileHashCache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> hashesInFlight = new(StringComparer.Ordinal);

    // SHA-256 of the loaded .adofai file. Cached by path+mtime+size so repeated
    // retries read the disk once; a content edit invalidates and re-hashes.
    private static string TryGetLevelFileMapKey() {
        try {
            string path = ADOBase.levelPath;
            if(string.IsNullOrEmpty(path) || !File.Exists(path)) {
                return null;
            }

            FileInfo info = new(path);
            string cacheKey = path + "|"
                + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|"
                + info.Length.ToString(CultureInfo.InvariantCulture);
            lock(hashGate) {
                if(fileHashCache.TryGetValue(cacheKey, out string cachedHash)) {
                    return "custom:" + cachedHash;
                }
            }

            // Use a stable temporary key immediately, then hash the file off-thread
            // and migrate its counter entry on the main thread when ready.
            string pendingKey = "pending:" + Sha256(cacheKey);
            bool start;
            lock(hashGate) {
                start = hashesInFlight.Add(cacheKey);
            }
            if(start) {
                _ = Task.Run(() => HashLevelFile(path, cacheKey, pendingKey));
            }
            return pendingKey;
        } catch {
            return null;
        }
    }

    private static void HashLevelFile(string path, string cacheKey, string pendingKey) {
        string hash = null;
        Exception error = null;
        try {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using SHA256 sha = SHA256.Create();
            hash = Hex(sha.ComputeHash(stream));
        } catch(Exception e) {
            error = e;
        }

        MainThread.Enqueue(() => {
            lock(hashGate) {
                hashesInFlight.Remove(cacheKey);
                if(hash != null) {
                    fileHashCache[cacheKey] = hash;
                }
            }

            if(hash == null) {
                MainCore.Log.Wrn("PlayCount map hash failed: " + error?.Message);
                return;
            }

            MigratePendingMapKey(pendingKey, "custom:" + hash);
        });
    }

    private static void MigratePendingMapKey(string pendingKey, string finalKey) {
        if(pendingKey == finalKey) {
            return;
        }

        if(playDatas.TryGetValue(pendingKey, out PlayData pending)) {
            PlayData final = For(finalKey);
            final.TotalAttempts += pending.TotalAttempts;
            if(pending.BestProgress > final.BestProgress) {
                final.BestProgress = pending.BestProgress;
                final.BestStartProgress = pending.BestStartProgress;
            }
            playDatas.Remove(pendingKey);
            dirty = true;
        }

        if(currentMapKey == pendingKey) {
            currentMapKey = finalKey;
        }
    }

    private static string Sha256(string s) => Sha256(Encoding.UTF8.GetBytes(s));

    private static string Sha256(byte[] bytes) {
        using SHA256 sha = SHA256.Create();
        return Hex(sha.ComputeHash(bytes));
    }

    private static string Hex(byte[] hash) {
        StringBuilder sb = new(hash.Length * 2);
        for(int i = 0; i < hash.Length; i++) {
            sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string FilePath => Path.Combine(MainCore.Paths.RootPath, "PlayCount.json");

    private static void Load() {
        playDatas.Clear();
        currentMapKey = "";
        sessionAttempts = 0;
        bestObservedThisRun = 0f;
        runHadFail = false;
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

            AtomicFile.WriteAllText(FilePath, root.ToString(Formatting.Indented));
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

    // A Miss/Overload is a "death" in ADOFAI's bookkeeping. Watch every hit so
    // the first fail freezes Best. This fires even under No Fail — where the run
    // continues and scrController.deaths never increments — so a No-Fail run
    // that racks up misses can't inflate Best past where it stopped being clean.
    // Restriction patches the same method; multiple Harmony postfixes coexist.
    [HarmonyPatch(typeof(scrMarginTracker), "AddHit", typeof(HitMargin))]
    private static class AddHitPatch {
        private static void Postfix(HitMargin hit) {
            if(!MainCore.IsModEnabled) {
                return;
            }

            if(runHadFail) {
                return;
            }

            if(hit == HitMargin.FailMiss || hit == HitMargin.FailOverload) {
                // Capture progress up to the failing tile before freezing, so a
                // clean run that dies at 80% still records ~80%.
                ObserveProgress(CurrentProgress());
                runHadFail = true;
            }
        }
    }
}
