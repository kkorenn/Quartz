using System.Collections;
using Koren.Core;
using Koren.Features.Recorder.Native;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Koren.Features.Recorder;

// Drives one offline render. Attached to a throwaway GameObject by Recorder.
//
// Each captured frame, after the frame has fully rendered (WaitForEndOfFrame):
//   1. grab the final composited screen into a screen-sized RT,
//   2. blit-scale it to the target resolution,
//   3. read it back to the CPU and hand the RGBA bytes to the encoder,
//   4. pull this frame's slice of the game audio mix and hand it over too.
//
// Time.captureFramerate is pinned for the whole session so steps 1-4 can take as
// long as they need without the timeline noticing — that is what removes the
// choppiness from heavy levels.
internal sealed class RecorderSession : MonoBehaviour {
    private NativeEncoder encoder;
    private bool running;
    private bool stopRequested;

    private int width, height, fps;
    private int prevCaptureFramerate;

    private RenderTexture screenRT;
    private RenderTexture targetRT;
    private Texture2D readTex;
    private byte[] videoBuf;

    private bool audioOn;
    private int channels;
    private float[] audioBuf;

    public void Begin() {
        if(running) {
            return;
        }
        RecorderSettings c = Recorder.Conf;

        width = Mathf.Max(2, c.Width & ~1);   // libx264/yuv420p need even dims
        height = Mathf.Max(2, c.Height & ~1);
        fps = Mathf.Clamp(c.Fps, 1, 240);

        // Build the encoder config.
        long videoBitrate = (long)c.VideoBitrateKbps * 1000;
        if(c.Codec.RequiresBitrate() && videoBitrate <= 0) {
            videoBitrate = 16_000_000; // VideoToolbox can't do CRF; pick a sane default
        }

        audioOn = c.CaptureAudio;
        channels = ChannelsFor(AudioSettings.speakerMode);
        int sampleRate = AudioSettings.outputSampleRate;

        if(audioOn) {
            try {
                if(!AudioRenderer.Start()) {
                    MainCore.Log.Wrn("[Recorder] AudioRenderer.Start failed; recording without audio");
                    audioOn = false;
                }
            } catch(Exception e) {
                MainCore.Log.Wrn($"[Recorder] AudioRenderer unavailable ({e.Message}); recording without audio");
                audioOn = false;
            }
        }

        string outPath = BuildOutputPath();
        try {
            encoder = new NativeEncoder(new NativeEncoder.Config {
                OutputPath = outPath,
                VideoCodec = c.Codec.EncoderName(),
                Width = width,
                Height = height,
                Fps = fps,
                VideoBitrate = videoBitrate,
                Crf = c.Crf,
                Gop = 0,
                FlipVertical = c.FlipVertical,
                AudioChannels = audioOn ? channels : 0,
                AudioSampleRate = sampleRate,
                AudioBitrate = (long)c.AudioBitrateKbps * 1000,
                AudioCodec = "aac",
            });
        } catch(Exception e) {
            if(audioOn) {
                TryStopAudio();
            }
            Recorder.Error = e.Message;
            Recorder.Current = Recorder.State.Idle;
            MainCore.Log.Err($"[Recorder] {e}");
            Recorder.OnSessionEnded();
            Object.Destroy(gameObject);
            return;
        }

        // Allocate reusable buffers.
        targetRT = new RenderTexture(width, height, 0, RenderTextureFormat.Default) { name = "KorenRecorderTarget" };
        targetRT.Create();
        readTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        videoBuf = new byte[width * height * 4];

        prevCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = fps;

        Recorder.OutputPath = outPath;
        Recorder.FramesWritten = 0;
        Recorder.Error = null;
        Recorder.Current = Recorder.State.Recording;
        running = true;
        stopRequested = false;

        MainCore.Log.Msg($"[Recorder] started {width}x{height}@{fps} -> {outPath}");
        StartCoroutine(CaptureLoop());
    }

    public void RequestStop() {
        if(running) {
            stopRequested = true;
        }
    }

    // Tear down with no finalize (mod disabled / scene gone).
    public void Abort() {
        running = false;
        StopAllCoroutines();
        RestoreTime();
        TryStopAudio();
        encoder?.Dispose();
        encoder = null;
        ReleaseBuffers();
    }

    private IEnumerator CaptureLoop() {
        var endOfFrame = new WaitForEndOfFrame();
        while(running && !stopRequested) {
            yield return endOfFrame;
            if(!running || stopRequested) {
                break;
            }
            if(!CaptureFrame()) {
                // Encoder error mid-stream: abort without a (broken) finalize.
                Recorder.Error = encoder?.LastError ?? "capture failed";
                MainCore.Log.Err($"[Recorder] {Recorder.Error}");
                Abort();
                Recorder.Current = Recorder.State.Idle;
                Recorder.OnSessionEnded();
                Object.Destroy(gameObject);
                yield break;
            }
            Recorder.FramesWritten++;
        }
        CompleteAndSave();
    }

    private bool CaptureFrame() {
        // (1) ensure a screen-sized RT, (2) grab the composited frame.
        int sw = Mathf.Max(2, Screen.width);
        int sh = Mathf.Max(2, Screen.height);
        if(screenRT == null || screenRT.width != sw || screenRT.height != sh) {
            if(screenRT != null) {
                screenRT.Release();
                Object.Destroy(screenRT);
            }
            screenRT = new RenderTexture(sw, sh, 0, RenderTextureFormat.Default) { name = "KorenRecorderScreen" };
            screenRT.Create();
        }
        ScreenCapture.CaptureScreenshotIntoRenderTexture(screenRT);

        // (2->3) scale to target resolution and read back to the CPU.
        Graphics.Blit(screenRT, targetRT);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = targetRT;
        readTex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
        RenderTexture.active = prev;

        NativeArray<byte> raw = readTex.GetRawTextureData<byte>();
        raw.CopyTo(videoBuf);
        if(!encoder.WriteVideo(videoBuf, videoBuf.Length)) {
            return false;
        }

        // (4) this frame's audio slice.
        if(audioOn) {
            int n = AudioRenderer.GetSampleCountForCaptureFrame();
            if(n > 0) {
                int len = n * channels;
                var buffer = new NativeArray<float>(len, Allocator.Temp);
                AudioRenderer.Render(buffer);
                if(audioBuf == null || audioBuf.Length < len) {
                    audioBuf = new float[len];
                }
                NativeArray<float>.Copy(buffer, 0, audioBuf, 0, len);
                buffer.Dispose();
                if(!encoder.WriteAudio(audioBuf, n)) {
                    return false;
                }
            }
        }
        return true;
    }

    private void CompleteAndSave() {
        if(encoder == null) {
            return;
        }
        Recorder.Current = Recorder.State.Finalizing;
        RestoreTime();
        TryStopAudio();

        bool ok = encoder.Finish();
        if(!ok) {
            Recorder.Error = encoder.LastError;
            MainCore.Log.Err($"[Recorder] finish failed: {Recorder.Error}");
        } else {
            MainCore.Log.Msg($"[Recorder] saved {Recorder.FramesWritten} frames -> {Recorder.OutputPath}");
        }
        encoder.Dispose();
        encoder = null;
        ReleaseBuffers();

        running = false;
        Recorder.Current = Recorder.State.Idle;
        Recorder.OnSessionEnded();
        Object.Destroy(gameObject);
    }

    private void RestoreTime() {
        Time.captureFramerate = prevCaptureFramerate;
    }

    private void TryStopAudio() {
        if(!audioOn) {
            return;
        }
        try {
            AudioRenderer.Stop();
        } catch { /* already stopped / unsupported */ }
        audioOn = false;
    }

    private void ReleaseBuffers() {
        if(screenRT != null) { screenRT.Release(); Object.Destroy(screenRT); screenRT = null; }
        if(targetRT != null) { targetRT.Release(); Object.Destroy(targetRT); targetRT = null; }
        if(readTex != null) { Object.Destroy(readTex); readTex = null; }
        videoBuf = null;
        audioBuf = null;
    }

    private void OnDestroy() {
        // Safety net: if the GameObject is destroyed out from under us, don't
        // leave the engine pinned at a fixed framerate.
        if(running) {
            RestoreTime();
            TryStopAudio();
            encoder?.Dispose();
            encoder = null;
        }
    }

    private string BuildOutputPath() {
        string dir = Recorder.ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(dir, $"adofai_{stamp}.mp4");
    }

    private static int ChannelsFor(AudioSpeakerMode mode) => mode switch {
        AudioSpeakerMode.Mono => 1,
        AudioSpeakerMode.Stereo => 2,
        AudioSpeakerMode.Quad => 4,
        AudioSpeakerMode.Surround => 5,
        AudioSpeakerMode.Mode5point1 => 6,
        AudioSpeakerMode.Mode7point1 => 8,
        AudioSpeakerMode.Prologic => 2,
        _ => 2,
    };
}
