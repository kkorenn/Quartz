using System.Collections.Generic;
using System.Linq;
using Quartz.Core;
using Quartz.Features.Recorder;
using Quartz.Resource;
using Quartz.UI.Generator;
using Quartz.UI.Objects.Impl;
using Quartz.UI.Utility;
using TMPro;
using UnityEngine;

namespace Quartz.UI.Factory.Page;

// FFmpeg renderer controls for the Editor tab. Built as a flat H1 section to
// match the rest of PageEditor. The render itself is offline (see Recorder), so
// the only live element is the status line driven by RecorderStatusBinder.
internal static partial class PageEditor {
    private static readonly Vector2Int[] ResolutionPresets = {
        new(1280, 720),
        new(1920, 1080),
        new(2560, 1440),
        new(3840, 2160),
    };

    public static void CreateRendererSection(Transform content) {
        Recorder.EnsureConf();
        Recorder.EnsureNative(); // populate status (idempotent; logs the loaded build)
        RecorderSettings conf = Recorder.Conf;
        RecorderSettings def = new();

        GenerateUI.Localize(
            GenerateUI.AddTextH1(GenerateUI.Row(content)),
            "HEADING_FFMPEG_RENDERER", "FFmpeg Renderer"
        );

        // --- Resolution: preset dropdown + width/height sliders kept in sync ---
        UISlider widthSlider = null;
        UISlider heightSlider = null;

        List<Vector2Int> presets = ResolutionPresets.ToList();
        Vector2Int currentRes = new(conf.Width, conf.Height);
        if(!presets.Contains(currentRes)) {
            presets.Insert(0, currentRes);
        }

        var resolutionDrop = GenerateUI.DropDown(
            GenerateUI.Row(content),
            new Vector2Int(def.Width, def.Height),
            currentRes,
            presets,
            r => $"{r.x} × {r.y}",
            r => {
                conf.Width = r.x;
                conf.Height = r.y;
                widthSlider?.Set(r.x, false);
                heightSlider?.Set(r.y, false);
                Recorder.Save();
            },
            "editor_render_resolution",
            300f,
            "Resolution"
        );
        resolutionDrop.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_RESOLUTION",
            "Output video resolution. Pick a preset or fine-tune with the width/height sliders below. The frame is captured at this size regardless of the game window."
        );

        static float evenFilter(float v) => Mathf.Round(v / 2f) * 2f;

        widthSlider = GenerateUI.Slider(
            GenerateUI.Row(content),
            def.Width,
            64f, 7680f, conf.Width, evenFilter, null, null,
            "Width", "editor_render_width"
        );
        widthSlider.Format = "0 px";
        widthSlider.OnChanged = v => conf.Width = (int)v;
        widthSlider.OnComplete = v => { conf.Width = (int)v; Recorder.Save(); };

        heightSlider = GenerateUI.Slider(
            GenerateUI.Row(content),
            def.Height,
            64f, 4320f, conf.Height, evenFilter, null, null,
            "Height", "editor_render_height"
        );
        heightSlider.Format = "0 px";
        heightSlider.OnChanged = v => conf.Height = (int)v;
        heightSlider.OnComplete = v => { conf.Height = (int)v; Recorder.Save(); };

        // --- Frame rate ---
        static float fpsFilter(float v) => Mathf.Round(v);
        UISlider fps = GenerateUI.Slider(
            GenerateUI.Row(content),
            def.Fps,
            10f, 240f, conf.Fps, fpsFilter, null, null,
            "Frame Rate", "editor_render_fps"
        );
        fps.Format = "0 fps";
        fps.OnChanged = v => conf.Fps = (int)v;
        fps.OnComplete = v => { conf.Fps = (int)v; Recorder.Save(); };
        fps.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_FPS",
            "Output frame rate. The engine is locked to this rate during the render, so every frame is captured — heavy levels render slower than real time but never drop frames."
        );

        // --- In-game FPS (simulation oversampling, integer multiple of output fps) ---
        List<int> inGameMults = new() { 1, 2, 4, 8 };
        int currentMult = Mathf.Clamp(conf.Oversample, 1, 8);
        if(!inGameMults.Contains(currentMult)) {
            inGameMults.Insert(0, currentMult);
        }
        var inGameFpsDrop = GenerateUI.DropDown(
            GenerateUI.Row(content),
            def.Oversample,
            currentMult,
            inGameMults,
            m => m <= 1 ? "1× (same as output)" : $"{m}× ({m * conf.Fps} fps)",
            m => { conf.Oversample = Mathf.Clamp(m, 1, 8); Recorder.Save(); },
            "editor_render_ingame_fps",
            300f,
            "In-Game FPS"
        );
        inGameFpsDrop.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_INGAME_FPS",
            "Steps the game simulation at this multiple of the output frame rate, then encodes every Nth frame. Higher means the planet lands tighter on the beat and fast motion is sampled finer — at the cost of a proportionally longer render. The output frame rate and file are unchanged; this only affects how finely the run is simulated. 4× at 60 fps simulates at 240."
        );

        // --- Playback speed ---
        static float speedFilter(float v) => Mathf.Clamp(Mathf.Round(v * 20f) / 20f, 0.1f, 4f);
        UISlider speed = GenerateUI.Slider(
            GenerateUI.Row(content),
            def.Speed,
            0.1f, 4f, conf.Speed, speedFilter, null, null,
            "Playback Speed", "editor_render_speed"
        );
        speed.Format = "0.00x";
        speed.OnChanged = v => conf.Speed = v;
        speed.OnComplete = v => { conf.Speed = v; Recorder.Save(); };
        speed.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_SPEED",
            "How fast the output plays relative to real time. 1.00x is the level exactly as played; lower is slow-motion, higher is sped up. Audio is stretched or compressed to match."
        );

        // --- Warm-up spin (silent settle before recording; not captured) ---
        static float leadInFilter(float v) => Mathf.Clamp(Mathf.Round(v * 2f) / 2f, 0f, 30f);
        UISlider leadIn = GenerateUI.Slider(
            GenerateUI.Row(content),
            def.LeadInSeconds,
            0f, 15f, conf.LeadInSeconds, leadInFilter, null, null,
            "Warm-Up Spin", "editor_render_leadin"
        );
        leadIn.Format = "0.0 s";
        leadIn.OnChanged = v => conf.LeadInSeconds = v;
        leadIn.OnComplete = v => { conf.LeadInSeconds = v; Recorder.Save(); };
        leadIn.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_LEADIN",
            "Seconds the planet spins silently to warm up before recording starts — this is NOT in the video. An auto render skips the count-in and begins on the frame the level loads, while the pipeline is still priming, which can leave the whole clip out of sync. The warm-up absorbs that hitch, then recording begins clean at the song. 0 turns it off."
        );

        // --- Pre-roll (a held opening still that IS captured before the song) ---
        static float preRollFilter(float v) => Mathf.Clamp(Mathf.Round(v * 2f) / 2f, 0f, 30f);
        UISlider preRoll = GenerateUI.Slider(
            GenerateUI.Row(content),
            def.PreRollSeconds,
            0f, 10f, conf.PreRollSeconds, preRollFilter, null, null,
            "Opening Hold", "editor_render_preroll"
        );
        preRoll.Format = "0.0 s";
        preRoll.OnChanged = v => conf.PreRollSeconds = v;
        preRoll.OnComplete = v => { conf.PreRollSeconds = v; Recorder.Save(); };
        preRoll.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_PREROLL",
            "Seconds the video holds on the opening still — with silent audio — before the song starts. Unlike the warm-up spin, this IS in the output: the clip opens on a freeze of the start, then plays, so it doesn't cut straight onto the first note. 0 turns it off."
        );

        // --- Video bitrate (Mbps in the UI, stored as kbps) ---
        static float mbpsFilter(float v) => Mathf.Round(v);
        UISlider bitrate = GenerateUI.Slider(
            GenerateUI.Row(content),
            def.VideoBitrateKbps / 1000f,
            1f, 150f, Mathf.Max(1, conf.VideoBitrateKbps) / 1000f, mbpsFilter, null, null,
            "Video Bitrate", "editor_render_bitrate"
        );
        bitrate.Format = "0 Mbps";
        bitrate.OnChanged = v => conf.VideoBitrateKbps = (int)v * 1000;
        bitrate.OnComplete = v => { conf.VideoBitrateKbps = (int)v * 1000; Recorder.Save(); };
        bitrate.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_BITRATE",
            "Target video bitrate in megabits per second. Higher is better quality and larger files. 16 Mbps is a good default for 1080p60."
        );

        // --- Codec ---
        List<RecorderCodec> codecs = new() { RecorderCodec.H264, RecorderCodec.H265, RecorderCodec.Av1 };
        if(Application.platform is RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor) {
            codecs.Insert(1, RecorderCodec.H264Hardware);
        }
        if(!codecs.Contains(conf.Codec)) {
            conf.Codec = RecorderCodec.H264;
        }
        var codecDrop = GenerateUI.DropDown(
            GenerateUI.Row(content),
            def.Codec,
            conf.Codec,
            codecs,
            CodecLabel,
            v => { conf.Codec = v; Recorder.Save(); },
            "editor_render_codec",
            300f,
            "Codec"
        );
        codecDrop.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_CODEC",
            "Video encoder. x264 is the most compatible; the hardware encoder (macOS) is fastest; x265 makes smaller files but encodes slower; AV1 (SVT-AV1) makes the smallest files but is the slowest to encode."
        );

        // --- Audio ---
        var captureAudio = GenerateUI.Toggle(
            GenerateUI.Row(content),
            def.CaptureAudio,
            conf.CaptureAudio,
            v => { conf.CaptureAudio = v; Recorder.Save(); },
            "Capture Audio",
            "editor_render_audio"
        );
        captureAudio.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_AUDIO",
            "Records the full game mix — music and hit sounds — in sync with the video. Turn off for a silent capture."
        );

        static float audioFilter(float v) => Mathf.Round(v / 32f) * 32f;
        UISlider audioBitrate = GenerateUI.Slider(
            GenerateUI.Row(content),
            def.AudioBitrateKbps,
            64f, 512f, conf.AudioBitrateKbps, audioFilter, null, null,
            "Audio Bitrate", "editor_render_audio_bitrate"
        );
        audioBitrate.Format = "0 kbps";
        audioBitrate.OnChanged = v => conf.AudioBitrateKbps = (int)v;
        audioBitrate.OnComplete = v => { conf.AudioBitrateKbps = (int)v; Recorder.Save(); };

        // --- Flip ---
        var flip = GenerateUI.Toggle(
            GenerateUI.Row(content),
            def.FlipVertical,
            conf.FlipVertical,
            v => { conf.FlipVertical = v; Recorder.Save(); },
            "Flip Vertically",
            "editor_render_flip"
        );
        flip.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_FLIP",
            "Flips the captured frame top-to-bottom. On by default because Unity reads the screen bottom-up; turn off only if your recordings come out upside down."
        );

        // --- Output folder ---
        GenerateUI.Input(
            GenerateUI.Row(content),
            null,
            string.IsNullOrWhiteSpace(conf.OutputDirectory) ? null : conf.OutputDirectory,
            v => { conf.OutputDirectory = v?.Trim() ?? ""; Recorder.Save(); },
            "Default folder (UserData/Quartz/Renders)",
            MainCore.Spr.Get(UISprite.Text128),
            "editor_render_output"
        ).Rect.AddToolTip(
            "DESC_EDITOR_RENDER_OUTPUT",
            "Folder the .mp4 files are written to. Leave blank to use UserData/Quartz/Renders. Each render is named with a timestamp."
        );

        // --- Sample mode (short test clip) ---
        var sampleMode = GenerateUI.Toggle(
            GenerateUI.Row(content),
            def.SampleMode,
            conf.SampleMode,
            v => { conf.SampleMode = v; Recorder.Save(); },
            "Sample mode (test clip)",
            "editor_render_sample"
        );
        sampleMode.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_SAMPLE",
            "Render only the first few seconds of the level instead of the whole thing — handy for checking audio sync and settings on long levels."
        );

        static float sampleFilter(float v) => Mathf.Round(v);
        UISlider sampleSeconds = GenerateUI.Slider(
            GenerateUI.Row(content),
            def.SampleSeconds,
            1f, 60f, conf.SampleSeconds, sampleFilter, null, null,
            "Sample Length", "editor_render_sample_seconds"
        );
        sampleSeconds.Format = "0 s";
        sampleSeconds.OnChanged = v => conf.SampleSeconds = (int)v;
        sampleSeconds.OnComplete = v => { conf.SampleSeconds = (int)v; Recorder.Save(); };

        // --- Render button + live status line ---
        UIButton renderButton = GenerateUI.Button(
            GenerateUI.Row(content),
            () => Recorder.Toggle(),
            "Render",
            "editor_render_button"
        );
        renderButton.Rect.AddToolTip(
            "DESC_EDITOR_RENDER_BUTTON",
            "Arms the renderer. Play a level and the whole run is recorded offline, frame by frame, then saved as an mp4 when you reach the end. The level is locked while rendering (auto-play on); a white screen shows the frame progress. Press Esc to cancel."
        );

        TextMeshProUGUI status = GenerateUI.AddText(GenerateUI.Row(content));
        status.alignment = TextAlignmentOptions.Left;
        status.textWrappingMode = TextWrappingModes.Normal;

        status.gameObject.AddComponent<RecorderStatusBinder>().Init(renderButton, status);
    }

    private static string CodecLabel(RecorderCodec codec) => codec switch {
        RecorderCodec.H264 => MainCore.Tr.Get("RENDER_CODEC_H264", "H.264 (x264)"),
        RecorderCodec.H264Hardware => MainCore.Tr.Get("RENDER_CODEC_H264_HW", "H.264 (hardware)"),
        RecorderCodec.H265 => MainCore.Tr.Get("RENDER_CODEC_H265", "H.265 (x265)"),
        RecorderCodec.Av1 => MainCore.Tr.Get("RENDER_CODEC_AV1", "AV1 (SVT-AV1)"),
        _ => codec.ToString(),
    };
}

// Drives the render button's label and the status line from Recorder's static
// state each frame (the render runs offline, so there's no callback to hook).
internal sealed class RecorderStatusBinder : MonoBehaviour {
    private UIButton button;
    private TextMeshProUGUI status;

    public void Init(UIButton button, TextMeshProUGUI status) {
        this.button = button;
        this.status = status;
    }

    // Last-rendered display inputs. BuildStatus/ButtonLabel allocate interpolated
    // strings + do dictionary Tr.Get lookups; this ran every frame even when nothing
    // on screen changed (e.g. idle "Saved: <path>"). Rebuild only on an actual change.
    private Recorder.State lastLabelState = (Recorder.State)(-1);
    private Recorder.State lastState = (Recorder.State)(-1);
    private int lastFrames = -1, lastTotal = -1, lastRate = -1;
    private string lastError = "", lastOutput = "";
    private bool lastReloading;

    private void Update() {
        if(button == null || status == null) {
            return;
        }

        Recorder.State state = Recorder.Current;
        if(state != lastLabelState && button.Label != null) {
            button.Label.text = ButtonLabel();   // depends only on state
            lastLabelState = state;
        }

        // Status depends on state + the live counters + rate + native/error/output.
        // During active recording the counters change every frame, so it still rebuilds
        // then (correct — the number is live); when idle it now costs nothing.
        int frames = Recorder.FramesWritten;
        int total = Recorder.TotalFrames;
        int rate = (int)Math.Round(Recorder.RenderFps);
        bool reloading = Recorder.Reloading;
        if(state != lastState || reloading != lastReloading || frames != lastFrames || total != lastTotal || rate != lastRate
           || !ReferenceEquals(Recorder.Error, lastError) || !ReferenceEquals(Recorder.OutputPath, lastOutput)) {
            status.text = BuildStatus();
            lastState = state;
            lastReloading = reloading;
            lastFrames = frames;
            lastTotal = total;
            lastRate = rate;
            lastError = Recorder.Error;
            lastOutput = Recorder.OutputPath;
        }
    }

    private static string ButtonLabel() => Recorder.Current switch {
        Recorder.State.Armed => MainCore.Tr.Get("RENDER_BUTTON_DISARM", "Disarm"),
        Recorder.State.Recording or Recorder.State.Finalizing =>
            MainCore.Tr.Get("RENDER_BUTTON_RENDERING", "Rendering…"),
        _ => MainCore.Tr.Get("RENDER_BUTTON_START", "Render"),
    };

    private static string BuildStatus() {
        if(!Recorder.NativeReady) {
            string unavailable = MainCore.Tr.Get("RENDER_STATUS_UNAVAILABLE", "Native encoder unavailable");
            return Recorder.NativeError != null ? $"{unavailable}: {Recorder.NativeError}" : unavailable;
        }

        switch(Recorder.Current) {
            case Recorder.State.Armed:
                return Recorder.Reloading
                    ? MainCore.Tr.Get("RENDER_STATUS_RELOADING", "Reloading level…")
                    : MainCore.Tr.Get("RENDER_STATUS_ARMED", "Armed — play a level to start rendering");
            case Recorder.State.Recording: {
                string r = MainCore.Tr.Get("RENDER_STATUS_RECORDING", "Recording");
                string rate = Recorder.RenderFps > 0 ? $" · {Recorder.RenderFps:0} fps" : "";
                return Recorder.TotalFrames > 0
                    ? $"{r} — {Recorder.FramesWritten:N0} / {Recorder.TotalFrames:N0} ({Recorder.Progress * 100f:0.0}%){rate}"
                    : $"{r} — {Recorder.FramesWritten:N0}{rate}";
            }
            case Recorder.State.Finalizing:
                return MainCore.Tr.Get("RENDER_STATUS_FINALIZING", "Finalizing…");
            default:
                if(Recorder.Error != null) {
                    return $"{MainCore.Tr.Get("RENDER_STATUS_ERROR", "Error")}: {Recorder.Error}";
                }
                if(!string.IsNullOrEmpty(Recorder.OutputPath)) {
                    return $"{MainCore.Tr.Get("RENDER_STATUS_SAVED", "Saved")}: {Recorder.OutputPath}";
                }
                return MainCore.Tr.Get("RENDER_STATUS_READY", "Ready");
        }
    }
}
