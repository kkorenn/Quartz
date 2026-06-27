using System.Collections;
using Quartz.Core;
using Quartz.Features.Recorder.Native;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Quartz.Features.Recorder;

// Drives one offline render of a level run. Created by Recorder.BeginSession when
// a level starts while armed.
//
// Two capture modes, picked in Begin:
//
// 1. Live-audio mode (default, at 1x speed). Unity's AudioRenderer runs the audio
//    engine offline and advances AudioSettings.dspTime one captured frame at a time,
//    so the conductor runs on the REAL clock (we don't patch it) and we pull the
//    game's master mix — music AND hit sounds — a frame at a time. The game owns the
//    timing, so there's no per-level offset to guess and picture/sound can't drift.
//
// 2. Owned-clock mode (fallback: non-1x speed, audio off, or AudioRenderer refused).
//    ADOFAI's clock normally comes from the audio hardware, which doesn't advance
//    under offline capture, so here we OWN it: Recorder.RenderClock is the song
//    position, the conductor's dspTime reads are patched to it (RecorderPatches), and
//    we step it 1/fps per frame. Audio, if any, is muxed from the decoded song file
//    indexed by RenderClock — hit sounds are not included.
//
// Video (both modes) is captured by re-rendering the game cameras into an off-screen
// texture, so the white progress overlay and HUD aren't in the frame.
internal sealed class RecorderSession : MonoBehaviour {
    private NativeEncoder encoder;
    private bool running;
    private bool finalizeRequested;

    private int width, height, fps;
    private double frameDt;
    private int simOversample = 1;   // sim runs at fps*simOversample; encode every Nth sim frame

    private int prevCaptureFramerate;
    private bool prevAuto;
    private bool autoForced;
    private bool asyncToggled;
    private float prevListenerVolume;
    private bool listenerMuted;
    private int prevScreenW, prevScreenH;
    private FullScreenMode prevFullscreen;
    private bool resolutionForced;
    private bool prevRunInBackground;
    private bool runInBackgroundForced;
    private int prevVSync;
    private bool vSyncForced;
    private bool calibNeutralized;
    private int prevInputOffset, prevVisualOffset;

    private RenderTexture targetRT;
    private Texture2D readTex;
    private byte[] videoBuf;

    // Wall-clock rate sampling (immune to captureFramerate, which warps Time).
    private readonly System.Diagnostics.Stopwatch wallClock = new();

    // Camera.allCameras allocates + needs sorting every call; refresh it only
    // periodically since the camera set rarely changes mid-level.
    private Camera[] cachedCams;
    private int camRefreshCountdown;

    private double endClock;        // song position at which the render stops
    private double dspTimeSong;     // conductor's song anchor (deterministic start)
    private double renderStart;     // song position the render starts at
    private double warmupSeconds;   // real-time planet-spin settle before recording (not captured)
    private int preRollFrames;      // opening frames held + CAPTURED before the song (0 = off)
    private float[] preRollSilence; // one video-frame of silence, reused for every held frame

    // Audio muxed from the song clip.
    private float[] clipData;       // interleaved PCM, or null for silent render
    private int clipChannels;
    private int clipSampleRate;
    private int clipFrames;         // samples per channel
    private long audioCursor;       // next clip sample index (per channel) to emit
    private float[] audioFrameBuf;

    // Live-audio capture: the game's real master mix (music + hit sounds) pulled from
    // Unity's AudioRenderer a frame at a time. Used instead of clip muxing at 1x speed
    // — see the mode note at the top of the file.
    private bool audioCaptureLive;
    private int liveChannels, liveSampleRate;
    private long liveAudioFramesWritten;   // sample-frames pulled from AudioRenderer over the run (0 = silent track bug)
    private NativeArray<float> liveNative;
    private float[] liveManaged;

    // Realtime capture (macOS): the level plays at 1x and we record the live engine
    // mix via OnAudioFilterRead instead of the (silent-on-macOS) offline AudioRenderer.
    private bool realtimeAudio;
    private RecorderAudioTap audioTap;
    private AudioListener tapListener;   // the listener we tapped; enabled-cycled to splice the filter in
    private float[] audioDrainBuf;
    private int prevTargetFrameRate;
    private bool targetFrameRateForced;

    // Audio prepass (pass 1 of the two-pass macOS path): captures the live mix to a
    // buffer in real time; the video pass then muxes it.
    private bool prepassMode;
    private System.Collections.Generic.List<float> prepassBuf;
    private double prepassStartSongPos;   // song position of the first captured sample
    // clip_time = song_position + audioOffsetSec (the level's audio offset; see
    // ADOFAI's songposition = clip_time - offset), plus the user fine-tune.
    private double audioOffsetSec;

    public void Begin() {
        if(running) {
            return;
        }
        RecorderSettings c = Recorder.Conf;

        width = Mathf.Max(2, c.Width & ~1);
        height = Mathf.Max(2, c.Height & ~1);
        fps = Mathf.Clamp(c.Fps, 1, 240);
        float speed = Mathf.Clamp(c.Speed, 0.1f, 4f);
        frameDt = speed / fps;   // song-seconds advanced per captured frame (1.0/fps = real time)

        long videoBitrate = (long)c.VideoBitrateKbps * 1000;
        if(c.Codec.RequiresBitrate() && videoBitrate <= 0) {
            // Hardware encoder has no CRF mode, so "auto" is a resolution-scaled target.
            // ~0.1 bits/pixel/frame keeps 4K crisp; the old flat 16 Mbps was fine at 1080p
            // but only ~0.03 bpp at 4K (soft/smeary). Floored so small outputs stay clean.
            videoBitrate = Math.Max(8_000_000L, (long)(width * (long)height * fps * 0.1));
        }

        // Start at the first tile (the conductor's live song position isn't
        // anchored yet at scnGame.Play, which made renders begin mid-level) and
        // end at the last tile plus a tail for the outro.
        double startClock = ComputeStartClock();
        endClock = ComputeEndClock(startClock);
        if(c.SampleMode) {
            endClock = startClock + Mathf.Max(1, c.SampleSeconds); // short test clip
        }

        bool wantLiveAudio = c.CaptureAudio && Mathf.Approximately(speed, 1f);
        bool macOS = Application.platform == RuntimePlatform.OSXPlayer
                  || Application.platform == RuntimePlatform.OSXEditor;

        // macOS can't capture the mix offline (AudioRenderer is silent), so live audio is
        // done in TWO PASSES: a realtime AUDIO-ONLY pass taps the live engine into a
        // buffer, then the level restarts and the video is rendered OFFLINE (frame-perfect
        // 60fps) with that buffer muxed in. This replaces the single-pass realtime loop,
        // which dropped/duplicated frames to keep pace with realtime ("looked 15fps") and
        // bogged down on heavy levels. Both passes auto-play deterministically from the
        // same start, and audio is keyed to song position (sample-accurate, since hit
        // sounds are DSP-scheduled), so picture and sound line up.
        bool twoPassLiveAudio = wantLiveAudio && macOS;
        if(twoPassLiveAudio && Recorder.PrepassAudio == null) {
            BeginAudioPrepass(startClock);
            return;
        }

        // --- VIDEO RENDER PASS (offline, deterministic, frame-perfect) ---
        realtimeAudio = false;
        // In-game FPS oversampling: step the simulation at fps*oversample and encode only
        // every oversample-th frame, so the output is still fps but auto-hit detection,
        // tweens and FFX all advance on a finer step (the planet lands tighter on the beat
        // and fast motion is sampled at the higher rate). captureFramerate pins Time to the
        // SIM rate and decouples it from wall-clock, so the extra sim frames just make the
        // offline render take longer — they're stepped as fast as the machine can and never
        // dropped. The conductor clock is sub-stepped to match in CaptureLoop.
        simOversample = Mathf.Clamp(Recorder.Conf.Oversample, 1, 8);
        prevCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = fps * simOversample;

        // Warm-up (lead-in): before recording, spin the planet for this many seconds of
        // REAL time WITHOUT capturing, so the camera/readback path and the planet/conductor
        // simulation settle and any first-frame hitch is absorbed before the recorded part.
        // An auto render forces fastTakeoff/forceNoCountdown — the game jumps straight into
        // the song — so without this the render begins on the exact frame the level loads,
        // while the pipeline is still priming, which reads as the whole clip being out of
        // sync. The warm-up holds the clock in the pre-tile-0 region (the planet's angle is
        // linear in song position, so it winds toward the first tile — the engine's own
        // count-in motion), keeps the song SILENT (live mix muted, nothing encoded), and is
        // NOT in the output: recording begins clean at tile 0 (startClock is left unshifted).
        // The live AudioRenderer pull can't run silently without losing the song start, so
        // that path (non-macOS live audio) opts out; macOS live audio renders via the
        // two-pass MUX path, whose video pass keeps the warm-up.
        bool audioRendererPath = wantLiveAudio && !macOS && Recorder.PrepassAudio == null;
        warmupSeconds = audioRendererPath ? 0.0 : Mathf.Clamp(Recorder.Conf.LeadInSeconds, 0f, 30f);

        if(Recorder.PrepassAudio != null) {
            // Live mix captured in the audio pass — mux it like a song clip, keyed to song
            // position (buffer[0] == song position PrepassStartSongPos). The level's audio
            // offset is ALREADY baked into this capture (the game played the song with it),
            // so unlike the raw-file paths we do NOT add LevelOffsetSec here — only the
            // optional manual fine-tune (0 by default).
            clipData = Recorder.PrepassAudio;
            clipChannels = Recorder.PrepassChannels;
            clipSampleRate = Recorder.PrepassRate;
            clipFrames = clipData.Length / Mathf.Max(1, clipChannels);
            audioOffsetSec = -Recorder.PrepassStartSongPos + Recorder.Conf.AudioOffsetMs / 1000.0;
            audioCursor = (long)Math.Round((startClock + audioOffsetSec) * clipSampleRate);
            MainCore.Log.Msg($"[Recorder] VIDEO PASS (2/2): muxing captured live audio {clipSampleRate}Hz {clipChannels}ch, " +
                             $"{clipFrames} frames, start songpos {Recorder.PrepassStartSongPos:0.000}");
        } else if(wantLiveAudio && AudioRenderer.Start()) {
            audioCaptureLive = true;
            liveSampleRate = AudioSettings.outputSampleRate;
            liveChannels = SpeakerChannels(AudioSettings.GetConfiguration().speakerMode);
            MainCore.Log.Msg($"[Recorder] capturing game audio via AudioRenderer: {liveSampleRate}Hz {liveChannels}ch");
        } else if(c.CaptureAudio) {
            LoadAudio(startClock);
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
                AudioChannels = (realtimeAudio || audioCaptureLive) ? liveChannels : (clipData != null ? clipChannels : 0),
                AudioSampleRate = (realtimeAudio || audioCaptureLive) ? liveSampleRate : (clipData != null ? clipSampleRate : 0),
                AudioBitrate = (long)c.AudioBitrateKbps * 1000,
                AudioCodec = "aac",
            });
        } catch(Exception e) {
            // captureFramerate was already pinned (and AudioRenderer maybe started) above;
            // restore them or the whole game stays locked to the render frame-rate.
            RestoreState();
            Recorder.Error = e.Message;
            Recorder.Current = Recorder.State.Idle;
            MainCore.Log.Err($"[Recorder] {e}");
            Recorder.OnSessionEnded();
            Object.Destroy(gameObject);
            return;
        }

        targetRT = new RenderTexture(width, height, 24, RenderTextureFormat.Default) { name = "QuartzRecorderTarget" };
        targetRT.Create();
        readTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        videoBuf = new byte[width * height * 4];

        // Pre-roll: a held still of the opening, captured before the song. Only on the
        // muxed-audio paths (clipData != null), where the matching silence can be written
        // — the live AudioRenderer pull has no silence to emit. Counts toward the total.
        preRollFrames = clipData != null
            ? Mathf.Max(0, Mathf.RoundToInt(Mathf.Clamp(c.PreRollSeconds, 0f, 30f) * fps))
            : 0;

        Recorder.TotalFrames = Mathf.Max(0, Mathf.CeilToInt((float)((endClock - startClock) / frameDt))) + preRollFrames;
        Recorder.FramesWritten = 0;
        Recorder.OutputPath = outPath;
        Recorder.Error = null;

        ForceRunInBackground();          // keep the player loop ticking if the window is minimized/unfocused mid-render
        ForceAutoPlay();
        DisableAsyncInput();             // route auto-play hits through the frame loop (sync path)
        MatchGameResolution();           // size the game window to the output so the framing matches (no crop/letterbox)
        NeutralizeCalibration();         // an auto render has no input lag to compensate; the planet must land on the beat
        renderStart = startClock;
        Recorder.RenderClock = startClock;

        if(realtimeAudio) {
            // Realtime mode: leave the conductor on the real audio clock so playback,
            // hit sounds and the captured mix all advance together. The loop paces to
            // wall-clock; the end is the actual portal/Won (NotifyComplete) or endClock.
            // Don't mute — that mix is exactly what we're recording.
            Recorder.DrivingClock = false;

            // Oversample: drive the GAME at fps*Oversample so the conductor steps and
            // hit detection are finer (planet lands tighter on the beat). The video is
            // still emitted at fps (wall-clock driven in RealtimeCaptureLoop), so only
            // every Oversample-th frame is read back + encoded — the extra frames are
            // sim+draw only and stay cheap. vSync was already dropped to 0 in
            // ForceRunInBackground, so targetFrameRate is honored. Restored in RestoreState.
            int oversample = Mathf.Clamp(Recorder.Conf.Oversample, 1, 8);
            if(oversample > 1) {
                prevTargetFrameRate = Application.targetFrameRate;
                Application.targetFrameRate = fps * oversample;
                targetFrameRateForced = true;
                MainCore.Log.Msg($"[Recorder] oversampling: game at {fps * oversample}fps, encoding every {oversample} -> {fps}fps");
            }
        } else {
            // Offline modes own the conductor's clock. Anchor to its song reference
            // (dspTimeSong) and step exactly 1/fps per frame (RecorderPatches
            // transpiler) so the planet/visuals advance deterministically regardless of
            // how the audio engine behaves.
            dspTimeSong = scrConductor.instance != null ? scrConductor.instance.dspTimeSong : 0.0;
            Recorder.ControlledDsp = dspTimeSong + startClock;
            Recorder.DrivingClock = true;
            if(!audioCaptureLive) {
                MuteLiveAudio();         // song-mux mode: the live mix plays at the offline rate; silence it
            }
            // In AudioRenderer mode we do NOT mute — that mix is what we're recording.
        }

        Recorder.Current = Recorder.State.Recording;
        running = true;
        finalizeRequested = false;

        wallClock.Restart();
        Recorder.RenderFps = 0;
        camRefreshCountdown = 0;

        // During a warm-up the capture overlay is deferred so the player sees the live spin;
        // CaptureLoop reveals it when recording actually starts.
        if(warmupSeconds <= 0) {
            RecorderOverlay.Show();
            RecorderOverlay.Set(0, Recorder.TotalFrames, 0);
        }

        MainCore.Log.Msg($"[Recorder] rendering {width}x{height}@{fps}{(realtimeAudio ? " (realtime live-audio)" : "")}" +
                         $"{(simOversample > 1 ? $", sim {fps * simOversample}fps (in-game {simOversample}x)" : "")}" +
                         $"{(warmupSeconds > 0 ? $", {warmupSeconds:0.0}s warm-up spin (not recorded)" : "")}, " +
                         $"clock {startClock:0.00}..{endClock:0.00}s, ~{Recorder.TotalFrames} frames -> {outPath}");
        StartCoroutine(realtimeAudio ? RealtimeCaptureLoop() : CaptureLoop());
    }

    // --- audio prepass (pass 1 of 2 on macOS) --------------------------------

    // Play the level once in real time and tap the live mix into a buffer (no video).
    // No encoder, no captureFramerate, no owned clock — the game runs normally so the
    // audio thread produces the genuine master mix. On end, hand the buffer to Recorder,
    // which restarts the level into the offline video pass.
    private void BeginAudioPrepass(double startClock) {
        prepassMode = true;
        liveSampleRate = AudioSettings.outputSampleRate;
        liveChannels = SpeakerChannels(AudioSettings.GetConfiguration().speakerMode);
        renderStart = startClock;
        Recorder.RenderClock = startClock;
        prepassStartSongPos = -1.0;
        prepassBuf = new System.Collections.Generic.List<float>(Mathf.Max(1, liveSampleRate) * Mathf.Max(1, liveChannels) * 8);

        prevCaptureFramerate = Time.captureFramerate;   // realtime: leave captureFramerate as-is, restore in RestoreState
        StartAudioTap();                 // SilenceSpeakers = true; spliced in the loop
        ForceRunInBackground();
        ForceAutoPlay();
        DisableAsyncInput();
        NeutralizeCalibration();
        Recorder.DrivingClock = false;   // realtime: the game owns the clock

        // Realtime pass: it plays to the live-audio tail (silence), whose length isn't
        // known up front, so the tile-geometry frame estimate is only a lower bound and
        // the counter would overshoot it. Show it indeterminate (0 = no denominator).
        Recorder.TotalFrames = 0;
        Recorder.FramesWritten = 0;
        Recorder.Error = null;
        Recorder.Current = Recorder.State.Recording;
        running = true;
        finalizeRequested = false;
        wallClock.Restart();

        RecorderOverlay.Show();
        RecorderOverlay.Set(0, Recorder.TotalFrames, 0);
        MainCore.Log.Msg($"[Recorder] AUDIO PASS (1/2): capturing live mix at {liveSampleRate}Hz {liveChannels}ch (silent locally)");
        StartCoroutine(AudioPrepassLoop());
    }

    private IEnumerator AudioPrepassLoop() {
        var endOfFrame = new WaitForEndOfFrame();
        yield return SpliceAudioTap();

        double silentAudioSeconds = 0;
        while(running) {
            yield return endOfFrame;
            if(!running) {
                break;
            }
            if(Input.GetKeyDown(KeyCode.Escape)) {
                MainCore.Log.Msg("[Recorder] audio pass cancelled by user");
                running = false;
                StopAllCoroutines();
                RestoreState();
                RecorderOverlay.Hide();
                Recorder.ClearPrepass();
                Recorder.Current = Recorder.State.Idle;
                Recorder.OnSessionEnded();
                Object.Destroy(gameObject);
                yield break;
            }

            if(audioTap != null) {
                int got = audioTap.Drain(ref audioDrainBuf);
                if(got > 0) {
                    if(prepassStartSongPos < 0) {
                        // Anchor buffer[0] to its TRUE song position. CurrentSongPos() is read
                        // now, but `got` frames already accumulated in the tap since the splice,
                        // so the first captured sample is got/rate seconds older than now. Without
                        // this back-date the whole track anchors late and the muxed audio lags the
                        // video by that much.
                        prepassStartSongPos = CurrentSongPos() - (double)got / Mathf.Max(1, liveSampleRate);
                    }
                    int n = got * audioTap.Channels;
                    for(int i = 0; i < n; i++) {
                        prepassBuf.Add(audioDrainBuf[i]);
                    }
                    if(levelEnded) {
                        float peak = 0f;
                        for(int i = 0; i < n; i++) {
                            float a = audioDrainBuf[i];
                            if(a < 0f) { a = -a; }
                            if(a > peak) { peak = a; }
                        }
                        if(peak < SilenceThreshold) {
                            silentAudioSeconds += (double)got / Mathf.Max(1, liveSampleRate);
                        } else {
                            silentAudioSeconds = 0;
                        }
                    }
                }
            }

            Recorder.RenderClock = renderStart + wallClock.Elapsed.TotalSeconds;
            int prog = Mathf.Max(0, Mathf.FloorToInt((float)((Recorder.RenderClock - renderStart) / frameDt)));
            Recorder.FramesWritten = prog;   // drive the page readout too; TotalFrames is 0 (indeterminate)
            RecorderOverlay.Set(prog, Recorder.TotalFrames, 0);

            bool audioFinished = levelEnded && silentAudioSeconds >= SilenceGraceSeconds;
            if(finalizeRequested || audioFinished || Recorder.RenderClock >= endClock) {
                break;
            }
        }

        // Final tail.
        if(audioTap != null) {
            int got = audioTap.Drain(ref audioDrainBuf);
            int n = got * audioTap.Channels;
            for(int i = 0; i < n; i++) {
                prepassBuf.Add(audioDrainBuf[i]);
            }
        }

        int ch = audioTap != null ? audioTap.Channels : liveChannels;
        float[] pcm = prepassBuf.ToArray();
        double s0 = prepassStartSongPos < 0 ? renderStart : prepassStartSongPos;
        int rate = liveSampleRate;

        if(pcm.Length == 0) {
            // Tap never fired (no active AudioListener / OnAudioFilterRead never called).
            // The video pass still runs; warn loudly so a silent render isn't a mystery.
            MainCore.Log.Wrn("[Recorder] AUDIO PASS captured 0 samples — the live tap never produced audio; the rendered video will have NO sound");
        }

        running = false;
        prepassMode = false;
        RestoreState();
        RecorderOverlay.Hide();
        MainCore.Log.Msg($"[Recorder] AUDIO PASS done: {pcm.Length / Mathf.Max(1, ch)} frames captured, start songpos {s0:0.000} — restarting for video");

        Object.Destroy(gameObject);
        Recorder.OnAudioPrepassComplete(pcm, rate, ch, s0);   // stores buffer, re-arms, restarts -> video pass
    }

    // Live song position (seconds): real dsp time minus the conductor's song anchor,
    // the same quantity the video pass uses for RenderClock.
    private static double CurrentSongPos() {
        scrConductor cond = scrConductor.instance;
        return cond != null ? AudioSettings.dspTime - cond.dspTimeSong : 0.0;
    }

    private const double EndingTailSeconds = 3.0;
    private const double RealtimeMaxTailSeconds = 30.0;  // safety cap if the live mix never goes silent (looping track)
    private const double SilenceGraceSeconds = 0.75;     // sustained quiet after the level ends before we stop
    private const float SilenceThreshold = 3e-4f;        // ~-70 dB peak counts as silence
    private bool endingExtended;
    private bool levelEnded;

    public void RequestFinalize() {
        if(running) {
            finalizeRequested = true;
        }
    }

    // Reached the end portal / Won. Offline modes capture a short fixed outro then stop.
    // Realtime mode keeps recording the live mix until it actually goes silent (handled
    // in RealtimeCaptureLoop) so a song's outro/reverb tail isn't clipped; endClock here
    // is only a safety cap against a looping track that never ends.
    public void ExtendToEnding() {
        if(running && !endingExtended) {
            endingExtended = true;
            levelEnded = true;
            if(realtimeAudio || prepassMode) {
                endClock = Recorder.RenderClock + RealtimeMaxTailSeconds;
                MainCore.Log.Msg("[Recorder] reached end portal; capturing until the live audio goes silent");
            } else {
                endClock = Recorder.RenderClock + EndingTailSeconds;
                MainCore.Log.Msg($"[Recorder] reached end portal; capturing {EndingTailSeconds:0.0}s outro");
            }
        }
    }

    public void Abort() {
        running = false;
        StopAllCoroutines();
        RestoreState();
        encoder?.Dispose();
        encoder = null;
        ReleaseBuffers();
        RecorderOverlay.Hide();
        Recorder.ClearPrepass();   // drop any orphaned audio-pass buffer so it can't hijack the next play
    }

    private IEnumerator CaptureLoop() {
        var endOfFrame = new WaitForEndOfFrame();
        // Screen.SetResolution applies on the NEXT frame, and the game rebuilds its
        // camera RT / display quad to the new size in its own Update. Let that settle
        // before the first capture so frame zero isn't shot at the old aspect (which
        // is exactly the crop the user sees). Bounded so a clamped fullscreen mode
        // that never reaches the exact size doesn't hang the render.
        for(int i = 0; resolutionForced && i < 10 && (Screen.width != width || Screen.height != height); i++) {
            yield return endOfFrame;
        }

        // Warm-up spin (NOT recorded): hold the clock in the pre-tile-0 region so the planet
        // winds toward the first tile (its angle is linear in song position), for warmupSeconds
        // of REAL time, rendering each frame through the capture path to prime camera/readback —
        // but encoding NOTHING and writing NO audio. The live mix is muted, so it's silent.
        // Recording then begins clean at tile 0 (renderStart); the spin never reaches the file.
        // captureFramerate decouples Time from wall-clock, so frames are stepped as fast as the
        // machine draws while the planet position tracks the real clock (natural spin rate).
        if(warmupSeconds > 0) {
            var warm = System.Diagnostics.Stopwatch.StartNew();
            double e;
            while(running && (e = warm.Elapsed.TotalSeconds) < warmupSeconds) {
                yield return endOfFrame;
                if(!running) {
                    break;
                }
                if(Input.GetKeyDown(KeyCode.Escape)) {
                    MainCore.Log.Msg("[Recorder] cancelled by user (warm-up)");
                    Abort();
                    Recorder.Current = Recorder.State.Idle;
                    Recorder.OnSessionEnded();
                    Object.Destroy(gameObject);
                    yield break;
                }
                double warmSong = renderStart - warmupSeconds + e;   // [tile0 - warmup, tile0)
                Recorder.RenderClock = warmSong;
                Recorder.ControlledDsp = dspTimeSong + warmSong;
                RenderVideoFrameToBuf();   // prime the capture path; result discarded
            }
            // Snap to the exact recording anchor and reveal the capture overlay.
            Recorder.RenderClock = renderStart;
            Recorder.ControlledDsp = dspTimeSong + renderStart;
            RecorderOverlay.Show();
            RecorderOverlay.Set(0, Recorder.TotalFrames, 0);
            wallClock.Restart();   // rate/ETA covers the recorded portion, not the warm-up
        }

        // Pre-roll: hold the opening still and CAPTURE it (with silent audio) before the
        // song, so the clip opens on the start instead of cutting onto the first note. One
        // render, reused for every held frame. Clip-mux paths only (preRollFrames is 0
        // otherwise), so the silence written here matches the muxed audio track frame-for-
        // frame and the song below stays sample-aligned (audioCursor is untouched — the
        // silence is written straight to the encoder, not pulled from the clip).
        if(preRollFrames > 0) {
            Recorder.RenderClock = renderStart;
            Recorder.ControlledDsp = dspTimeSong + renderStart;
            RenderVideoFrameToBuf();   // the opening frame, held for the whole pre-roll

            int silPerFrame = Mathf.Max(1, clipSampleRate / Mathf.Max(1, fps));
            int silLen = silPerFrame * Mathf.Max(1, clipChannels);
            if(preRollSilence == null || preRollSilence.Length != silLen) {
                preRollSilence = new float[silLen];
            }

            for(int k = 0; running && k < preRollFrames; k++) {
                yield return endOfFrame;
                if(!running) {
                    break;
                }
                if(Input.GetKeyDown(KeyCode.Escape)) {
                    MainCore.Log.Msg("[Recorder] cancelled by user (pre-roll)");
                    Abort();
                    Recorder.Current = Recorder.State.Idle;
                    Recorder.OnSessionEnded();
                    Object.Destroy(gameObject);
                    yield break;
                }
                // Keep the conductor pinned at tile 0 so it can't drift during the hold.
                Recorder.ControlledDsp = dspTimeSong + renderStart;
                if(!encoder.WriteVideo(videoBuf, videoBuf.Length)
                   || !encoder.WriteAudio(preRollSilence, silPerFrame)) {
                    Recorder.Error = encoder?.LastError ?? "pre-roll write failed";
                    MainCore.Log.Err($"[Recorder] {Recorder.Error}");
                    Abort();
                    Recorder.Current = Recorder.State.Idle;
                    Recorder.OnSessionEnded();
                    Object.Destroy(gameObject);
                    yield break;
                }
                Recorder.FramesWritten++;
                UpdateRate();
                RecorderOverlay.Set(Recorder.FramesWritten, Recorder.TotalFrames, Recorder.RenderFps);
            }
        }

        AutoHitSnap.ResetStats();

        // Oversampling: simulate at fps*oversample, encode only every oversample-th sim
        // frame. simStep counts SIM frames; video frame F == sim step F*oversample, where
        // songTime lands back exactly on renderStart + F*frameDt (so audio/mux alignment is
        // identical to a non-oversampled render). subDt is the song time per sim frame.
        int oversample = Mathf.Max(1, simOversample);
        double subDt = frameDt / oversample;
        long simStep = 0;
        while(running) {
            yield return endOfFrame;
            if(!running) {
                break;
            }

            if(Input.GetKeyDown(KeyCode.Escape)) {
                MainCore.Log.Msg("[Recorder] cancelled by user");
                Abort();
                Recorder.Current = Recorder.State.Idle;
                Recorder.OnSessionEnded();
                Object.Destroy(gameObject);
                yield break;
            }

            // Deterministic song time for THIS sim frame (the conductor is driven to exactly
            // this position via ControlledDsp). Sub-stepped by subDt; on encode boundaries it
            // lands back on renderStart + F*frameDt. Computed, not read back, so it can't drift.
            double songTime = renderStart + simStep * subDt;
            Recorder.RenderClock = songTime;

            // Tile snap: the game's auto-hit (scrPlayer.OttoHoldHit) and the planet-angle
            // refresh (scrPlanet.Update_RefreshAngles) are separate MonoBehaviours with
            // undefined relative Update order, so on the frame an angle first crosses a
            // tile the hit can lag a frame and the planet renders PAST the tile. Driving
            // the clock deterministically makes that lag deterministic, not absent, so we
            // still flush any due auto-hit here — at end-of-frame, right before the cameras
            // are re-rendered — exactly as the (now-unused) realtime loop did. No-op when
            // nothing is due; hitsounds are DSP-scheduled so firing the hit here doesn't
            // shift them. Without this the snap only lived in RealtimeCaptureLoop and the
            // offline video pass (the actual renderer) never snapped — the planet-late bug.
            // Runs every sim frame so the oversampled in-between steps stay on the beat too.
            if(Recorder.Conf.SnapPlanetToBeat) {
                AutoHitSnap.FlushDueAutoHits();
            }

            // Audio every sim frame: the clip-mux path is keyed to RenderClock and the
            // AudioRenderer path pumps one capture-frame (captureFramerate == fps*oversample),
            // so writing at the finer cadence keeps the track continuous and exactly as long
            // as the video regardless of the oversample factor.
            if(!WriteFrameAudio()) {
                Recorder.Error = encoder?.LastError ?? "audio write failed";
                MainCore.Log.Err($"[Recorder] {Recorder.Error}");
                Abort();
                Recorder.Current = Recorder.State.Idle;
                Recorder.OnSessionEnded();
                Object.Destroy(gameObject);
                yield break;
            }

            // Encode a video frame only on oversample boundaries; the in-between sim frames
            // just advance the simulation (and the snap/audio) as fast as the machine steps.
            if(simStep % oversample == 0) {
                if(!CaptureVideoFrame()) {
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

            simStep++;
            Recorder.ControlledDsp = dspTimeSong + renderStart + simStep * subDt; // next sim frame
            UpdateRate();
            RecorderOverlay.Set(Recorder.FramesWritten, Recorder.TotalFrames, Recorder.RenderFps);

            if(finalizeRequested || songTime >= endClock) {
                break;
            }
        }
        if(Recorder.Conf.SnapPlanetToBeat) {
            MainCore.Log.Msg($"[Recorder] tile snap: ran {AutoHitSnap.Invocations} frames, force-advanced a tile on {AutoHitSnap.ForcedAdvances}" +
                             (AutoHitSnap.Invocations == 0 ? " (NEVER ran — guard blocked: not auto / not PlayerControl)" : ""));
        }
        CompleteAndSave();
    }

    // Render + read back + encode ONE video frame. Split from the audio write so the offline
    // loop can oversample: video is emitted every oversample-th sim frame while audio is
    // written every sim frame.
    private bool CaptureVideoFrame() {
        RenderVideoFrameToBuf();
        return encoder.WriteVideo(videoBuf, videoBuf.Length);
    }

    // Write one sim frame of audio: the live AudioRenderer pull, or the clip mux keyed to
    // RenderClock (captured-live two-pass buffer, or decoded song file).
    private bool WriteFrameAudio() {
        return audioCaptureLive ? CaptureLiveAudio() : WriteAudioForFrame();
    }

    // Realtime capture loop (macOS live-audio path). The level plays at 1x on the real
    // clock; the audio thread feeds the master mix into the tap continuously. Each loop:
    // drain the captured audio and write it, then emit video frames so the video length
    // tracks wall-clock — i.e. stays in sync with the realtime audio. A heavy hitch
    // duplicates the current frame to catch up; both audio and video are wall-clock
    // driven so there's no cumulative drift.
    private IEnumerator RealtimeCaptureLoop() {
        var endOfFrame = new WaitForEndOfFrame();

        // Splice the tap into the DSP chain BEFORE the capture clock starts so
        // OnAudioFilterRead is firing by t=0 and the a/v head stays aligned.
        yield return SpliceAudioTap();

        for(int i = 0; resolutionForced && i < 10 && (Screen.width != width || Screen.height != height); i++) {
            yield return endOfFrame;
        }

        wallClock.Restart();
        long audioFramesWritten = 0;
        bool loggedFirstAudio = false;
        double silentAudioSeconds = 0;   // run of quiet (audio-domain) since the level ended
        AutoHitSnap.ResetStats();

        while(running) {
            yield return endOfFrame;
            if(!running) {
                break;
            }
            if(Input.GetKeyDown(KeyCode.Escape)) {
                MainCore.Log.Msg("[Recorder] cancelled by user");
                Abort();
                Recorder.Current = Recorder.State.Idle;
                Recorder.OnSessionEnded();
                Object.Destroy(gameObject);
                yield break;
            }

            // 1) Flush whatever the live tap captured since last frame.
            if(audioTap != null) {
                int got = audioTap.Drain(ref audioDrainBuf);
                if(got > 0) {
                    if(!encoder.WriteAudio(audioDrainBuf, got)) {
                        Recorder.Error = encoder?.LastError ?? "audio write failed";
                        MainCore.Log.Err($"[Recorder] {Recorder.Error}");
                        Abort();
                        Recorder.Current = Recorder.State.Idle;
                        Recorder.OnSessionEnded();
                        Object.Destroy(gameObject);
                        yield break;
                    }
                    audioFramesWritten += got;
                    if(!loggedFirstAudio) {
                        MainCore.Log.Msg($"[Recorder] live tap producing audio: {got} frames x {audioTap.Channels}ch");
                        loggedFirstAudio = true;
                    }

                    // After the level ends, keep recording until the live mix actually
                    // goes quiet (so a song's outro/reverb tail isn't clipped). Measure
                    // the run of silence in the audio domain from this drained chunk.
                    if(levelEnded) {
                        int samples = got * audioTap.Channels;
                        float peak = 0f;
                        for(int i = 0; i < samples; i++) {
                            float a = audioDrainBuf[i];
                            if(a < 0f) { a = -a; }
                            if(a > peak) { peak = a; }
                        }
                        if(peak < SilenceThreshold) {
                            silentAudioSeconds += (double)got / Mathf.Max(1, liveSampleRate);
                        } else {
                            silentAudioSeconds = 0;
                        }
                    }
                }
            }

            // 2) Drive video off wall-clock so output is realtime-paced. Render the
            //    current game frame once; duplicate it if we owe more than one frame.
            double wallElapsed = wallClock.Elapsed.TotalSeconds;
            Recorder.RenderClock = renderStart + wallElapsed;
            int targetFrames = (int)(wallElapsed * fps);
            if(targetFrames > Recorder.FramesWritten) {
                // Tile snap: at end-of-frame (here, just before the cameras are
                // re-rendered) advance any auto-hit whose angle reached its tile this
                // frame, so the planet is drawn ON the tile, never one frame past it.
                // No-op when nothing is due; only runs on frames we actually encode.
                // Hitsounds are scheduled by song DSP time, so firing the hit here does
                // not shift them — the live tap still records each at its scheduled time.
                if(Recorder.Conf.SnapPlanetToBeat) {
                    AutoHitSnap.FlushDueAutoHits();
                }
                RenderVideoFrameToBuf();
                while(Recorder.FramesWritten < targetFrames) {
                    if(!encoder.WriteVideo(videoBuf, videoBuf.Length)) {
                        Recorder.Error = encoder?.LastError ?? "video write failed";
                        MainCore.Log.Err($"[Recorder] {Recorder.Error}");
                        Abort();
                        Recorder.Current = Recorder.State.Idle;
                        Recorder.OnSessionEnded();
                        Object.Destroy(gameObject);
                        yield break;
                    }
                    Recorder.FramesWritten++;
                }
            }

            UpdateRate();
            RecorderOverlay.Set(Recorder.FramesWritten, Recorder.TotalFrames, Recorder.RenderFps);

            // Stop on: a fail (finalizeRequested); or, once the level has ended, the live
            // audio having been silent long enough (its outro finished); or the safety
            // cap / SampleMode length (RenderClock >= endClock).
            bool audioFinished = levelEnded && silentAudioSeconds >= SilenceGraceSeconds;
            if(finalizeRequested || audioFinished || Recorder.RenderClock >= endClock) {
                if(audioFinished) {
                    MainCore.Log.Msg($"[Recorder] live audio quiet for {SilenceGraceSeconds:0.00}s after level end — stopping");
                } else if(levelEnded && Recorder.RenderClock >= endClock) {
                    MainCore.Log.Msg($"[Recorder] {RealtimeMaxTailSeconds:0.0}s safety cap reached after level end — stopping (audio never went silent)");
                }
                break;
            }
        }

        // Final tail of audio captured after the last emitted frame.
        if(running && audioTap != null) {
            int got = audioTap.Drain(ref audioDrainBuf);
            if(got > 0) {
                encoder.WriteAudio(audioDrainBuf, got);
            }
        }
        if(!loggedFirstAudio) {
            MainCore.Log.Wrn("[Recorder] live tap produced NO audio — OnAudioFilterRead never fired; output has no sound");
        }
        if(Recorder.Conf.SnapPlanetToBeat) {
            MainCore.Log.Msg($"[Recorder] tile snap: ran {AutoHitSnap.Invocations} frames, force-advanced a tile on {AutoHitSnap.ForcedAdvances}" +
                             (AutoHitSnap.Invocations == 0 ? " (NEVER ran — guard blocked: not auto / not PlayerControl)" : ""));
        }
        CompleteAndSave();
    }

    // Re-render the game cameras into the capture RT and read them into videoBuf
    // (no audio). Used by the realtime loop, which writes audio separately.
    private void RenderVideoFrameToBuf() {
        RenderGameInto(targetRT);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = targetRT;
        readTex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
        RenderTexture.active = prev;
        NativeArray<byte> raw = readTex.GetRawTextureData<byte>();
        raw.CopyTo(videoBuf);
    }

    // Pull one capture-frame of the game's master mix (music + hit sounds) from
    // AudioRenderer and hand it to the encoder. The buffer holds
    // GetSampleCountForCaptureFrame() sample-frames across liveChannels (interleaved),
    // sized to the frame rate so audio and picture advance together.
    private bool CaptureLiveAudio() {
        int avail = AudioRenderer.GetSampleCountForCaptureFrame();
        bool first = liveAudioFramesWritten == 0;   // one-shot even when oversampling steps before frame 0 is encoded

        // Pump a full capture-frame's worth EVERY frame. Two reasons:
        //  1. Some platforms (macOS) report 0 until Render() is actually called — if we
        //     bail when the count is 0 (as the old code did) the capture clock never
        //     advances and it stays 0 forever. Calling Render() each frame gives it the
        //     kick it needs.
        //  2. Writing a fixed expected count keeps the audio track exactly as long as
        //     the video and a/v aligned, even on frames where Render yields nothing.
        // 48000/60 = 800 exactly; if a rate isn't divisible the FIFO/AAC repacking
        // absorbs the ±1-sample jitter. Divide by the SIM rate (fps*oversample) since this
        // is pumped once per sim frame, not per encoded video frame.
        int expected = Mathf.Max(1, liveSampleRate / Mathf.Max(1, fps * simOversample));
        int frames = avail > 0 ? avail : expected;
        int need = frames * liveChannels;
        if(!liveNative.IsCreated || liveNative.Length != need) {
            if(liveNative.IsCreated) {
                liveNative.Dispose();
            }
            liveNative = new NativeArray<float>(need, Allocator.Persistent);
            liveManaged = new float[need];
        }
        bool rendered = AudioRenderer.Render(liveNative);
        if(rendered) {
            NativeArray<float>.Copy(liveNative, liveManaged, need);
            liveAudioFramesWritten += frames;
        } else {
            Array.Clear(liveManaged, 0, need); // not ready / no data this frame — silence keeps a/v aligned
        }
        if(first) {
            MainCore.Log.Msg($"[Recorder] frame0 AudioRenderer: avail={avail} wrote={frames}f x {liveChannels}ch render={rendered} (expect ~{expected})");
        }
        return encoder.WriteAudio(liveManaged, frames);
    }

    private static int SpeakerChannels(AudioSpeakerMode mode) => mode switch {
        AudioSpeakerMode.Mono => 1,
        AudioSpeakerMode.Stereo => 2,
        AudioSpeakerMode.Quad => 4,
        AudioSpeakerMode.Surround => 5,
        AudioSpeakerMode.Mode5point1 => 6,
        AudioSpeakerMode.Mode7point1 => 8,
        _ => 2,
    };

    // Emit the clip samples spanning this frame, keyed off RenderClock so audio
    // stays sample-accurate with the picture. Out-of-range (count-in / past the
    // song end) is written as silence.
    private bool WriteAudioForFrame() {
        if(clipData == null) {
            return true;
        }
        long target = (long)Math.Round((Recorder.RenderClock + audioOffsetSec) * clipSampleRate);
        int n = (int)(target - audioCursor);
        if(n <= 0) {
            return true;
        }

        int need = n * clipChannels;
        if(audioFrameBuf == null || audioFrameBuf.Length < need) {
            audioFrameBuf = new float[need];
        }
        for(int i = 0; i < n; i++) {
            long idx = audioCursor + i;
            int dst = i * clipChannels;
            if(idx >= 0 && idx < clipFrames) {
                long src = idx * clipChannels;
                for(int ch = 0; ch < clipChannels; ch++) {
                    audioFrameBuf[dst + ch] = clipData[src + ch];
                }
            } else {
                for(int ch = 0; ch < clipChannels; ch++) {
                    audioFrameBuf[dst + ch] = 0f;
                }
            }
        }
        audioCursor = target;
        return encoder.WriteAudio(audioFrameBuf, n);
    }

    // Cumulative wall-clock processing rate (frames / real seconds). Cumulative
    // rather than a short window so the ETA derived from it is stable and counts
    // down smoothly instead of jumping around.
    private void UpdateRate() {
        double elapsed = wallClock.Elapsed.TotalSeconds;
        if(elapsed > 0.25) {
            Recorder.RenderFps = Recorder.FramesWritten / elapsed;
        }
    }

    // Re-render every on-screen game camera into rt at the target resolution.
    // ScreenSpaceOverlay canvases (HUD + our white overlay) aren't tied to a
    // camera, so they're naturally excluded — the captured frame is clean gameplay.
    private void RenderGameInto(RenderTexture rt) {
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prevActive;

        float aspect = (float)width / height;
        if(cachedCams == null || camRefreshCountdown <= 0) {
            cachedCams = Camera.allCameras;
            Array.Sort(cachedCams, static (a, b) => a.depth.CompareTo(b.depth));
            camRefreshCountdown = 30;
        }
        camRefreshCountdown--;
        foreach(Camera cam in cachedCams) {
            if(cam == null || cam.targetTexture != null) {
                continue;
            }
            try {
                cam.aspect = aspect;
                cam.targetTexture = rt;
                cam.Render();
            } catch(Exception e) {
                MainCore.Log.Wrn($"[Recorder] camera '{cam.name}' render failed: {e.Message}");
            } finally {
                cam.targetTexture = null;
                cam.ResetAspect();
            }
        }
    }

    private void CompleteAndSave() {
        if(encoder == null) {
            return;
        }
        Recorder.Current = Recorder.State.Finalizing;
        if(audioCaptureLive && liveAudioFramesWritten == 0) {
            MainCore.Log.Wrn("[Recorder] live audio capture produced 0 samples — saved video will have NO audio track");
        }
        RestoreState();

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
        RecorderOverlay.Hide();

        running = false;
        Recorder.Current = Recorder.State.Idle;
        Recorder.ClearPrepass();        // video pass done — drop the captured-audio buffer
        Recorder.OnSessionEnded();
        Object.Destroy(gameObject);
    }

    // --- clock / audio sources ---------------------------------------------

    private static double ComputeStartClock() {
        try {
            var floors = ADOBase.lm?.listFloors;
            if(floors != null && floors.Count > 0 && floors[0] != null) {
                return floors[0].entryTime; // song position with the planet on tile 0
            }
        } catch { /* level not ready */ }
        return 0.0;
    }

    // The level's own audio offset in seconds — the .adofai "offset" (ms) the chart was
    // authored with, i.e. the lead-in between the song file's start and the first beat.
    // The conductor exposes it as addoffset (set to levelData.offset * 0.001 in
    // SetupConductorWithLevelData). songposition = clip_time - offset, so a raw song clip
    // is read at clip_time = song_position + offset. Zero when no level/conductor is live.
    private static double LevelOffsetSec() {
        try {
            scrConductor cd = scrConductor.instance;
            // skipOffset: the conductor is playing without the lead-in (so song_position
            // already equals clip_time) — don't re-apply the offset in that case.
            return cd != null && !cd.skipOffset ? cd.addoffset : 0.0;
        } catch {
            return 0.0;
        }
    }

    private static double ComputeEndClock(double startClock) {
        const double tail = 3.0; // capture the outro after the last tile
        try {
            var floors = ADOBase.lm?.listFloors;
            if(floors != null && floors.Count > 0) {
                scrFloor last = floors[floors.Count - 1];
                if(last != null && last.entryTime > startClock) {
                    return last.entryTime + tail;
                }
            }
        } catch { /* fall through */ }
        return startClock + 1.0; // unknown — render a short clip rather than nothing
    }

    // Prefer decoding the level's song file (robust for streamed clips Unity
    // won't hand back); fall back to AudioClip samples.
    private void LoadAudio(double startClock) {
        string songPath = ResolveSongPath();
        if(songPath != null) {
            float[] pcm = NativeEncoder.DecodeAudioFile(songPath, out int sr, out int ch);
            if(pcm != null && sr > 0 && ch > 0) {
                clipData = pcm;
                clipSampleRate = sr;
                clipChannels = ch;
                clipFrames = pcm.Length / ch;

                // The raw song file isn't pre-shifted, so sync it with the LEVEL's own
                // audio offset (the .adofai "offset", read live as scrConductor.addoffset):
                // clip_time = song_position + offset. The manual AudioOffsetMs is an
                // optional fine-tune added on top (0 by default).
                audioOffsetSec = LevelOffsetSec() + Recorder.Conf.AudioOffsetMs / 1000.0;
                audioCursor = (long)Math.Round((startClock + audioOffsetSec) * sr);

                MainCore.Log.Msg($"[Recorder] audio from {Path.GetFileName(songPath)}: {sr}Hz {ch}ch, " +
                                 $"{clipFrames} frames, audio offset {audioOffsetSec:0.000}s (level {LevelOffsetSec():0.000}s)");
                return;
            }
        }
        LoadSongClip(startClock);
    }

    private static string ResolveSongPath() {
        try {
            string songFile = scnGame.instance?.levelData?.song;
            string levelPath = ADOBase.levelPath;

            // levelData.song is often blank — the real name lives in the .adofai's
            // "songFilename" field. Parse it out of the level file.
            if(string.IsNullOrEmpty(songFile) && !string.IsNullOrEmpty(levelPath) && File.Exists(levelPath)) {
                songFile = ExtractSongFilename(levelPath);
            }
            MainCore.Log.Msg($"[Recorder] song lookup: song='{songFile}' levelPath='{levelPath}'");

            if(string.IsNullOrEmpty(songFile)) {
                return null;
            }
            // Already absolute?
            if(File.Exists(songFile)) {
                return songFile;
            }
            // Otherwise it's relative to the level folder. levelPath is usually the
            // .adofai file; fall back to treating it as the folder itself.
            if(!string.IsNullOrEmpty(levelPath)) {
                string dir = File.Exists(levelPath) ? Path.GetDirectoryName(levelPath) : levelPath;
                if(!string.IsNullOrEmpty(dir)) {
                    string p = Path.Combine(dir, songFile);
                    MainCore.Log.Msg($"[Recorder] song candidate '{p}' exists={File.Exists(p)}");
                    if(File.Exists(p)) {
                        return p;
                    }
                }
            }
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] song path lookup failed: {e.Message}");
        }
        return null;
    }

    // Pull "songFilename": "x.ogg" out of a .adofai (lenient — the files have
    // trailing commas and odd whitespace, so a regex beats a JSON parser here).
    private static string ExtractSongFilename(string adofaiPath) {
        try {
            string text = File.ReadAllText(adofaiPath);
            var m = System.Text.RegularExpressions.Regex.Match(
                text, "\"songFilename\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if(m.Success) {
                return m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
            }
        } catch { /* unreadable level file */ }
        return null;
    }

    private void LoadSongClip(double startClock) {
        try {
            AudioSource song = scrConductor.instance?.song;
            AudioClip clip = song != null ? song.clip : null;
            if(clip == null || clip.samples <= 0) {
                return;
            }
            clipChannels = clip.channels;
            clipSampleRate = clip.frequency;
            clipFrames = clip.samples;
            clipData = new float[clipFrames * clipChannels];
            if(!clip.GetData(clipData, 0)) {
                MainCore.Log.Wrn("[Recorder] song clip GetData failed; rendering without audio");
                clipData = null;
                return;
            }
            // Same level audio offset (+ manual fine-tune) as the decoded-file path; the
            // in-memory clip is just as un-shifted, so it needs the offset too.
            audioOffsetSec = LevelOffsetSec() + Recorder.Conf.AudioOffsetMs / 1000.0;
            audioCursor = (long)Math.Round((startClock + audioOffsetSec) * clipSampleRate);
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] couldn't read song clip ({e.Message}); no audio");
            clipData = null;
        }
    }

    // --- realtime live-audio tap --------------------------------------------

    // Attach the OnAudioFilterRead tap to the active AudioListener so it sees the final
    // master mix. The component is INERT until SpliceAudioTap forces a DSP-chain rebuild
    // (AddComponent alone doesn't splice it). Detached in StopAudioTap.
    private void StartAudioTap() {
        try {
            tapListener = FindActiveAudioListener();
            if(tapListener == null) {
                MainCore.Log.Wrn("[Recorder] no active AudioListener found — realtime audio capture disabled");
                return;
            }
            audioTap = tapListener.gameObject.AddComponent<RecorderAudioTap>();
            audioTap.SilenceSpeakers = true;   // capture-only: keep the recording, mute the speakers
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] couldn't attach audio tap: {e.Message}");
            audioTap = null;
            tapListener = null;
        }
    }

    // FindObjectOfType<AudioListener>() can return a component whose .enabled is false on
    // an active GameObject (ADOFAI keeps listeners on pooled cameras). Tapping a
    // non-output listener gives zero callbacks — identical to the splice bug — so prefer
    // the genuinely active one.
    private static AudioListener FindActiveAudioListener() {
        AudioListener fallback = null;
        foreach(AudioListener l in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) {
            if(l == null) { continue; }
            fallback ??= l;
            if(l.isActiveAndEnabled) { return l; }
        }
        return fallback;
    }

    // Force Unity to rebuild the listener GO's DSP filter chain so the runtime-added
    // OnAudioFilterRead is collected into it. A same-frame off->on is coalesced to a
    // no-op, so a frame must pass between the writes. Run before the capture clock starts
    // so the tap is live at t=0 and the a/v head stays aligned.
    private IEnumerator SpliceAudioTap() {
        if(audioTap == null || tapListener == null) {
            yield break;
        }
        var eof = new WaitForEndOfFrame();
        tapListener.enabled = false;
        yield return eof;
        tapListener.enabled = true;
        yield return eof;
        int waited = 0;
        while(audioTap.TotalSamples == 0 && waited < 30) {   // ~0.5s @60fps
            yield return eof;
            waited++;
        }
        MainCore.Log.Msg($"[Recorder] tap after listener cycle: callbacks={audioTap.Callbacks} " +
                         $"samples={audioTap.TotalSamples} ch={audioTap.Channels} cfgCh={liveChannels} " +
                         $"firstNonZero={audioTap.FirstNonZeroSample:0.######} (waited {waited}f)");
        if(audioTap.TotalSamples == 0) {
            MainCore.Log.Wrn("[Recorder] OnAudioFilterRead never fired after the listener cycle — video will have no sound");
        }
    }

    private void StopAudioTap() {
        if(audioTap != null) {
            try { Object.Destroy(audioTap); } catch { }
            audioTap = null;
        }
        tapListener = null;
    }

    // --- state save/restore -------------------------------------------------

    // The capture loop advances on WaitForEndOfFrame. Minimizing or unfocusing the
    // window pauses/throttles Unity's player loop (default Application.runInBackground
    // == false), which freezes the coroutine: no frames are stepped, AudioRenderer
    // never gets pumped (so the captured mix goes silent) and the conductor stalls —
    // the render comes out slowed/garbled with no sound. Force background execution so
    // the engine keeps ticking regardless of window focus, and drop vSync so the loop
    // is paced by Unity's own timer rather than the OS display-link present, which the
    // compositor stops delivering when the window is occluded/minimized (the macOS
    // stall: runInBackground alone doesn't cover Dock-minimize / occlusion). The live
    // audio mix is rendered offline in software and doesn't depend on the OS audio
    // device, so it's captured whether or not the window is visible. Restored in
    // RestoreState.
    private void ForceRunInBackground() {
        try {
            prevRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            runInBackgroundForced = true;
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] couldn't force run-in-background: {e.Message}");
        }
        try {
            prevVSync = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            vSyncForced = true;
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] couldn't disable vSync: {e.Message}");
        }
    }

    private void ForceAutoPlay() {
        try {
            prevAuto = RDC.auto;
            RDC.auto = true;
            autoForced = true;
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] couldn't force auto-play: {e.Message}");
        }
    }

    // ADOFAI's low-latency input runs on a real-time background thread that
    // ignores offline capture, so auto-play hits land by wall-clock instead of our
    // frame clock and the planet falls behind. Disabling the async hook for the
    // render forces the frame-dependent (synchronous) input path.
    private void DisableAsyncInput() {
        try {
            if(AsyncInputManager.isActive) {
                AsyncInputManager.ToggleHook(false);
                asyncToggled = true;
            }
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] couldn't disable async input: {e.Message}");
        }
    }

    private void MuteLiveAudio() {
        try {
            prevListenerVolume = AudioListener.volume;
            AudioListener.volume = 0f;
            listenerMuted = true;
        } catch { /* ignore */ }
    }

    // Drive the game's Screen resolution to the output size for the duration of the
    // render. Everything the game frames off Screen.width/height — the orthographic
    // camera aspect, scrCamera's camRT, the display quad scale — then matches the
    // captured texture, so the video isn't cropped or letterboxed when the output
    // aspect differs from the player's window. Restored in RestoreState. No-op (and
    // nothing to restore) when the window already matches.
    private void MatchGameResolution() {
        try {
            if(Screen.width == width && Screen.height == height) {
                return;
            }
            prevScreenW = Screen.width;
            prevScreenH = Screen.height;
            prevFullscreen = Screen.fullScreenMode;
            Screen.SetResolution(width, height, prevFullscreen);
            resolutionForced = true;
            MainCore.Log.Msg($"[Recorder] game resolution {prevScreenW}x{prevScreenH} -> {width}x{height} for render");
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] couldn't set game resolution: {e.Message}");
        }
    }

    // Zero the conductor's calibration for the render. ADOFAI shifts song position by
    // the player's input-offset calibration (currentPreset.inputOffset — a default
    // preset can be ~100ms) so that manual hits line up with their hardware latency;
    // the auto planet's angle is driven from that same shifted song position
    // (songposition_minusi). In an offline auto render there is no input to compensate
    // for, and the muxed track is the raw song, so a nonzero input offset just makes
    // the planet reach each tile late — invisible on slow tiles, obvious on fast ones.
    // Neutralize input + visual offset for the run so the planet lands exactly on the
    // beat. currentPreset is a struct field, so this writes only the static copy, not
    // the saved presets list. Restored in RestoreState.
    private void NeutralizeCalibration() {
        try {
            prevInputOffset = scrConductor.currentPreset.inputOffset;
            prevVisualOffset = scrConductor.visualOffset;
            scrConductor.currentPreset.inputOffset = 0;
            scrConductor.visualOffset = 0;
            calibNeutralized = true;
            if(prevInputOffset != 0 || prevVisualOffset != 0) {
                MainCore.Log.Msg($"[Recorder] neutralized calibration for render (input {prevInputOffset}ms, visual {prevVisualOffset}ms -> 0)");
            }
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] couldn't neutralize calibration: {e.Message}");
        }
    }

    private void RestoreState() {
        Recorder.DrivingClock = false;
        Time.captureFramerate = prevCaptureFramerate;
        if(autoForced) {
            try { RDC.auto = prevAuto; } catch { }
            autoForced = false;
        }
        if(asyncToggled) {
            try { AsyncInputManager.ToggleHook(true); } catch { }
            asyncToggled = false;
        }
        if(listenerMuted) {
            try { AudioListener.volume = prevListenerVolume; } catch { }
            listenerMuted = false;
        }
        if(resolutionForced) {
            try { Screen.SetResolution(prevScreenW, prevScreenH, prevFullscreen); } catch { }
            resolutionForced = false;
        }
        if(runInBackgroundForced) {
            try { Application.runInBackground = prevRunInBackground; } catch { }
            runInBackgroundForced = false;
        }
        if(vSyncForced) {
            try { QualitySettings.vSyncCount = prevVSync; } catch { }
            vSyncForced = false;
        }
        if(targetFrameRateForced) {
            try { Application.targetFrameRate = prevTargetFrameRate; } catch { }
            targetFrameRateForced = false;
        }
        if(calibNeutralized) {
            try {
                scrConductor.currentPreset.inputOffset = prevInputOffset;
                scrConductor.visualOffset = prevVisualOffset;
            } catch { }
            calibNeutralized = false;
        }
        if(audioCaptureLive) {
            try { AudioRenderer.Stop(); } catch { }
            audioCaptureLive = false;
        }
        StopAudioTap();
    }

    private void ReleaseBuffers() {
        if(targetRT != null) { targetRT.Release(); Object.Destroy(targetRT); targetRT = null; }
        if(readTex != null) { Object.Destroy(readTex); readTex = null; }
        videoBuf = null;
        audioFrameBuf = null;
        clipData = null;
        if(liveNative.IsCreated) { liveNative.Dispose(); }
        liveManaged = null;
    }

    private void OnDestroy() {
        // Always restore — Begin()'s early-return paths (encoder fail, prepass handoff)
        // destroy the object with global engine state already mutated but running==false.
        // RestoreState + Hide + Dispose are all idempotent/guarded, so a normal teardown
        // that already ran them is unaffected.
        RestoreState();
        encoder?.Dispose();
        encoder = null;
        RecorderOverlay.Hide();
        if(liveNative.IsCreated) { liveNative.Dispose(); } // Persistent alloc isn't auto-freed
    }

    private string BuildOutputPath() {
        string dir = Recorder.ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(dir, $"adofai_{stamp}.mp4");
    }
}
