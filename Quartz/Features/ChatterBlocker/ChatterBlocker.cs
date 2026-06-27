using HarmonyLib;
using Quartz.Core;
using Quartz.IO;
using SkyHook;
using UnityEngine;

namespace Quartz.Features.ChatterBlocker;

// Keyboard Chatter Blocker, ported from the original KorenResourcePack
// (which follows fangshenghan's KeyboardChatterBlocker ADOFAI mod: per-key
// last-press timestamps, a single configurable ms threshold, and both the
// sync and async input paths filtered).
//
// A chattering switch re-fires within a few ms of the real press; any repeat
// of the same key inside the threshold is dropped. Repeats <= 5 ms apart pass
// through — that's the same key event reported twice by the engine, not
// chatter (same exemption the reference mod uses).
//
// The CountValidKeysPressed prefix is the shared funnel for this feature AND
// the Key Limiter, exactly like v1: it re-counts the frame's pressed keys,
// skipping limiter-blocked keys and chatter repeats, while keeping the game's
// key-frequency stats bookkeeping intact. The SkyHook prefix covers the async
// input path the same way.
public static class ChatterBlocker {
    public static SettingsFile<ChatterBlockerSettings> ConfMgr { get; private set; }
    public static ChatterBlockerSettings Conf => ConfMgr?.Data;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<ChatterBlockerSettings>(
            Path.Combine(MainCore.Paths.RootPath, "ChatterBlocker.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    private static bool IsActive() {
        EnsureConf();
        return MainCore.IsModEnabled && Conf.Enabled;
    }

    private static bool HasAnyFilter() => IsActive() || KeyLimiter.KeyLimiter.IsEnabled();

    private static long ThresholdMs() => Math.Max(0L, (long)Math.Round(Conf?.ThresholdMs ?? 0f));

    private static long NowMs() => DateTimeOffset.Now.ToUnixTimeMilliseconds();

    private static readonly Dictionary<KeyCode, long> lastKeyPress = [];
    private static readonly Dictionary<ushort, long> lastAsyncKeyPress = [];
    private static readonly HashSet<KeyCode> reportedKeysThisFrame = [];
    private static readonly HashSet<KeyCode> injectedKeyHeldPrev = [];

    // Per-blocked-key diagnostic logging. Off by default: the $"..." interpolation
    // plus a synchronous console write on every blocked key is measurable on macOS,
    // and chatter fires bursts of blocks. Flip to true only when debugging.
    private static readonly bool DebugLog = false;

    private static bool AcceptNormalKey(KeyCode key, long now, long thresholdMs, bool active) {
        if(!active) {
            return true;
        }

        if(!lastKeyPress.TryGetValue(key, out long last)) {
            lastKeyPress[key] = now;
            return true;
        }

        long elapsed = now - last;
        if(elapsed > thresholdMs || elapsed <= 5L) {
            lastKeyPress[key] = now;
            return true;
        }

        if(DebugLog) {
            MainCore.Log.Msg($"[ChatterBlocker] Blocked Key: {key} time: {elapsed}ms.");
        }
        return false;
    }

    private static void RecordKeyStats(scrController controller, object key) {
        try {
            scrPlayer player = controller != null ? controller.playerOne : null;
            if(player == null || player.keyFrequency == null) {
                return;
            }
            player.keyFrequency[key] = player.keyFrequency.ContainsKey(key)
                ? player.keyFrequency[key] + 1
                : 1; // first press is one occurrence, not zero (kept keyTotal in step)
            player.keyTotal++;
        } catch {
        }
    }

    private static void ResetKeyLimiterOverCounter(scrController controller) {
        if(controller != null && controller.playerOne != null) {
            controller.playerOne.keyLimiterOverCounter = 0;
        }
    }

    private static int CountValidKeysPressed() {
        scrController controller = scrController.instance;
        if(controller == null) {
            return 0;
        }
        ResetKeyLimiterOverCounter(controller);

        bool chatterActive = IsActive();
        long now = NowMs();
        long threshold = ThresholdMs();
        int count = 0;

        reportedKeysThisFrame.Clear();
        foreach(AnyKeyCode mainPressKey in RDInput.GetMainPressKeys()) {
            object value = mainPressKey.value;
            if(value is KeyCode key) {
                reportedKeysThisFrame.Add(KeyLimiter.KeyLimiter.NormalizeKey(key));
                if(KeyLimiter.KeyLimiter.ShouldBlockKey(key)) {
                    continue;
                }

                RecordKeyStats(controller, key);
                if(AcceptNormalKey(key, now, threshold, chatterActive)) {
                    count++;
                }
            } else if(value is AsyncKeyCode asyncKey) {
                RecordKeyStats(controller, asyncKey);
                count++;
            }
        }

        count += CountKeysMissedByGame(controller, now, threshold, chatterActive);

        return count;
    }

    // macOS doesn't deliver an Input.GetKeyDown down-edge for modifier keys
    // (Left/Right Shift, etc.), so the game's main-key scan never counts them
    // as hits even though their held state is readable. Re-create the missing
    // down-edge for allowed keys the game failed to report this frame. No-op
    // on Windows (the game already reports those keys, so they land in
    // reportedKeysThisFrame and are skipped). Ported from v1; held detection
    // uses Input.GetKey since v2 has no KeyViewer raw-hook state.
    private static int CountKeysMissedByGame(scrController controller, long now, long threshold, bool chatterActive) {
        if(!KeyLimiter.KeyLimiter.IsActive() || !KeyLimiter.KeyLimiter.InPlayerControl()) {
            injectedKeyHeldPrev.Clear();
            return 0;
        }

        int[] allowed = KeyLimiter.KeyLimiter.Conf?.AllowedKeys;
        if(allowed == null || allowed.Length == 0) {
            injectedKeyHeldPrev.Clear();
            return 0;
        }

        int injected = 0;
        for(int i = 0; i < allowed.Length; i++) {
            KeyCode key = KeyLimiter.KeyLimiter.NormalizeKey((KeyCode)allowed[i]);
            if(key == KeyCode.None || KeyLimiter.KeyLimiter.IsMouseKey(key)) {
                continue;
            }

            // Game already reported it this frame: count handled above. Mark
            // it held so a lagging held-state read can't re-fire the edge
            // next frame.
            if(reportedKeysThisFrame.Contains(key)) {
                injectedKeyHeldPrev.Add(key);
                continue;
            }

            bool held;
            try { held = UnityEngine.Input.GetKey(key); }
            catch { continue; }

            if(held && !injectedKeyHeldPrev.Contains(key)) {
                RecordKeyStats(controller, key);
                if(AcceptNormalKey(key, now, threshold, chatterActive)) {
                    injected++;
                }
            }

            if(held) {
                injectedKeyHeldPrev.Add(key);
            } else {
                injectedKeyHeldPrev.Remove(key);
            }
        }

        return injected;
    }

    [HarmonyPatch(typeof(scrPlayer), "CountValidKeysPressed")]
    private static class CountValidKeysPressedPatch {
        private static bool Prefix(ref int __result) {
            if(!HasAnyFilter()) {
                return true;
            }

            __result = CountValidKeysPressed();
            return false;
        }
    }

    // Async input path. Runs on SkyHook's thread — only the volatile
    // player-control snapshot and plain dictionaries are touched here.
    [HarmonyPatch(typeof(SkyHookManager), "HookCallback")]
    private static class HookCallbackPatch {
        private static bool Prefix(SkyHookEvent __0) {
            // Runs inside SkyHook's native keyboard event-tap callback. ANY exception
            // here propagates into the tap and makes it SWALLOW the event — the
            // keyboard dies game-wide. (A SkyHook game update renamed AsyncKeyMapper
            // → TypeLoadException on every key → total keyboard loss.) Never throw:
            // on any failure, let the key through untouched.
            try {
                return PrefixCore(__0);
            } catch {
                return true;
            }
        }

        private static bool PrefixCore(SkyHookEvent __0) {
            SkyHookEvent ev = __0;

            if(KeyLimiter.KeyLimiter.IsMouseLabel(ev.Label)) {
                return true;
            }

            // Forward every key edge to the viewer's hook-held tracker before the
            // KeyReleased early-out below. Unity's Input can't see Hangul/Hanja,
            // so the key viewer relies on these edges to light RightAlt /
            // RightControl boxes (and clear them on release).
            KeyLimiter.KeyLimiter.NoteHookEvent(
                KeyLimiter.KeyLimiter.HookKeyToPhysicalUnityKey(ev.Key, ev.Label),
                ev.Type == SkyHook.EventType.KeyPressed);

            if(ev.Type == SkyHook.EventType.KeyReleased || ev.Key == 27) {
                return true;
            }

            if(KeyLimiter.KeyLimiter.ShouldBlockAsyncKeyFromHook(ev.Key, ev.Label)) {
                return false;
            }

            if(!IsActive()) {
                return true;
            }

            long now = NowMs();
            long threshold = ThresholdMs();
            if(!lastAsyncKeyPress.TryGetValue(ev.Key, out long last)) {
                last = 0L;
            }

            long elapsed = now - last;
            if(elapsed > threshold) {
                lastAsyncKeyPress[ev.Key] = now;
                return true;
            }

            if(DebugLog) {
                MainCore.Log.Msg($"[ChatterBlocker] Blocked Async Key: {ev.Label} time: {elapsed}ms.");
            }
            return false;
        }
    }
}
