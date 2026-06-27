using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;

namespace Quartz.Features.Recorder;

// Video codec choices exposed in the Editor tab. Maps to a libav encoder name.
public enum RecorderCodec {
    H264,          // libx264 — most compatible
    H264Hardware,  // h264_videotoolbox — macOS hardware encode (needs a bitrate)
    H265,          // libx265 — smaller files, slower
    Av1,           // libsvtav1 (SVT-AV1) — smallest files, modern; supports CRF
}

public static class RecorderCodecExtensions {
    public static string EncoderName(this RecorderCodec codec) => codec switch {
        RecorderCodec.H264 => "libx264",
        RecorderCodec.H264Hardware => "h264_videotoolbox",
        RecorderCodec.H265 => "libx265",
        RecorderCodec.Av1 => "libsvtav1",
        _ => "libx264",
    };

    public static bool RequiresBitrate(this RecorderCodec codec) =>
        codec == RecorderCodec.H264Hardware; // VideoToolbox has no CRF mode
}

// Settings for the FFmpeg renderer (Editor tab). Persisted to Recorder.json.
// These describe how the offline render is encoded; starting/stopping a render
// is a transient action driven from the page, not a persisted toggle.
public sealed class RecorderSettings : ISettingsFile {
    public int Width = 1920;
    public int Height = 1080;
    public int Fps = 60;

    // Output playback speed. 1.0 = real time (the level as played); 0.5 = half
    // speed (slow-mo), 2.0 = double speed. Changes how much song time each output
    // frame advances, and stretches/compresses the muxed audio to match.
    public float Speed = 1.0f;

    // Target video bitrate in kbit/s. 0 = auto quality: constant-quality (CRF) for the
    // software encoders, or a resolution-scaled target for the hardware encoder (which
    // has no CRF mode). A fixed flat value starved 4K into softness (fine at 1080p), so
    // auto is the default; set a number to pin it.
    public int VideoBitrateKbps = 0;
    public int Crf = 18; // used only when VideoBitrateKbps == 0 (software encoders)

    public RecorderCodec Codec = RecorderCodec.H264;

    public bool CaptureAudio = true;
    public int AudioBitrateKbps = 192;

    // In-game FPS multiplier for the offline render: step the simulation at Fps * Oversample
    // and encode every Oversample-th sim frame, so the output stays at Fps but auto-hit
    // detection, tweens and FFX advance on a finer step (planet lands tighter on the beat,
    // fast motion sampled higher). Integer multiple only — sub-frames must divide evenly into
    // an output frame. 1 = off. Unlike the old realtime path this owns the clock and decouples
    // Time from wall-clock, so the extra sim frames just make the render take longer (stepped
    // as fast as the machine can), never strain a realtime budget. Clamped 1..8. Exposed in the
    // Editor tab as "In-Game FPS" (1×/2×/4×/8×). Pairs with SnapPlanetToBeat.
    public int Oversample = 1;

    // Realtime (macOS live-audio) capture only: at end-of-frame, advance any auto-play
    // hit whose orbit angle reached its tile this frame, so the captured frame shows the
    // planet ON the tile instead of one frame past it (a frame-quantization slip from the
    // undefined Update order between the planet's angle refresh and the auto-hit pass).
    // Routes through the game's own gated auto-hit loop, so it never double-hits and is a
    // no-op when nothing is due. Hitsounds are scheduled by song clock and are unaffected.
    // On by default; an escape hatch if a specific level interacts badly with it.
    public bool SnapPlanetToBeat = true;

    // OPTIONAL manual audio-sync fine-tune in milliseconds, added on top of the
    // level's own audio offset (the .adofai "offset", read live from the conductor —
    // see RecorderSession.LevelOffsetSec). The level offset already lines the song up,
    // so this defaults to 0; nudge it only if a particular file/encoder still drifts.
    // JSON-editable, no slider.
    public int AudioOffsetMs = 0;

    // Warm-up: seconds of silent planet-spin BEFORE recording starts (not captured).
    // An auto render forces fastTakeoff/forceNoCountdown, so the game jumps straight
    // into the song with no count-in — the render would otherwise begin on the exact
    // frame the level loads, where the encoder/camera/readback path is still priming,
    // which shows up as the song being out of sync for the whole clip. The warm-up
    // spins the planet (held in the pre-tile-0 region; its angle is linear in song
    // position) for this many seconds of real time WITHOUT encoding and with the song
    // muted, so the priming hitch is absorbed during the spin; recording then begins
    // clean at tile 0. The spin is NOT in the output — the video starts at the song.
    // 0 disables it. Applies to the muxed-audio paths (incl. the macOS two-pass video
    // pass) and silent renders; the live AudioRenderer pull can't run silently without
    // losing the song start, so that path ignores it. Clamped 0..30.
    public float LeadInSeconds = 5f;

    // Pre-roll: seconds of the opening frame held — and CAPTURED — at the very start
    // of the output, before the song begins. Unlike LeadInSeconds (a silent warm-up
    // that is NOT in the video), this IS in the video: the clip opens on a still of
    // the start with silent audio for this long, then the song plays. Gives the
    // viewer a beat to settle before the chart kicks off instead of a hard cut onto
    // the first note. Applies only to the muxed-audio paths (file mux and the macOS
    // two-pass video pass), where the matching silence can be written; 0 disables it.
    // Clamped 0..30.
    public float PreRollSeconds = 2f;

    // Unity reads back textures bottom-up; the captured frame usually needs a
    // vertical flip. Exposed so it can be corrected if a platform differs.
    public bool FlipVertical = true;

    // Empty -> <UserData>/Quartz/Renders. Otherwise an absolute folder path.
    public string OutputDirectory = "";

    // Sample mode: render only the first SampleSeconds of the level instead of the
    // whole thing — for quickly checking audio sync / settings on long levels.
    public bool SampleMode = false;
    public int SampleSeconds = 10;

    public JToken Serialize() {
        return new JObject {
            [nameof(Width)] = Width,
            [nameof(Height)] = Height,
            [nameof(Fps)] = Fps,
            [nameof(Speed)] = Speed,
            [nameof(VideoBitrateKbps)] = VideoBitrateKbps,
            [nameof(Crf)] = Crf,
            [nameof(Codec)] = (int)Codec,
            [nameof(CaptureAudio)] = CaptureAudio,
            [nameof(AudioBitrateKbps)] = AudioBitrateKbps,
            [nameof(Oversample)] = Oversample,
            [nameof(SnapPlanetToBeat)] = SnapPlanetToBeat,
            [nameof(AudioOffsetMs)] = AudioOffsetMs,
            [nameof(LeadInSeconds)] = LeadInSeconds,
            [nameof(PreRollSeconds)] = PreRollSeconds,
            [nameof(FlipVertical)] = FlipVertical,
            [nameof(OutputDirectory)] = OutputDirectory,
            [nameof(SampleMode)] = SampleMode,
            [nameof(SampleSeconds)] = SampleSeconds,
        };
    }

    public void Deserialize(JToken token) {
        if(token == null) {
            return;
        }
        Width = IOUtils.Read(token, nameof(Width), Width);
        Height = IOUtils.Read(token, nameof(Height), Height);
        Fps = IOUtils.Read(token, nameof(Fps), Fps);
        Speed = IOUtils.Read(token, nameof(Speed), Speed);
        VideoBitrateKbps = IOUtils.Read(token, nameof(VideoBitrateKbps), VideoBitrateKbps);
        Crf = IOUtils.Read(token, nameof(Crf), Crf);
        Codec = (RecorderCodec)IOUtils.Read(token, nameof(Codec), (int)Codec);
        CaptureAudio = IOUtils.Read(token, nameof(CaptureAudio), CaptureAudio);
        AudioBitrateKbps = IOUtils.Read(token, nameof(AudioBitrateKbps), AudioBitrateKbps);
        Oversample = IOUtils.Read(token, nameof(Oversample), Oversample);
        SnapPlanetToBeat = IOUtils.Read(token, nameof(SnapPlanetToBeat), SnapPlanetToBeat);
        AudioOffsetMs = IOUtils.Read(token, nameof(AudioOffsetMs), AudioOffsetMs);
        LeadInSeconds = IOUtils.Read(token, nameof(LeadInSeconds), LeadInSeconds);
        PreRollSeconds = IOUtils.Read(token, nameof(PreRollSeconds), PreRollSeconds);
        FlipVertical = IOUtils.Read(token, nameof(FlipVertical), FlipVertical);
        OutputDirectory = IOUtils.Read(token, nameof(OutputDirectory), OutputDirectory);
        SampleMode = IOUtils.Read(token, nameof(SampleMode), SampleMode);
        SampleSeconds = IOUtils.Read(token, nameof(SampleSeconds), SampleSeconds);
    }
}
