/*
 * Standalone correctness harness for koren_encoder. Synthesises a moving
 * gradient (RGBA, bottom-up like Unity) plus a 440 Hz sine and muxes a short
 * mp4. Exit 0 on success; the caller (build.sh) then runs ffprobe to confirm
 * the stream params landed as configured.
 *
 *   usage: koren_encoder_test <out.mp4> [seconds]
 */
#include "koren_encoder.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

int main(int argc, char** argv) {
    const char* out = argc > 1 ? argv[1] : "test.mp4";
    int seconds = argc > 2 ? atoi(argv[2]) : 2;

    const int W = 640, H = 360, FPS = 30, SR = 48000, CH = 2;

    KorenEncoderConfig cfg; memset(&cfg, 0, sizeof(cfg));
    cfg.output_path = out;
    cfg.video_codec = "libx264";
    cfg.width = W; cfg.height = H; cfg.fps = FPS;
    cfg.video_bitrate = 0; cfg.crf = 20; cfg.gop = 0;
    cfg.flip_vertical = 1;            /* exercise the Unity bottom-up path */
    cfg.audio_channels = CH; cfg.audio_sample_rate = SR; cfg.audio_bitrate = 0;
    cfg.audio_codec = "aac";

    char err[512] = {0};
    KorenEncoder* e = koren_enc_open(&cfg, err, sizeof(err));
    if (!e) { fprintf(stderr, "open failed: %s\n", err); return 1; }
    printf("%s\n", koren_enc_version());

    unsigned char* rgba = (unsigned char*)malloc((size_t)W * H * 4);
    int total = FPS * seconds;
    /* audio cadence: SR/FPS samples per video frame */
    int spf = SR / FPS;
    float* pcm = (float*)malloc(sizeof(float) * spf * CH);
    double phase = 0.0, dphi = 2.0 * M_PI * 440.0 / SR;

    for (int f = 0; f < total; f++) {
        for (int y = 0; y < H; y++) {
            for (int x = 0; x < W; x++) {
                unsigned char* p = rgba + ((size_t)y * W + x) * 4;
                p[0] = (unsigned char)((x + f * 4) & 0xff);
                p[1] = (unsigned char)((y + f * 2) & 0xff);
                p[2] = (unsigned char)((f * 6) & 0xff);
                p[3] = 255;
            }
        }
        if (koren_enc_write_video(e, rgba, W * H * 4) < 0) {
            fprintf(stderr, "video write: %s\n", koren_enc_last_error(e)); return 2;
        }
        for (int s = 0; s < spf; s++) {
            float v = (float)(0.2 * sin(phase)); phase += dphi;
            pcm[s * CH + 0] = v;
            pcm[s * CH + 1] = v;
        }
        if (koren_enc_write_audio(e, pcm, spf) < 0) {
            fprintf(stderr, "audio write: %s\n", koren_enc_last_error(e)); return 3;
        }
    }

    if (koren_enc_finish(e) < 0) {
        fprintf(stderr, "finish: %s\n", koren_enc_last_error(e)); return 4;
    }
    koren_enc_free(e);
    free(rgba); free(pcm);
    printf("wrote %s (%d frames)\n", out, total);
    return 0;
}
