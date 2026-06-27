using System.Diagnostics;
using Quartz.Compat.Interface;
using Quartz.Core;
using Quartz.Features.Status;
using Quartz.IO;
using Quartz.UI.Utility;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace Quartz.Features.Optimizer;

// Engine/runtime performance toggles. Unlike the Effect Remover (which strips
// visual events out of the chart) nothing here changes how a level looks — it
// tunes how the engine runs: GC scheduling, OS process priority, background
// execution. None of these are exposed by the game's own options. See
// OptimizerSettings for what each toggle does.
//
// Captured engine defaults are restored when a toggle (or the whole mod) turns
// off, so the feature never leaves global state changed behind it.
public static class Optimizer {
    public static SettingsFile<OptimizerSettings> ConfMgr { get; private set; }
    public static OptimizerSettings Conf => ConfMgr?.Data;

    // Per-frame hook (added to RuntimeTicks). Watches the in-game transition to
    // open/close the manual-GC window and runs the heap-growth safety valve.
    public static readonly IRuntimeTick Ticker = new TickImpl();

    // Cap how far the deferred (Manual-mode) heap may grow over its size at the
    // start of the run before a forced collect. Kept SMALL on purpose: a large
    // cap (this was 768 MB) let the heap balloon during play, and a collect of a
    // huge heap is a multi-100 ms stop-the-world freeze — plus the bloated heap
    // adds OS memory pressure — which is exactly what craters 1% lows. A tighter
    // cap trades that one catastrophic hitch for a few small, bounded collects.
    private const long GCSafetyBytes = 96L * 1024 * 1024;

    private static bool defaultsCaptured;
    private static bool defaultRunInBackground;
    private static ProcessPriorityClass defaultPriority = ProcessPriorityClass.Normal;

    // True while SmoothGC is smoothing collection for an active run.
    private static bool gcDeferred;
    private static long heapAtDefer;
    // True only when the deferral actually switched the GC to Manual mode (a
    // non-incremental build). On incremental-GC builds SmoothGC leaves the GC
    // enabled: incremental collection amortizes pauses across frames, which is
    // far better for 1% lows than a Manual defer + one giant catch-up collect.
    private static bool usingManualDefer;
    private static bool loggedGcStrategy;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }
        ConfMgr = new SettingsFile<OptimizerSettings>(
            Path.Combine(MainCore.Paths.RootPath, "Optimizer.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    private static bool Active {
        get {
            EnsureConf();
            return MainCore.IsModEnabled;
        }
    }

    public static void Initialize() {
        EnsureConf();
        CaptureDefaults();
        // Idempotent: a UnityModManager in-process reload runs Initialize again
        // on this static feature, so drop any prior subscription first to avoid
        // stacking duplicate per-scene handlers.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    private static void CaptureDefaults() {
        if(defaultsCaptured) {
            return;
        }
        defaultRunInBackground = Application.runInBackground;
        try {
            defaultPriority = Process.GetCurrentProcess().PriorityClass;
        } catch {
            defaultPriority = ProcessPriorityClass.Normal;
        }
        defaultsCaptured = true;
    }

    // Re-applies the non-per-frame toggles (background execution, process
    // priority) and reconciles GC mode. Called on init, on any toggle change,
    // and when the mod is enabled.
    public static void Apply() {
        EnsureConf();
        CaptureDefaults();
        bool on = MainCore.IsModEnabled;

        Application.runInBackground = on && Conf.RunInBackground
            ? true
            : defaultRunInBackground;

        SetPriority(on && Conf.BoostProcessPriority
            ? ProcessPriorityClass.AboveNormal
            : defaultPriority);

        // If Smooth GC was switched off (or the mod disabled) while a run still
        // had the GC deferred, hand scheduling back now.
        if(gcDeferred && !(on && Conf.SmoothGC && GameStats.InGame)) {
            ResumeGC();
        }

        // Global lightweight-shadow toggle for the overlay text shadows. Read on
        // each TMPTextShadow.Apply; existing labels pick it up on their next
        // refresh / scene rebuild. The offset scale is pushed first so a tuned value
        // is live before the next Apply reads it.
        TMPTextShadow.UnderlayOffsetScale = Conf.ShadowUnderlayOffsetScale;
        TMPTextShadow.UseMaterialUnderlay = on && Conf.LightTextShadows;
    }

    // Restores every engine default. Called when the mod is disabled.
    public static void Restore() {
        if(gcDeferred) {
            ResumeGC();
        }
        Application.runInBackground = defaultRunInBackground;
        SetPriority(defaultPriority);
    }

    internal static bool FastBloomActive {
        get {
            EnsureConf();
            return MainCore.IsModEnabled && Conf != null && Conf.FastBloom;
        }
    }

    internal static bool SkipNoOpScreenFiltersActive {
        get {
            EnsureConf();
            return MainCore.IsModEnabled && Conf != null && Conf.SkipNoOpScreenFilters;
        }
    }

    private static void SetPriority(ProcessPriorityClass priority) {
        try {
            Process proc = Process.GetCurrentProcess();
            if(proc.PriorityClass != priority) {
                proc.PriorityClass = priority;
            }
        } catch {
            // Not supported / not permitted on this platform — ignore.
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if(Active && Conf.CollectOnLevelLoad) {
            GC.Collect();
        }
    }

    // Drop the scene-load subscription on full mod unload (UMM in-process reload).
    // Not done in Restore(): the subscription is idempotent + inert when disabled,
    // and Initialize (which re-adds it) only runs at startup, not on re-enable.
    public static void Unhook() {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private static void Tick() {
        // Bracket the run with manual GC: entering gameplay defers collection so
        // none lands mid-run; leaving gameplay collects the run's garbage and
        // hands scheduling back to the runtime.
        bool wantDefer = Active && Conf.SmoothGC && GameStats.InGame;
        if(wantDefer != gcDeferred) {
            if(wantDefer) {
                DeferGC();
            } else {
                ResumeGC();
            }
            return;
        }

        // Only the Manual path lets the heap balloon; bound it so a very long
        // level can't run the process out of memory (and so the eventual collect
        // stays small). Incremental builds never defer, so this never fires there.
        if(gcDeferred && usingManualDefer
            && GC.GetTotalMemory(false) - heapAtDefer > GCSafetyBytes) {
            GC.Collect();
            heapAtDefer = GC.GetTotalMemory(false);
        }
    }

    private static void DeferGC() {
        try {
            if(!loggedGcStrategy) {
                loggedGcStrategy = true;
                MainCore.Log.Msg(GarbageCollector.isIncremental
                    ? "[Optimizer] SmoothGC: incremental GC present — leaving collection enabled (no Manual defer)."
                    : "[Optimizer] SmoothGC: no incremental GC — deferring via Manual mode (96MB heap cap).");
            }

            if(GarbageCollector.isIncremental) {
                // Incremental GC already amortizes collection across frames — far
                // better for 1% lows than Manual mode, which halts ALL collection,
                // balloons the heap, then dumps one multi-100ms stop-the-world
                // collect. Leave it enabled; don't defer.
                usingManualDefer = false;
                gcDeferred = true;
                return;
            }

            GarbageCollector.GCMode = GarbageCollector.Mode.Manual;
            usingManualDefer = true;
            gcDeferred = true;
            heapAtDefer = GC.GetTotalMemory(false);
        } catch {
            // GC mode control unavailable — leave automatic collection on.
            gcDeferred = false;
            usingManualDefer = false;
        }
    }

    private static void ResumeGC() {
        try {
            // Only restore + catch-up collect if we actually went Manual. The
            // incremental path never changed GC state, so leave it alone (and skip
            // the catch-up collect that would itself be a needless spike).
            if(usingManualDefer) {
                GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
                GC.Collect();
            }
        } catch {
        }
        usingManualDefer = false;
        gcDeferred = false;
    }

    private sealed class TickImpl : IRuntimeTick {
        public void Tick() => Optimizer.Tick();
    }
}
