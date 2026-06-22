using Koren.Core;
using Koren.Features.Recorder.Native;
using Koren.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Koren.Features.Recorder;

// FFmpeg renderer (Editor tab). Offline, deterministic capture: the session
// pins Time.captureFramerate so the engine advances by a fixed 1/fps step each
// frame regardless of how long the frame takes to draw — heavy-decoration levels
// render slower than real time but never drop a frame, so the output is smooth.
// Frames + the game audio mix are fed to the native libav encoder (NativeEncoder).
//
// Recording is a transient action, not a persisted toggle: settings persist, the
// active session does not. The page polls the static state below to drive its
// button/label.
public static class Recorder {
    public static SettingsFile<RecorderSettings> ConfMgr { get; private set; }
    public static RecorderSettings Conf => ConfMgr?.Data;

    public enum State { Idle, Recording, Finalizing }

    public static State Current { get; internal set; } = State.Idle;
    public static int FramesWritten { get; internal set; }
    public static string OutputPath { get; internal set; }
    public static string Error { get; internal set; }

    public static bool IsRecording => Current != State.Idle;
    public static float OutputSeconds {
        get {
            int fps = Conf?.Fps ?? 60;
            return fps > 0 ? FramesWritten / (float)fps : 0f;
        }
    }

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

    // Absolute path to the bundled native encoder library for this platform.
    public static string NativeLibraryPath => Path.Combine(
        MainCore.Paths.RootPath, "native",
        KorenNativeLibrary.PlatformDir, KorenNativeLibrary.LibraryFileName);

    public static bool EnsureNative() => NativeEncoder.Initialize(NativeLibraryPath);

    public static bool NativeReady => NativeEncoder.Available;
    public static string NativeError => NativeEncoder.LoadError;
    public static string NativeVersion => NativeEncoder.Version;

    // Folder renders are written to (created on demand).
    public static string ResolveOutputDirectory() {
        string dir = Conf?.OutputDirectory;
        if(string.IsNullOrWhiteSpace(dir)) {
            dir = Path.Combine(MainCore.Paths.RootPath, "Renders");
        }
        return dir;
    }

    // Start if idle, stop+save if recording. Returns the resulting recording state.
    public static bool Toggle() {
        if(IsRecording) {
            Stop();
        } else {
            Start();
        }
        return IsRecording;
    }

    public static void Start() {
        if(IsRecording) {
            return;
        }
        EnsureConf();
        Error = null;

        if(!EnsureNative()) {
            Error = NativeError ?? "native encoder unavailable";
            MainCore.Log.Wrn($"[Recorder] cannot start: {Error}");
            return;
        }

        if(session == null) {
            GameObject go = new("KorenRecorderSession");
            go.transform.SetParent(MainCore.Root.transform, false);
            session = go.AddComponent<RecorderSession>();
        }

        session.Begin();
    }

    public static void Stop() {
        if(!IsRecording) {
            return;
        }
        session?.RequestStop();
    }

    // Hard stop with no finalize — used when the mod is disabled / torn down so a
    // half-written file and a pinned capture framerate don't outlive the session.
    public static void Restore() {
        if(session != null) {
            session.Abort();
            Object.Destroy(session.gameObject);
            session = null;
        }
        Current = State.Idle;
    }

    internal static void OnSessionEnded() {
        // The component self-destructs after finalizing; drop our reference so the
        // next Start recreates it cleanly.
        session = null;
    }
}
