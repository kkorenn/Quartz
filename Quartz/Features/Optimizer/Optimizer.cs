using System.Diagnostics;
using Quartz.Compat.Interface;
using Quartz.Core;
using Quartz.Features.Status;
using Quartz.IO;
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

    // One forced collect if the deferred heap grows past this much over its size
    // at the start of the run. A single hitch on a marathon level beats an OOM.
    private const long GCSafetyBytes = 768L * 1024 * 1024;

    private static bool defaultsCaptured;
    private static bool defaultRunInBackground;
    private static ProcessPriorityClass defaultPriority = ProcessPriorityClass.Normal;

    // True while the GC has been put into Manual mode for an active run.
    private static bool gcDeferred;
    private static long heapAtDefer;

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
    }

    // Restores every engine default. Called when the mod is disabled.
    public static void Restore() {
        if(gcDeferred) {
            ResumeGC();
        }
        Application.runInBackground = defaultRunInBackground;
        SetPriority(defaultPriority);
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

        // While deferred, bound heap growth so a very long level can't run the
        // process out of memory (GetTotalMemory(false) doesn't force a collect).
        if(gcDeferred && GC.GetTotalMemory(false) - heapAtDefer > GCSafetyBytes) {
            GC.Collect();
            heapAtDefer = GC.GetTotalMemory(false);
        }
    }

    private static void DeferGC() {
        try {
            GarbageCollector.GCMode = GarbageCollector.Mode.Manual;
            gcDeferred = true;
            heapAtDefer = GC.GetTotalMemory(false);
        } catch {
            // GC mode control unavailable — leave automatic collection on.
            gcDeferred = false;
        }
    }

    private static void ResumeGC() {
        try {
            GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
        } catch {
        }
        gcDeferred = false;
        GC.Collect();
    }

    private sealed class TickImpl : IRuntimeTick {
        public void Tick() => Optimizer.Tick();
    }
}
