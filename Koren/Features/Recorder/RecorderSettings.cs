using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;

namespace Koren.Features.Recorder;

// Video codec choices exposed in the Editor tab. Maps to a libav encoder name.
public enum RecorderCodec {
    H264,          // libx264 — most compatible
    H264Hardware,  // h264_videotoolbox — macOS hardware encode (needs a bitrate)
    H265,          // libx265 — smaller files, slower
}

public static class RecorderCodecExtensions {
    public static string EncoderName(this RecorderCodec codec) => codec switch {
        RecorderCodec.H264 => "libx264",
        RecorderCodec.H264Hardware => "h264_videotoolbox",
        RecorderCodec.H265 => "libx265",
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

    // Target video bitrate in kbit/s. 0 means constant-quality (CRF) for the
    // software encoders; the hardware encoder always needs a real bitrate.
    public int VideoBitrateKbps = 16000;
    public int Crf = 18; // used only when VideoBitrateKbps == 0

    public RecorderCodec Codec = RecorderCodec.H264;

    public bool CaptureAudio = true;
    public int AudioBitrateKbps = 192;

    // Unity reads back textures bottom-up; the captured frame usually needs a
    // vertical flip. Exposed so it can be corrected if a platform differs.
    public bool FlipVertical = true;

    // Empty -> <UserData>/Koren/Renders. Otherwise an absolute folder path.
    public string OutputDirectory = "";

    public JToken Serialize() {
        return new JObject {
            [nameof(Width)] = Width,
            [nameof(Height)] = Height,
            [nameof(Fps)] = Fps,
            [nameof(VideoBitrateKbps)] = VideoBitrateKbps,
            [nameof(Crf)] = Crf,
            [nameof(Codec)] = (int)Codec,
            [nameof(CaptureAudio)] = CaptureAudio,
            [nameof(AudioBitrateKbps)] = AudioBitrateKbps,
            [nameof(FlipVertical)] = FlipVertical,
            [nameof(OutputDirectory)] = OutputDirectory,
        };
    }

    public void Deserialize(JToken token) {
        if(token == null) {
            return;
        }
        Width = IOUtils.Read(token, nameof(Width), Width);
        Height = IOUtils.Read(token, nameof(Height), Height);
        Fps = IOUtils.Read(token, nameof(Fps), Fps);
        VideoBitrateKbps = IOUtils.Read(token, nameof(VideoBitrateKbps), VideoBitrateKbps);
        Crf = IOUtils.Read(token, nameof(Crf), Crf);
        Codec = (RecorderCodec)IOUtils.Read(token, nameof(Codec), (int)Codec);
        CaptureAudio = IOUtils.Read(token, nameof(CaptureAudio), CaptureAudio);
        AudioBitrateKbps = IOUtils.Read(token, nameof(AudioBitrateKbps), AudioBitrateKbps);
        FlipVertical = IOUtils.Read(token, nameof(FlipVertical), FlipVertical);
        OutputDirectory = IOUtils.Read(token, nameof(OutputDirectory), OutputDirectory);
    }
}
