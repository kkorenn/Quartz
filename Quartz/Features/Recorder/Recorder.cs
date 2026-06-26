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
// Flow: pressing Render fully reloads the current level from disk and immediately
// starts capturing from frame zero — no separate "arm then play" step. A normal
// level reloads via ADOBase.RestartScene (a clean SceneManager reload that re-reads
// the level from its file and auto-plays); an editor level reloads scnEditor from
// the saved file and then playtests from the start (see RecorderReload). Either way
// scnGame.Play (see RecorderPatches) starts the session; the engine is pinned to a
// fixed frame rate (Time.captureFramerate) and the audio mix is rendered offline
// (AudioRenderer), so every frame is captured regardless of how long it takes to
// draw — heavy levels render slower than real time but never skip a frame. While
// recording the level is locked (pause/exit blocked) and a white progress overlay
// covers the screen; the capture itself re-renders the game cameras into an
// off-screen texture, so the overlay is never in the video. The run finalizes when
// the player lands on the end portal. Pressed from a menu (no level loaded) it falls
// back to arming, so the next level played starts the render.
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

    // True while a press-Render reload is in flight (the level is reloading and will
    // start capturing on its own) — distinct from being armed from a menu, where we're
    // genuinely waiting for the player to start a level. Drives the status line wording.
    public static bool Reloading { get; internal set; }

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

    // Button entry point: start (reload + render) if idle, disarm if armed, cancel if
    // recording.
    public static void Toggle() {
        switch(Current) {
            case State.Idle: Arm(); break;
            case State.Armed: Disarm(); break;
            case State.Recording: Cancel(); break;
        }
    }

    // Press Render: arm, then fully reload the current level from disk and start the
    // capture from frame zero. The reload path depends on context:
    //   normal play -> ADOBase.RestartScene() reloads the scene from the level file and
    //                  auto-plays; the scnGame.Play patch fires BeginSession.
    //   editor      -> reload scnEditor from the saved file, then playtest from the
    //                  start once it has settled (RecorderReload). Needs a saved file —
    //                  an unsaved level has nothing on disk to reload.
    //   no level    -> stay armed; the next level played starts the render.
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

        try {
            var ed = ADOBase.isLevelEditor ? ADOBase.editor : null;
            if(ed != null) {
                string levelPath = null;
                try { levelPath = ed.customLevel?.levelPath; } catch { }
                if(string.IsNullOrEmpty(levelPath)) {
                    // Nothing on disk to reload — a full reload would open a blank editor.
                    Error = "save the level before rendering from the editor";
                    Current = State.Idle;
                    MainCore.Log.Wrn($"[Recorder] {Error}");
                    return;
                }
                // Full reload from disk (RestartScene re-sets scnEditor.levelToOpenOnLoad),
                // then playtest from the start once the editor has reloaded.
                Reloading = true;
                MainCore.Log.Msg("[Recorder] editor render: full reload from disk, then capture");
                ADOBase.RestartScene();
                RecorderReload.PlayEditorFromStartWhenReady();
            } else if(ADOBase.isScnGame && ADOBase.controller != null) {
                // Full scene reload: re-reads the level from its file and auto-plays from
                // frame zero, so the captured run is pristine. scnGame.Play -> BeginSession.
                Reloading = true;
                MainCore.Log.Msg("[Recorder] level render: full reload, then capture");
                ADOBase.RestartScene();
            } else {
                // Pressed from a menu / level select — no level to reload yet.
                Reloading = false;
                MainCore.Log.Msg("[Recorder] armed — play a level to start rendering");
            }
        } catch(Exception e) {
            // Reload failed but we're still armed; the next Play will start the render.
            Reloading = false;
            MainCore.Log.Wrn($"[Recorder] reload-on-start failed, staying armed: {e.Message}");
        }
    }

    public static void Disarm() {
        if(Current == State.Armed) {
            Current = State.Idle;
            Reloading = false;
            MainCore.Log.Msg("[Recorder] disarmed");
        }
    }

    // Called by the scnGame.Play patch when a level starts while armed.
    internal static void BeginSession() {
        if(Current != State.Armed) {
            return;
        }
        Reloading = false;   // the level has loaded and capture is starting
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
        ClearPrepass();
        Current = State.Idle;
        Reloading = false;
    }

    // Hard stop on mod disable / teardown.
    public static void Restore() {
        if(session != null) {
            session.Abort();
            Object.Destroy(session.gameObject);
            session = null;
        }
        ClearPrepass();
        RecorderOverlay.Hide();
        Current = State.Idle;
        Reloading = false;
    }

    internal static void OnSessionEnded() => session = null;

    // --- two-pass live audio (macOS) ----------------------------------------
    // Buffer captured by the realtime audio pass; the video pass reads it as its audio
    // source, then clears it. Non-null only between the two passes.
    internal static float[] PrepassAudio;
    internal static int PrepassRate;
    internal static int PrepassChannels;
    internal static double PrepassStartSongPos;

    internal static void ClearPrepass() {
        PrepassAudio = null;
        PrepassRate = 0;
        PrepassChannels = 0;
        PrepassStartSongPos = 0;
    }

    // Called by the audio-pass session when it finishes capturing. Stores the buffer,
    // re-arms, and restarts the level — the restart's scnGame.Play fires BeginSession
    // again, which (seeing PrepassAudio set) runs the offline VIDEO pass and muxes it.
    internal static void OnAudioPrepassComplete(float[] pcm, int rate, int channels, double startSongPos) {
        PrepassAudio = pcm;
        PrepassRate = rate;
        PrepassChannels = channels;
        PrepassStartSongPos = startSongPos;
        session = null;
        Current = State.Armed;
        Reloading = true;   // video pass is replaying the level, not waiting for the player
        // Replaying for the video pass is context-sensitive: from the editor we must
        // re-enter play via scnEditor.Play() — controller.Restart there drops back to the
        // blank edit view, so scnGame.Play never fires and the video pass never starts.
        // The handoff helper picks the right path and defers until the level has settled.
        RecorderHandoff.BeginVideoPass();
    }
}
