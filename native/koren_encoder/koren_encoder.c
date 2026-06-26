/*
 * koren_encoder implementation. See koren_encoder.h for the contract.
 *
 * Pipeline:
 *   video: RGBA (caller) --sws_scale--> YUV420P --x264--> mp4 video stream
 *   audio: FLT interleaved (caller) --swr--> FLTP --fifo--> AAC frames --> mp4 audio stream
 *
 * The audio FIFO exists because AAC wants fixed 1024-sample frames while the
 * caller hands us whatever a capture frame's worth of samples happens to be
 * (e.g. 800 @ 48k/60fps). We buffer and emit full frames; the tail is flushed
 * at finish.
 */
#include "koren_encoder.h"

#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/avutil.h>
#include <libavutil/opt.h>
#include <libavutil/imgutils.h>
#include <libavutil/audio_fifo.h>
#include <libavutil/channel_layout.h>
#include <libswscale/swscale.h>
#include <libswresample/swresample.h>

#include <stdio.h>
#include <string.h>
#include <stdlib.h>

struct KorenEncoder {
    AVFormatContext* fmt;

    /* video */
    AVCodecContext* vc;
    AVStream*       vst;
    struct SwsContext* sws;
    AVFrame*        vframe;     /* reusable YUV420P frame */
    int64_t         vpts;       /* next video pts, in frames */
    int             width, height, fps, flip;

    /* audio (may be absent) */
    AVCodecContext* ac;
    AVStream*       ast;
    SwrContext*     swr;
    AVAudioFifo*    fifo;
    AVFrame*        aframe;     /* reusable encode frame, ac->frame_size samples */
    int64_t         apts;       /* next audio pts, in samples */
    int             channels;

    AVPacket*       pkt;
    int             header_written;

    char err[512];
};

static void set_err(KorenEncoder* e, const char* fmt, ...) {
    if (!e) return;
    va_list ap; va_start(ap, fmt);
    vsnprintf(e->err, sizeof(e->err), fmt, ap);
    va_end(ap);
}

static void set_err_av(KorenEncoder* e, const char* what, int code) {
    char buf[256];
    av_strerror(code, buf, sizeof(buf));
    set_err(e, "%s: %s", what, buf);
}

/* Drain a codec's ready packets into the muxer. Returns 0 or negative. */
static int drain(KorenEncoder* e, AVCodecContext* c, AVStream* st) {
    for (;;) {
        int ret = avcodec_receive_packet(c, e->pkt);
        if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) return 0;
        if (ret < 0) { set_err_av(e, "receive_packet", ret); return ret; }

        e->pkt->stream_index = st->index;
        av_packet_rescale_ts(e->pkt, c->time_base, st->time_base);
        ret = av_interleaved_write_frame(e->fmt, e->pkt);
        av_packet_unref(e->pkt);
        if (ret < 0) { set_err_av(e, "write_frame", ret); return ret; }
    }
}

static int encode_frame(KorenEncoder* e, AVCodecContext* c, AVStream* st, AVFrame* f) {
    int ret = avcodec_send_frame(c, f);
    if (ret < 0) { set_err_av(e, "send_frame", ret); return ret; }
    return drain(e, c, st);
}

/* Pull one ac->frame_size (or `count`) chunk out of the FIFO and encode it. */
static int encode_audio_chunk(KorenEncoder* e, int count) {
    AVFrame* f = e->aframe;
    int ret = av_frame_make_writable(f);
    if (ret < 0) { set_err_av(e, "audio make_writable", ret); return ret; }

    f->nb_samples = count;
    ret = av_audio_fifo_read(e->fifo, (void**)f->data, count);
    if (ret < count) { set_err(e, "audio fifo underrun (%d<%d)", ret, count); return -1; }

    f->pts = e->apts;
    e->apts += count;
    return encode_frame(e, e->ac, e->ast, f);
}

/* ------------------------------------------------------------------ open ---- */

static int open_video(KorenEncoder* e, const KorenEncoderConfig* cfg) {
    const char* name = (cfg->video_codec && cfg->video_codec[0]) ? cfg->video_codec : "libx264";
    const AVCodec* codec = avcodec_find_encoder_by_name(name);
    if (!codec) { set_err(e, "video encoder '%s' not found", name); return -1; }

    e->vst = avformat_new_stream(e->fmt, NULL);
    if (!e->vst) { set_err(e, "alloc video stream failed"); return -1; }

    e->vc = avcodec_alloc_context3(codec);
    if (!e->vc) { set_err(e, "alloc video ctx failed"); return -1; }

    e->vc->width  = cfg->width;
    e->vc->height = cfg->height;
    e->vc->pix_fmt = AV_PIX_FMT_YUV420P;
    e->vc->time_base = (AVRational){ 1, cfg->fps };
    e->vc->framerate = (AVRational){ cfg->fps, 1 };
    e->vc->gop_size = cfg->gop > 0 ? cfg->gop : cfg->fps * 2;
    e->vst->time_base = e->vc->time_base;

    if (cfg->video_bitrate > 0) {
        e->vc->bit_rate = cfg->video_bitrate;
    } else {
        /* constant-quality; only x264/x265 understand crf, harmless elsewhere */
        char crf[8];
        snprintf(crf, sizeof(crf), "%d", cfg->crf > 0 ? cfg->crf : 18);
        av_opt_set(e->vc->priv_data, "crf", crf, 0);
    }
    /* Offline render: bias every codec hard toward encode throughput. Each encoder
       family takes a different "go fast" knob, so dispatch on the encoder name rather
       than blasting one string at all of them (the old code set x264's "ultrafast" on
       everything, which SVT-AV1 and VideoToolbox silently rejected -> they ran at their
       slow defaults). */
    if (!strcmp(name, "libx264") || !strcmp(name, "libx265")) {
        /* Fastest CPU preset. Frame threading (below) carries the rest; deliberately
           NOT "zerolatency", which disables frame threading and hurts throughput. */
        av_opt_set(e->vc->priv_data, "preset", "ultrafast", 0);
    } else if (!strcmp(name, "libsvtav1")) {
        /* SVT-AV1 preset is numeric 0 (slowest) .. 13 (fastest). 12 sits near the top
           of the speed range and still produces a clean, small file. */
        av_opt_set(e->vc->priv_data, "preset", "12", 0);
    } else if (strstr(name, "videotoolbox")) {
        /* Apple hardware encoder: prioritise speed over latency/quality tuning. */
        av_opt_set(e->vc->priv_data, "realtime", "1", 0);
        av_opt_set(e->vc->priv_data, "prio_speed", "1", 0);
    }

    /* Multithreaded encode across all logical cores — the dominant offline-render speed
       win. x264/x265 parallelise over frames AND slices; SVT-AV1 runs its own thread
       pool (harmless); hardware encoders ignore this. Frame threading also overlaps
       encoding with the NEXT frame's capture, because avcodec_send_frame returns before
       the frame is fully encoded and drain() only pulls packets that are already ready. */
    e->vc->thread_count = 0;                                  /* 0 = detect cores */
    e->vc->thread_type  = FF_THREAD_FRAME | FF_THREAD_SLICE;

    if (e->fmt->oformat->flags & AVFMT_GLOBALHEADER)
        e->vc->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

    int ret = avcodec_open2(e->vc, codec, NULL);
    if (ret < 0) { set_err_av(e, "open video codec", ret); return ret; }

    ret = avcodec_parameters_from_context(e->vst->codecpar, e->vc);
    if (ret < 0) { set_err_av(e, "video parameters", ret); return ret; }

    /* Same in/out dimensions (no spatial scaling), so the only filtering is chroma
       subsampling — SWS_FAST_BILINEAR is the cheapest path and visually identical here. */
    e->sws = sws_getContext(cfg->width, cfg->height, AV_PIX_FMT_RGBA,
                            cfg->width, cfg->height, AV_PIX_FMT_YUV420P,
                            SWS_FAST_BILINEAR, NULL, NULL, NULL);
    if (!e->sws) { set_err(e, "sws_getContext failed"); return -1; }

    e->vframe = av_frame_alloc();
    if (!e->vframe) { set_err(e, "alloc vframe failed"); return -1; }
    e->vframe->format = AV_PIX_FMT_YUV420P;
    e->vframe->width  = cfg->width;
    e->vframe->height = cfg->height;
    ret = av_frame_get_buffer(e->vframe, 0);
    if (ret < 0) { set_err_av(e, "vframe buffer", ret); return ret; }

    return 0;
}

static int open_audio(KorenEncoder* e, const KorenEncoderConfig* cfg) {
    const char* name = (cfg->audio_codec && cfg->audio_codec[0]) ? cfg->audio_codec : "aac";
    const AVCodec* codec = avcodec_find_encoder_by_name(name);
    if (!codec) { set_err(e, "audio encoder '%s' not found", name); return -1; }

    e->ast = avformat_new_stream(e->fmt, NULL);
    if (!e->ast) { set_err(e, "alloc audio stream failed"); return -1; }

    e->ac = avcodec_alloc_context3(codec);
    if (!e->ac) { set_err(e, "alloc audio ctx failed"); return -1; }

    e->ac->sample_fmt  = AV_SAMPLE_FMT_FLTP;   /* what aac wants */
    e->ac->sample_rate = cfg->audio_sample_rate;
    e->ac->bit_rate    = cfg->audio_bitrate > 0 ? cfg->audio_bitrate : 192000;
    av_channel_layout_default(&e->ac->ch_layout, cfg->audio_channels);
    e->ac->time_base = (AVRational){ 1, cfg->audio_sample_rate };
    e->ast->time_base = e->ac->time_base;

    if (e->fmt->oformat->flags & AVFMT_GLOBALHEADER)
        e->ac->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

    int ret = avcodec_open2(e->ac, codec, NULL);
    if (ret < 0) { set_err_av(e, "open audio codec", ret); return ret; }

    ret = avcodec_parameters_from_context(e->ast->codecpar, e->ac);
    if (ret < 0) { set_err_av(e, "audio parameters", ret); return ret; }

    /* input from Unity: interleaved float, same rate, default layout */
    AVChannelLayout in_layout;
    av_channel_layout_default(&in_layout, cfg->audio_channels);
    ret = swr_alloc_set_opts2(&e->swr,
            &e->ac->ch_layout, e->ac->sample_fmt, e->ac->sample_rate,
            &in_layout, AV_SAMPLE_FMT_FLT, cfg->audio_sample_rate,
            0, NULL);
    av_channel_layout_uninit(&in_layout);
    if (ret < 0) { set_err_av(e, "swr alloc", ret); return ret; }
    ret = swr_init(e->swr);
    if (ret < 0) { set_err_av(e, "swr init", ret); return ret; }

    e->fifo = av_audio_fifo_alloc(e->ac->sample_fmt, cfg->audio_channels, 1);
    if (!e->fifo) { set_err(e, "alloc audio fifo failed"); return -1; }

    /* some encoders report frame_size 0 meaning "any size"; pick a sane block */
    if (e->ac->frame_size <= 0) e->ac->frame_size = 1024;

    e->aframe = av_frame_alloc();
    if (!e->aframe) { set_err(e, "alloc aframe failed"); return -1; }
    e->aframe->format = e->ac->sample_fmt;
    e->aframe->nb_samples = e->ac->frame_size;
    e->aframe->sample_rate = e->ac->sample_rate;
    ret = av_channel_layout_copy(&e->aframe->ch_layout, &e->ac->ch_layout);
    if (ret < 0) { set_err_av(e, "aframe layout", ret); return ret; }
    ret = av_frame_get_buffer(e->aframe, 0);
    if (ret < 0) { set_err_av(e, "aframe buffer", ret); return ret; }

    e->channels = cfg->audio_channels;
    return 0;
}

KOREN_API KorenEncoder* koren_enc_open(const KorenEncoderConfig* cfg, char* err, int err_len) {
    if (!cfg || !cfg->output_path || cfg->width <= 0 || cfg->height <= 0 || cfg->fps <= 0) {
        if (err && err_len > 0) snprintf(err, err_len, "invalid config");
        return NULL;
    }
    /* libx264/yuv420p require even dimensions */
    if ((cfg->width & 1) || (cfg->height & 1)) {
        if (err && err_len > 0) snprintf(err, err_len, "width/height must be even (got %dx%d)", cfg->width, cfg->height);
        return NULL;
    }

    KorenEncoder* e = (KorenEncoder*)calloc(1, sizeof(KorenEncoder));
    if (!e) { if (err && err_len > 0) snprintf(err, err_len, "out of memory"); return NULL; }
    e->width = cfg->width; e->height = cfg->height; e->fps = cfg->fps; e->flip = cfg->flip_vertical;

    int ret = avformat_alloc_output_context2(&e->fmt, NULL, NULL, cfg->output_path);
    if (ret < 0 || !e->fmt) { set_err_av(e, "alloc output context", ret); goto fail; }

    if (open_video(e, cfg) < 0) goto fail;
    if (cfg->audio_channels > 0 && open_audio(e, cfg) < 0) goto fail;

    e->pkt = av_packet_alloc();
    if (!e->pkt) { set_err(e, "alloc packet failed"); goto fail; }

    if (!(e->fmt->oformat->flags & AVFMT_NOFILE)) {
        ret = avio_open(&e->fmt->pb, cfg->output_path, AVIO_FLAG_WRITE);
        if (ret < 0) { set_err_av(e, "open output file", ret); goto fail; }
    }

    ret = avformat_write_header(e->fmt, NULL);
    if (ret < 0) { set_err_av(e, "write header", ret); goto fail; }
    e->header_written = 1;

    return e;

fail:
    if (err && err_len > 0) snprintf(err, err_len, "%s", e->err[0] ? e->err : "open failed");
    koren_enc_free(e);
    return NULL;
}

/* ----------------------------------------------------------------- write ---- */

KOREN_API int koren_enc_write_video(KorenEncoder* e, const unsigned char* rgba, int len) {
    if (!e || !e->vc) { set_err(e, "no video stream"); return -1; }
    if (!rgba || len != e->width * e->height * 4) {
        set_err(e, "bad video buffer (len=%d expected=%d)", len, e->width * e->height * 4);
        return -1;
    }

    int ret = av_frame_make_writable(e->vframe);
    if (ret < 0) { set_err_av(e, "vframe make_writable", ret); return ret; }

    const uint8_t* src[4]; int stride[4] = {0,0,0,0};
    int rowbytes = e->width * 4;
    if (e->flip) { src[0] = rgba + (size_t)(e->height - 1) * rowbytes; stride[0] = -rowbytes; }
    else         { src[0] = rgba;                                       stride[0] =  rowbytes; }
    src[1] = src[2] = src[3] = NULL;

    sws_scale(e->sws, src, stride, 0, e->height, e->vframe->data, e->vframe->linesize);
    e->vframe->pts = e->vpts++;
    return encode_frame(e, e->vc, e->vst, e->vframe);
}

KOREN_API int koren_enc_write_audio(KorenEncoder* e, const float* interleaved, int nb) {
    if (!e || !e->ac) { set_err(e, "no audio stream"); return -1; }
    if (!interleaved || nb <= 0) return 0;

    /* convert this chunk FLT->FLTP into a scratch buffer, push to the FIFO */
    uint8_t** conv = NULL;
    int ret = av_samples_alloc_array_and_samples(&conv, NULL, e->channels, nb, e->ac->sample_fmt, 0);
    if (ret < 0) { set_err_av(e, "samples_alloc", ret); return ret; }

    const uint8_t* in[1] = { (const uint8_t*)interleaved };
    int got = swr_convert(e->swr, conv, nb, in, nb);
    if (got < 0) { set_err_av(e, "swr_convert", got); av_freep(&conv[0]); av_freep(&conv); return got; }

    if (got > 0) {
        if (av_audio_fifo_write(e->fifo, (void**)conv, got) < got) {
            set_err(e, "audio fifo write failed");
            av_freep(&conv[0]); av_freep(&conv);
            return -1;
        }
    }
    av_freep(&conv[0]);
    av_freep(&conv);

    while (av_audio_fifo_size(e->fifo) >= e->ac->frame_size) {
        ret = encode_audio_chunk(e, e->ac->frame_size);
        if (ret < 0) return ret;
    }
    return 0;
}

/* ---------------------------------------------------------------- finish ---- */

KOREN_API int koren_enc_finish(KorenEncoder* e) {
    if (!e || !e->fmt) return -1;
    int ret;

    if (e->ac) {
        /* flush resampler's internal buffer */
        for (;;) {
            uint8_t** conv = NULL;
            int cap = 4096;
            ret = av_samples_alloc_array_and_samples(&conv, NULL, e->channels, cap, e->ac->sample_fmt, 0);
            if (ret < 0) { set_err_av(e, "flush samples_alloc", ret); return ret; }
            int got = swr_convert(e->swr, conv, cap, NULL, 0);
            if (got > 0) av_audio_fifo_write(e->fifo, (void**)conv, got);
            av_freep(&conv[0]); av_freep(&conv);
            if (got <= 0) break;
        }
        while (av_audio_fifo_size(e->fifo) >= e->ac->frame_size) {
            ret = encode_audio_chunk(e, e->ac->frame_size);
            if (ret < 0) return ret;
        }
        int rem = av_audio_fifo_size(e->fifo);
        if (rem > 0) { ret = encode_audio_chunk(e, rem); if (ret < 0) return ret; }

        /* drain encoder */
        ret = encode_frame(e, e->ac, e->ast, NULL);
        if (ret < 0) return ret;
    }

    if (e->vc) {
        ret = encode_frame(e, e->vc, e->vst, NULL);
        if (ret < 0) return ret;
    }

    ret = av_write_trailer(e->fmt);
    if (ret < 0) { set_err_av(e, "write trailer", ret); return ret; }
    return 0;
}

KOREN_API void koren_enc_free(KorenEncoder* e) {
    if (!e) return;
    if (e->sws) sws_freeContext(e->sws);
    if (e->swr) swr_free(&e->swr);
    if (e->fifo) av_audio_fifo_free(e->fifo);
    if (e->vframe) av_frame_free(&e->vframe);
    if (e->aframe) av_frame_free(&e->aframe);
    if (e->pkt) av_packet_free(&e->pkt);
    if (e->vc) avcodec_free_context(&e->vc);
    if (e->ac) avcodec_free_context(&e->ac);
    if (e->fmt) {
        if (!(e->fmt->oformat->flags & AVFMT_NOFILE) && e->fmt->pb)
            avio_closep(&e->fmt->pb);
        avformat_free_context(e->fmt);
    }
    free(e);
}

KOREN_API const char* koren_enc_last_error(KorenEncoder* e) {
    if (!e) return "null encoder";
    return e->err[0] ? e->err : "ok";
}

/* ------------------------------------------------------- audio file decode -- */

KOREN_API float* koren_decode_audio(const char* path, int* out_sr,
                                    int* out_channels, long long* out_frames,
                                    char* err, int err_len) {
    #define FAIL(...) do { if(err && err_len > 0) snprintf(err, err_len, __VA_ARGS__); goto fail; } while(0)

    AVFormatContext* fmt = NULL;
    AVCodecContext* dc = NULL;
    SwrContext* swr = NULL;
    AVPacket* pkt = NULL;
    AVFrame* frame = NULL;
    float* buf = NULL;
    size_t cap = 0, len = 0; /* in floats */
    AVChannelLayout outlay; memset(&outlay, 0, sizeof(outlay));

    if(!path) { if(err && err_len > 0) snprintf(err, err_len, "null path"); return NULL; }

    int ret = avformat_open_input(&fmt, path, NULL, NULL);
    if(ret < 0) FAIL("open '%s' failed", path);
    if(avformat_find_stream_info(fmt, NULL) < 0) FAIL("no stream info");

    int sidx = av_find_best_stream(fmt, AVMEDIA_TYPE_AUDIO, -1, -1, NULL, 0);
    if(sidx < 0) FAIL("no audio stream");
    AVStream* st = fmt->streams[sidx];

    const AVCodec* dec = avcodec_find_decoder(st->codecpar->codec_id);
    if(!dec) FAIL("no decoder");
    dc = avcodec_alloc_context3(dec);
    if(!dc) FAIL("alloc decoder ctx");
    if(avcodec_parameters_to_context(dc, st->codecpar) < 0) FAIL("copy params");
    if(avcodec_open2(dc, dec, NULL) < 0) FAIL("open decoder");

    int sr = dc->sample_rate;
    int ch = dc->ch_layout.nb_channels > 0 ? dc->ch_layout.nb_channels : 2;
    av_channel_layout_default(&outlay, ch);

    ret = swr_alloc_set_opts2(&swr, &outlay, AV_SAMPLE_FMT_FLT, sr,
                              &dc->ch_layout, dc->sample_fmt, dc->sample_rate, 0, NULL);
    if(ret < 0 || swr_init(swr) < 0) FAIL("resampler init");

    pkt = av_packet_alloc();
    frame = av_frame_alloc();
    if(!pkt || !frame) FAIL("alloc pkt/frame");

    cap = (size_t)sr * ch * 8; /* ~8s seed */
    buf = (float*)malloc(cap * sizeof(float));
    if(!buf) FAIL("out of memory");

    /* Pull a converted block into buf, growing as needed. */
    #define DRAIN_FRAME(FR, NSAMP) do { \
        int outn = swr_get_out_samples(swr, (NSAMP)); \
        size_t need = len + (size_t)outn * ch; \
        if(need > cap) { while(need > cap) cap *= 2; \
            float* nb = (float*)realloc(buf, cap * sizeof(float)); \
            if(!nb) FAIL("out of memory"); buf = nb; } \
        uint8_t* outp = (uint8_t*)(buf + len); \
        const uint8_t** in = (FR) ? (const uint8_t**)((FR)->extended_data) : NULL; \
        int got = swr_convert(swr, &outp, outn, in, (NSAMP)); \
        if(got > 0) len += (size_t)got * ch; \
    } while(0)

    while(av_read_frame(fmt, pkt) >= 0) {
        if(pkt->stream_index == sidx && avcodec_send_packet(dc, pkt) >= 0) {
            while(avcodec_receive_frame(dc, frame) >= 0) {
                DRAIN_FRAME(frame, frame->nb_samples);
            }
        }
        av_packet_unref(pkt);
    }
    /* flush decoder */
    avcodec_send_packet(dc, NULL);
    while(avcodec_receive_frame(dc, frame) >= 0) {
        DRAIN_FRAME(frame, frame->nb_samples);
    }
    /* flush resampler */
    DRAIN_FRAME((AVFrame*)NULL, 0);

    #undef DRAIN_FRAME

    *out_sr = sr;
    *out_channels = ch;
    *out_frames = (long long)(len / ch);

    av_channel_layout_uninit(&outlay);
    av_frame_free(&frame);
    av_packet_free(&pkt);
    swr_free(&swr);
    avcodec_free_context(&dc);
    avformat_close_input(&fmt);
    return buf;

fail:
    av_channel_layout_uninit(&outlay);
    if(buf) free(buf);
    if(frame) av_frame_free(&frame);
    if(pkt) av_packet_free(&pkt);
    if(swr) swr_free(&swr);
    if(dc) avcodec_free_context(&dc);
    if(fmt) avformat_close_input(&fmt);
    return NULL;
    #undef FAIL
}

KOREN_API void koren_free_audio(float* pcm) {
    free(pcm);
}

KOREN_API const char* koren_enc_version(void) {
    static char buf[128];
    unsigned v = avcodec_version();
    snprintf(buf, sizeof(buf), "koren_encoder %d / libavcodec %u.%u.%u",
             KOREN_ENC_ABI, v >> 16, (v >> 8) & 0xff, v & 0xff);
    return buf;
}

KOREN_API int koren_enc_abi(void) { return KOREN_ENC_ABI; }
