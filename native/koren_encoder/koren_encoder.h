/*
 * koren_encoder — a thin, stable C ABI over FFmpeg's libav* for the
 * KorenResourcePack ADOFAI renderer.
 *
 * The mod captures frames offline (Time.captureDeltaTime), so the C# side just
 * hands raw RGBA frames + interleaved float PCM to this library, which muxes an
 * H.264/AAC mp4. All of libav's version-sensitive struct/ABI surface is isolated
 * here behind ~7 functions; C# P/Invokes only these.
 *
 * Threading: a KorenEncoder is single-threaded. Create, write, finish, free from
 * one thread (the capture loop). Do not share a handle across threads.
 *
 * Return convention: functions returning int give 0 on success and a negative
 * value on failure; call koren_enc_last_error() for a human-readable reason.
 */
#ifndef KOREN_ENCODER_H
#define KOREN_ENCODER_H

#include <stddef.h>

#if defined(_WIN32)
#  define KOREN_API __declspec(dllexport)
#else
#  define KOREN_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct KorenEncoder KorenEncoder;

/* All strings are UTF-8. Pointers are borrowed for the duration of the call. */
typedef struct {
    const char* output_path;   /* container inferred from extension (.mp4) */
    const char* video_codec;   /* encoder name; NULL/"" -> "libx264" */

    int   width;
    int   height;
    int   fps;                 /* frames per second of the output timeline */

    long long video_bitrate;   /* target bits/sec; 0 -> constant-quality (crf) */
    int   crf;                  /* used when video_bitrate==0; 0 -> 18 */
    int   gop;                  /* keyframe interval in frames; 0 -> fps*2 */

    /* Unity reads textures bottom-up; set 1 to flip rows during conversion. */
    int   flip_vertical;

    /* Audio. audio_channels==0 disables the audio stream entirely. */
    int   audio_channels;
    int   audio_sample_rate;   /* e.g. 48000 */
    long long audio_bitrate;   /* 0 -> 192000 */
    const char* audio_codec;   /* NULL/"" -> "aac" */
} KorenEncoderConfig;

/*
 * Open an encoder. On failure returns NULL and, if err is non-NULL, writes a
 * NUL-terminated message into err (capacity err_len).
 */
KOREN_API KorenEncoder* koren_enc_open(const KorenEncoderConfig* cfg, char* err, int err_len);

/* rgba: width*height*4 bytes, row-major, 8-bit R,G,B,A. len must equal that. */
KOREN_API int koren_enc_write_video(KorenEncoder* e, const unsigned char* rgba, int len);

/* interleaved: nb_samples_per_channel * channels floats in [-1,1]. */
KOREN_API int koren_enc_write_audio(KorenEncoder* e, const float* interleaved, int nb_samples_per_channel);

/* Flush encoders, write the trailer. Call once; the handle is still freed via koren_enc_free. */
KOREN_API int koren_enc_finish(KorenEncoder* e);

KOREN_API void koren_enc_free(KorenEncoder* e);

/* Last error for this handle (or a static message if e is NULL). Never NULL. */
KOREN_API const char* koren_enc_last_error(KorenEncoder* e);

/* "koren_encoder N / libavcodec X.Y.Z" — for logging which build is loaded. */
KOREN_API const char* koren_enc_version(void);

/* ABI revision; bump on any incompatible signature/struct change. */
#define KOREN_ENC_ABI 1
KOREN_API int koren_enc_abi(void);

#ifdef __cplusplus
}
#endif

#endif /* KOREN_ENCODER_H */
