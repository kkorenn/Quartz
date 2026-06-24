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

    private int prevCaptureFramerate;
    private bool prevAuto;
    private bool autoForced;
    private bool asyncToggled;
    private float prevListenerVolume;
    private bool listenerMuted;
    private int prevScreenW, prevScreenH;
    private FullScreenMode prevFullscreen;
    private bool resolutionForced;
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
    private NativeArray<float> liveNative;
    private float[] liveManaged;
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
            videoBitrate = 16_000_000;
        }

        // Start at the first tile (the conductor's live song position isn't
        // anchored yet at scnGame.Play, which made renders begin mid-level) and
        // end at the last tile plus a tail for the outro.
        double startClock = ComputeStartClock();
        endClock = ComputeEndClock(startClock);
        if(c.SampleMode) {
            endClock = startClock + Mathf.Max(1, c.SampleSeconds); // short test clip
        }

        // Prefer capturing the game's real mix (music + hit sounds) via AudioRenderer.
        // It plays at 1x only, so non-1x renders fall back to muxing the song file.
        bool wantLiveAudio = c.CaptureAudio && Mathf.Approximately(speed, 1f);
        if(wantLiveAudio && AudioRenderer.Start()) {
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
                AudioChannels = audioCaptureLive ? liveChannels : (clipData != null ? clipChannels : 0),
                AudioSampleRate = audioCaptureLive ? liveSampleRate : (clipData != null ? clipSampleRate : 0),
                AudioBitrate = (long)c.AudioBitrateKbps * 1000,
                AudioCodec = "aac",
            });
        } catch(Exception e) {
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

        Recorder.TotalFrames = Mathf.Max(0, Mathf.CeilToInt((float)((endClock - startClock) / frameDt)));
        Recorder.FramesWritten = 0;
        Recorder.OutputPath = outPath;
        Recorder.Error = null;

        prevCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = fps;     // step Time-based visuals deterministically too
        ForceAutoPlay();
        DisableAsyncInput();             // route auto-play hits through the frame loop (sync path)
        MatchGameResolution();           // size the game window to the output so the framing matches (no crop/letterbox)
        NeutralizeCalibration();         // an auto render has no input lag to compensate; the planet must land on the beat
        renderStart = startClock;
        Recorder.RenderClock = startClock;

        if(audioCaptureLive) {
            // AudioRenderer advances AudioSettings.dspTime one captured frame at a
            // time, so the conductor runs on the real clock — we DON'T own it, and we
            // must NOT mute (that mix is what we're recording). The run stops at the
            // end portal (ExtendToEnding); endClock is padded as a safety net because
            // RenderClock here only approximates the conductor's song position.
            Recorder.DrivingClock = false;
            if(!c.SampleMode) {
                endClock += 60.0; // portal (ExtendToEnding) is the real stop; this just guards against RenderClock drift
            }
        } else {
            // Own the conductor's clock. Anchor to its song reference (dspTimeSong)
            // rather than live dsp time so the render always starts at the same song
            // position. Then step 1/fps per frame (see the RecorderPatches transpiler).
            MuteLiveAudio();             // the live mix plays at the offline rate; silence it
            dspTimeSong = scrConductor.instance != null ? scrConductor.instance.dspTimeSong : 0.0;
            Recorder.ControlledDsp = dspTimeSong + startClock;
            Recorder.DrivingClock = true;
        }

        Recorder.Current = Recorder.State.Recording;
        running = true;
        finalizeRequested = false;

        wallClock.Restart();
        Recorder.RenderFps = 0;
        camRefreshCountdown = 0;

        RecorderOverlay.Show();
        RecorderOverlay.Set(0, Recorder.TotalFrames, 0);

        MainCore.Log.Msg($"[Recorder] rendering {width}x{height}@{fps}, clock {startClock:0.00}..{endClock:0.00}s, " +
                         $"~{Recorder.TotalFrames} frames -> {outPath}");
        StartCoroutine(CaptureLoop());
    }

    private const double EndingTailSeconds = 3.0;
    private bool endingExtended;

    public void RequestFinalize() {
        if(running) {
            finalizeRequested = true;
        }
    }

    // Reached the end portal — keep going for a short outro instead of cutting
    // off, then stop. Anchored to the actual portal moment so it's right even if
    // the duration estimate was a little off.
    public void ExtendToEnding() {
        if(running && !endingExtended) {
            endingExtended = true;
            endClock = Recorder.RenderClock + EndingTailSeconds;
            MainCore.Log.Msg($"[Recorder] reached end portal; capturing {EndingTailSeconds:0.0}s outro");
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

            // Deterministic song time for this frame (the conductor is driven to
            // exactly this position via ControlledDsp). Computed, not read back, so
            // it can't drift.
            double songTime = renderStart + Recorder.FramesWritten * frameDt;
            Recorder.RenderClock = songTime;

            if(!CaptureFrame()) {
                Recorder.Error = encoder?.LastError ?? "capture failed";
                MainCore.Log.Err($"[Recorder] {Recorder.Error}");
                Abort();
                Recorder.Current = Recorder.State.Idle;
                Recorder.OnSessionEnded();
                Object.Destroy(gameObject);
                yield break;
            }

            Recorder.FramesWritten++;
            Recorder.ControlledDsp = dspTimeSong + renderStart + Recorder.FramesWritten * frameDt; // next frame
            UpdateRate();
            RecorderOverlay.Set(Recorder.FramesWritten, Recorder.TotalFrames, Recorder.RenderFps);

            if(finalizeRequested || songTime >= endClock) {
                break;
            }
        }
        CompleteAndSave();
    }

    private bool CaptureFrame() {
        RenderGameInto(targetRT);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = targetRT;
        readTex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
        RenderTexture.active = prev;

        NativeArray<byte> raw = readTex.GetRawTextureData<byte>();
        raw.CopyTo(videoBuf);
        if(!encoder.WriteVideo(videoBuf, videoBuf.Length)) {
            return false;
        }

        return audioCaptureLive ? CaptureLiveAudio() : WriteAudioForFrame();
    }

    // Pull one capture-frame of the game's master mix (music + hit sounds) from
    // AudioRenderer and hand it to the encoder. The buffer holds
    // GetSampleCountForCaptureFrame() sample-frames across liveChannels (interleaved),
    // sized to the frame rate so audio and picture advance together.
    private bool CaptureLiveAudio() {
        int frames = AudioRenderer.GetSampleCountForCaptureFrame();
        if(frames <= 0) {
            return true;
        }
        int need = frames * liveChannels;
        if(!liveNative.IsCreated || liveNative.Length != need) {
            if(liveNative.IsCreated) {
                liveNative.Dispose();
            }
            liveNative = new NativeArray<float>(need, Allocator.Persistent);
            liveManaged = new float[need];
        }
        if(AudioRenderer.Render(liveNative)) {
            NativeArray<float>.Copy(liveNative, liveManaged, need);
        } else {
            Array.Clear(liveManaged, 0, need); // engine not ready yet — silence keeps a/v aligned
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

                // Audio sync is a per-render adjustment driven entirely by the user
                // Audio Offset slider (the level's own offset value doesn't map
                // cleanly to clip time, so auto-applying it over/under-shot).
                // clip_time = song_position + AudioOffset.
                audioOffsetSec = Recorder.Conf.AudioOffsetMs / 1000.0;
                audioCursor = (long)Math.Round((startClock + audioOffsetSec) * sr);

                MainCore.Log.Msg($"[Recorder] audio from {Path.GetFileName(songPath)}: {sr}Hz {ch}ch, " +
                                 $"{clipFrames} frames, audio offset {audioOffsetSec:0.000}s");
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
            audioCursor = (long)Math.Round(startClock * clipSampleRate);
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] couldn't read song clip ({e.Message}); no audio");
            clipData = null;
        }
    }

    // --- state save/restore -------------------------------------------------

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
        if(running) {
            RestoreState();
            encoder?.Dispose();
            encoder = null;
            RecorderOverlay.Hide();
        }
        if(liveNative.IsCreated) { liveNative.Dispose(); } // Persistent alloc isn't auto-freed
    }

    private string BuildOutputPath() {
        string dir = Recorder.ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(dir, $"adofai_{stamp}.mp4");
    }
}
