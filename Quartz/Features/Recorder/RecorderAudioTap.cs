using UnityEngine;

namespace Quartz.Features.Recorder;

// Taps the game's final master mix (music + hit sounds + any mixer effects) for the
// realtime renderer. Added to the AudioListener's GameObject during a render: Unity
// then calls OnAudioFilterRead on the audio thread with the post-mix interleaved PCM,
// which we copy into a buffer for the capture loop to drain a frame at a time. When
// SilenceSpeakers is set we then zero the buffer so local playback is muted while the
// captured copy keeps the real signal (capture-only render).
//
// This is the macOS path: Unity's offline AudioRenderer yields only silence there, so
// instead the level plays in real time and we record what the engine actually outputs.
internal sealed class RecorderAudioTap : MonoBehaviour {
    private readonly object gate = new();
    private float[] buf = new float[1 << 18];   // grows if a hitch lets it back up
    private int len;                            // interleaved floats currently buffered

    public int Channels { get; private set; } = 2;
    public long TotalSamples { get; private set; }       // interleaved floats seen (diagnostic)
    public long Callbacks { get; private set; }          // OnAudioFilterRead invocations (diagnostic)
    public float FirstNonZeroSample { get; private set; } // first audible sample seen (diagnostic)
    private bool sawNonZero;

    // When true (capture-only render), zero `data` AFTER copying so the speakers stay
    // silent while we still record the real mix. Our tap is the LAST filter on the
    // listener GO (runtime-added => end of component order), so this is the final DSP
    // stage and the output device gets silence.
    public bool SilenceSpeakers;

    // Audio thread. Copy the mix out; then optionally silence local playback.
    private void OnAudioFilterRead(float[] data, int channels) {
        lock(gate) {
            Channels = channels;
            Callbacks++;
            int n = data.Length;
            if(!sawNonZero) {
                for(int i = 0; i < n; i++) {
                    if(data[i] != 0f) { FirstNonZeroSample = data[i]; sawNonZero = true; break; }
                }
            }
            if(len + n > buf.Length) {
                int cap = buf.Length;
                while(len + n > cap) {
                    cap <<= 1;
                }
                System.Array.Resize(ref buf, cap);
            }
            System.Array.Copy(data, 0, buf, len, n);
            len += n;
            TotalSamples += n;
        }
        if(SilenceSpeakers) {
            System.Array.Clear(data, 0, data.Length);   // capture already taken above
        }
    }

    // Main thread. Copy everything captured since the last call into dst (grown if
    // needed) and clear. Returns per-channel sample frames (what the encoder wants).
    public int Drain(ref float[] dst) {
        lock(gate) {
            if(len <= 0) {
                return 0;
            }
            if(dst == null || dst.Length < len) {
                dst = new float[len];
            }
            System.Array.Copy(buf, 0, dst, 0, len);
            int interleaved = len;
            len = 0;
            return interleaved / Mathf.Max(1, Channels);
        }
    }
}
