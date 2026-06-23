using ADOFAI;
using Quartz.Core;
using Quartz.Features.Recorder.Native;
using Quartz.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Quartz.Features.Recorder;

// FFmpeg renderer (Editor tab). Offline, deterministic, frame-by-frame capture
// bound to a level run.
//
// Flow: the player arms the renderer from the menu, then plays a level. scnGame
// .Play (see RecorderPatches) starts the session; the engine is pinned to a
// fixed frame rate (Time.captureFramerate) and the audio mix is rendered offline
// (AudioRenderer), so every frame is captured regardless of how long it takes to
// draw — heavy levels render slower than real time but never skip a frame. While
// recording the level is locked (pause/exit blocked) and a white progress overlay
// covers the screen; the capture itself re-renders the game cameras into an
// off-screen texture, so the overlay is never in the video. The run finalizes when
// the player lands on the end portal.
public static class Recorder {
    public static SettingsFile<RecorderSettings> ConfMgr { get; private set; }
    public static RecorderSettings Conf => ConfMgr?.Data;

    public enum State { Idle, Armed, Recording, Finalizing }

    public static State Current { get; internal set; } = State.Idle;
    public static int FramesWritten { get; internal set; }
    public static int TotalFrames { get; internal set; }   // 0 = unknown
    public static string OutputPath { get; internal set; }
    public static string Error { get; internal set; }

    // The render owns the conductor's clock. scrConductor.Update is transpiled
    // (RecorderPatches) so every AudioSettings.dspTime read goes through
    // ControlledDspTime instead; while DrivingClock is true that returns
    // ControlledDsp, which the session steps by exactly 1/fps each captured frame.
    // The conductor then advances song position, beat events AND tile progression
    // one frame at a time — deterministic, and immune to the audio clock racing
    // ahead on a slow frame or stalling on a pause. When not rendering it returns
    // the real value, so normal play is byte-for-byte unchanged.
    public static double ControlledDsp;
    public static bool DrivingClock;

    public static double ControlledDspTime() =>
        DrivingClock ? ControlledDsp : UnityEngine.AudioSettings.dspTime;

    // Current song position (seconds) of the frame being captured — drives audio
    // indexing and the end-of-render check.
    public static double RenderClock;

    // Wall-clock processing rate (frames encoded per real second) — the "how fast
    // is it grinding" number, distinct from the output frame rate.
    public static double RenderFps;

    public static bool IsArmed => Current == State.Armed;
    public static bool IsRecording => Current is State.Recording or State.Finalizing;

    // While true, the pause/exit patch swallows TogglePauseGame so the run can't
    // be interrupted mid-render.
    public static bool Locked => Current == State.Recording;

    public static float Progress =>
        TotalFrames > 0 ? Mathf.Clamp01(FramesWritten / (float)TotalFrames) : 0f;

    private static RecorderSession session;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }
        ConfMgr = new SettingsFile<RecorderSettings>(
            Path.Combine(MainCore.Paths.RootPath, "Recorder.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    public static string NativeLibraryPath => Path.Combine(
        MainCore.Paths.RootPath, "native",
        QuartzNativeLibrary.PlatformDir, QuartzNativeLibrary.LibraryFileName);

    public static bool EnsureNative() => NativeEncoder.Initialize(NativeLibraryPath);
    public static bool NativeReady => NativeEncoder.Available;
    public static string NativeError => NativeEncoder.LoadError;
    public static string NativeVersion => NativeEncoder.Version;

    public static string ResolveOutputDirectory() {
        string dir = Conf?.OutputDirectory;
        if(string.IsNullOrWhiteSpace(dir)) {
            dir = Path.Combine(MainCore.Paths.RootPath, "Renders");
        }
        return dir;
    }

    // Button entry point: arm if idle, disarm if armed, cancel if recording.
    public static void Toggle() {
        switch(Current) {
            case State.Idle: Arm(); break;
            case State.Armed: Disarm(); break;
            case State.Recording: Cancel(); break;
        }
    }

    // Arm the renderer; the next level Play begins capture. If a level is already
    // playing, restart it so the capture starts from a clean frame zero.
    public static void Arm() {
        if(Current != State.Idle) {
            return;
        }
        EnsureConf();
        Error = null;
        OutputPath = null;

        if(!EnsureNative()) {
            Error = NativeError ?? "native encoder unavailable";
            MainCore.Log.Wrn($"[Recorder] cannot arm: {Error}");
            return;
        }

        Current = State.Armed;
        MainCore.Log.Msg("[Recorder] armed — play a level to start rendering");

        // Already in a level? Restart it so capture begins from the top.
        try {
            if(ADOBase.controller != null && ADOBase.isScnGame && !ADOBase.isLevelEditor) {
                ADOBase.controller.Restart(false);
            }
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] restart-on-arm skipped: {e.Message}");
        }
    }

    public static void Disarm() {
        if(Current == State.Armed) {
            Current = State.Idle;
            MainCore.Log.Msg("[Recorder] disarmed");
        }
    }

    // Called by the scnGame.Play patch when a level starts while armed.
    internal static void BeginSession() {
        if(Current != State.Armed) {
            return;
        }
        if(session == null) {
            GameObject go = new("QuartzRecorderSession");
            go.transform.SetParent(MainCore.Root.transform, false);
            session = go.AddComponent<RecorderSession>();
        }
        session.Begin();
    }

    // Called by the completion patch (landed on the end portal / Won). Don't stop
    // dead on the portal — extend a few seconds so the outro/clear animation is
    // captured.
    internal static void NotifyComplete() {
        if(Current == State.Recording) {
            session?.ExtendToEnding();
        }
    }

    // Called by the fail patch — a render that dies mid-run is saved as-is so the
    // player isn't left stuck in a locked level.
    internal static void NotifyFailed() {
        if(Current == State.Recording) {
            session?.RequestFinalize();
        }
    }

    // User-initiated abort (Esc during render): stop now, no finalize.
    public static void Cancel() {
        if(IsRecording && session != null) {
            session.Abort();
            Object.Destroy(session.gameObject);
            session = null;
        }
        Current = State.Idle;
    }

    // Hard stop on mod disable / teardown.
    public static void Restore() {
        if(session != null) {
            session.Abort();
            Object.Destroy(session.gameObject);
            session = null;
        }
        RecorderOverlay.Hide();
        Current = State.Idle;
    }

    internal static void OnSessionEnded() => session = null;
}
