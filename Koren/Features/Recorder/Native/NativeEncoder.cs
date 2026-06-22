using System.Runtime.InteropServices;
using System.Text;
using Koren.Core;

namespace Koren.Features.Recorder.Native;

// Managed wrapper over the koren_encoder native library (see
// native/koren_encoder/koren_encoder.h). One instance owns one open encoder.
//
// The library is loaded once (Initialize) and its exports bound as delegates;
// after that, instances are cheap. All string fields cross the boundary as
// explicit UTF-8 buffers so non-ASCII output paths survive regardless of the
// Mono default charset.
internal sealed class NativeEncoder : IDisposable {
    // Mirrors KorenEncoderConfig. Sequential + natural alignment matches the C
    // struct on LP64/LLP64; strings are pre-marshalled UTF-8 pointers.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeConfig {
        public IntPtr output_path;
        public IntPtr video_codec;
        public int width;
        public int height;
        public int fps;
        public long video_bitrate;
        public int crf;
        public int gop;
        public int flip_vertical;
        public int audio_channels;
        public int audio_sample_rate;
        public long audio_bitrate;
        public IntPtr audio_codec;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr OpenDelegate(ref NativeConfig cfg, [Out] byte[] err, int errLen);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteVideoDelegate(IntPtr enc, byte[] rgba, int len);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteAudioDelegate(IntPtr enc, float[] pcm, int nbSamplesPerChannel);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FinishDelegate(IntPtr enc);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeDelegate(IntPtr enc);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr LastErrorDelegate(IntPtr enc);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr VersionDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AbiDelegate();

    private static OpenDelegate _open;
    private static WriteVideoDelegate _writeVideo;
    private static WriteAudioDelegate _writeAudio;
    private static FinishDelegate _finish;
    private static FreeDelegate _free;
    private static LastErrorDelegate _lastError;
    private static VersionDelegate _version;
    private static AbiDelegate _abi;

    // ABI the managed side was written against; must match the native build.
    private const int ExpectedAbi = 1;

    public static bool Available { get; private set; }
    public static string LoadError { get; private set; }
    public static string Version { get; private set; }

    // Idempotent. Returns true if the encoder is usable after the call.
    public static bool Initialize(string libraryPath) {
        if(Available) {
            return true;
        }
        try {
            IntPtr handle = KorenNativeLibrary.Open(libraryPath);

            _open = KorenNativeLibrary.GetExport<OpenDelegate>(handle, "koren_enc_open");
            _writeVideo = KorenNativeLibrary.GetExport<WriteVideoDelegate>(handle, "koren_enc_write_video");
            _writeAudio = KorenNativeLibrary.GetExport<WriteAudioDelegate>(handle, "koren_enc_write_audio");
            _finish = KorenNativeLibrary.GetExport<FinishDelegate>(handle, "koren_enc_finish");
            _free = KorenNativeLibrary.GetExport<FreeDelegate>(handle, "koren_enc_free");
            _lastError = KorenNativeLibrary.GetExport<LastErrorDelegate>(handle, "koren_enc_last_error");
            _version = KorenNativeLibrary.GetExport<VersionDelegate>(handle, "koren_enc_version");
            _abi = KorenNativeLibrary.GetExport<AbiDelegate>(handle, "koren_enc_abi");

            int abi = _abi();
            if(abi != ExpectedAbi) {
                throw new InvalidOperationException(
                    $"native encoder ABI {abi} != expected {ExpectedAbi}; rebuild native/koren_encoder");
            }

            Version = Marshal.PtrToStringAnsi(_version());
            Available = true;
            LoadError = null;
            MainCore.Log.Msg($"[Recorder] native encoder loaded: {Version}");
        } catch(Exception e) {
            Available = false;
            LoadError = e.Message;
            MainCore.Log.Wrn($"[Recorder] native encoder unavailable: {e.Message}");
        }
        return Available;
    }

    public struct Config {
        public string OutputPath { get; set; }
        public string VideoCodec { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Fps { get; set; }
        public long VideoBitrate { get; set; }
        public int Crf { get; set; }
        public int Gop { get; set; }
        public bool FlipVertical { get; set; }
        public int AudioChannels { get; set; }
        public int AudioSampleRate { get; set; }
        public long AudioBitrate { get; set; }
        public string AudioCodec { get; set; }
    }

    private IntPtr handle;

    public string LastError =>
        handle == IntPtr.Zero ? "encoder not open" : Marshal.PtrToStringAnsi(_lastError(handle));

    // Throws on failure with the native error message attached.
    public NativeEncoder(in Config cfg) {
        if(!Available) {
            throw new InvalidOperationException("native encoder library not loaded");
        }

        var pinned = new List<IntPtr>();
        IntPtr Utf8(string s) {
            if(s == null) {
                return IntPtr.Zero;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            IntPtr p = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            Marshal.WriteByte(p, bytes.Length, 0);
            pinned.Add(p);
            return p;
        }

        try {
            var native = new NativeConfig {
                output_path = Utf8(cfg.OutputPath),
                video_codec = Utf8(cfg.VideoCodec),
                width = cfg.Width,
                height = cfg.Height,
                fps = cfg.Fps,
                video_bitrate = cfg.VideoBitrate,
                crf = cfg.Crf,
                gop = cfg.Gop,
                flip_vertical = cfg.FlipVertical ? 1 : 0,
                audio_channels = cfg.AudioChannels,
                audio_sample_rate = cfg.AudioSampleRate,
                audio_bitrate = cfg.AudioBitrate,
                audio_codec = Utf8(cfg.AudioCodec),
            };

            byte[] err = new byte[512];
            handle = _open(ref native, err, err.Length);
            if(handle == IntPtr.Zero) {
                throw new InvalidOperationException($"encoder open failed: {ReadCString(err)}");
            }
        } finally {
            // The native side copies everything it needs out of the strings
            // during open, so they're safe to release now.
            foreach(IntPtr p in pinned) {
                Marshal.FreeHGlobal(p);
            }
        }
    }

    // rgba must be exactly Width*Height*4 bytes. Returns false on failure.
    public bool WriteVideo(byte[] rgba, int length) =>
        handle != IntPtr.Zero && _writeVideo(handle, rgba, length) == 0;

    // interleaved holds nbSamplesPerChannel*channels floats. Returns false on failure.
    public bool WriteAudio(float[] interleaved, int nbSamplesPerChannel) =>
        handle != IntPtr.Zero && _writeAudio(handle, interleaved, nbSamplesPerChannel) == 0;

    public bool Finish() => handle != IntPtr.Zero && _finish(handle) == 0;

    public void Dispose() {
        if(handle != IntPtr.Zero) {
            _free(handle);
            handle = IntPtr.Zero;
        }
    }

    private static string ReadCString(byte[] buf) {
        int n = Array.IndexOf<byte>(buf, 0);
        if(n < 0) {
            n = buf.Length;
        }
        return Encoding.UTF8.GetString(buf, 0, n);
    }
}
