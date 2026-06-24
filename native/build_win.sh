#!/usr/bin/env bash
# Cross-compile the koren_encoder Windows DLL from macOS/Linux and stage a
# self-contained native/dist/win/ (the wrapper + the FFmpeg DLLs it needs) for
# the Quartz csproj to bundle into the mod zip.
#
# The mod loads koren_encoder.dll (our wrapper around FFmpeg's C API), so Windows
# needs a COMPILED wrapper — a bare ffmpeg.dll isn't enough. This builds it with
# mingw-w64 against a prebuilt Windows FFmpeg (BtbN, hosted on GitHub) and copies
# the 5 FFmpeg runtime DLLs next to it. BtbN's GPL "shared" build statically links
# the codecs (x264/x265/...) into the FFmpeg DLLs, so no separate codec DLLs are
# needed — matching the GPL codec set the macOS bundle ships.
#
# At runtime QuartzNativeLibrary loads koren_encoder.dll with
# LOAD_WITH_ALTERED_SEARCH_PATH, so the sibling FFmpeg DLLs in native/win/ resolve.
#
# Requires: mingw-w64 (`brew install mingw-w64` / apt `gcc-mingw-w64-x86-64`),
# curl, unzip. Run on macOS or Linux. FFmpeg version is pinned to match the
# avcodec/avformat/avutil majors the encoder source targets (8.x: 62/62/60).
set -euo pipefail
cd "$(dirname "$0")"

CC="${MINGW_CC:-x86_64-w64-mingw32-gcc}"
OBJDUMP="${MINGW_OBJDUMP:-x86_64-w64-mingw32-objdump}"
command -v "$CC" >/dev/null || { echo "ERROR: $CC not found — install mingw-w64 (brew install mingw-w64)"; exit 1; }

# BtbN release asset (GPL shared, FFmpeg 8.1 — avcodec-62/avformat-62/avutil-60).
FF_ZIP="ffmpeg-n8.1-latest-win64-gpl-shared-8.1.zip"
FF_URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/${FF_ZIP}"
FF_DLLS=(avcodec-62 avformat-62 avutil-60 swscale-9 swresample-6)

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
DIST="dist/win"

echo ">> fetching Windows FFmpeg ($FF_ZIP)..."
curl -fSL -o "$WORK/ff.zip" "$FF_URL"
unzip -q "$WORK/ff.zip" -d "$WORK"
FF="$(echo "$WORK"/ffmpeg-*)"
[ -d "$FF/include" ] && [ -d "$FF/lib" ] && [ -d "$FF/bin" ] || { echo "ERROR: unexpected FFmpeg layout"; exit 1; }

echo ">> cross-compiling koren_encoder.dll..."
rm -rf "$DIST"; mkdir -p "$DIST"
"$CC" -shared -O2 -DNDEBUG \
    -o "$DIST/koren_encoder.dll" koren_encoder/koren_encoder.c \
    -I"$FF/include" -L"$FF/lib" \
    -lavformat -lavcodec -lavutil -lswscale -lswresample \
    -static-libgcc -Wl,-Bstatic -lwinpthread -Wl,-Bdynamic

echo ">> bundling FFmpeg runtime DLLs..."
for d in "${FF_DLLS[@]}"; do cp -f "$FF/bin/$d.dll" "$DIST/"; done

echo ">> verifying dependency closure (non-system deps must all be in-bundle)..."
missing=0
for f in "$DIST"/*.dll; do
    while read -r dep; do
        [ -z "$dep" ] && continue
        # System DLLs (lower-cased match): present on every Windows install.
        case "$(echo "$dep" | tr '[:upper:]' '[:lower:]')" in
            kernel32.dll|ntdll.dll|msvcrt.dll|ucrtbase.dll|api-ms-win-*) continue ;;
            user32.dll|gdi32.dll|shell32.dll|ole32.dll|advapi32.dll|rpcrt4.dll) continue ;;
            crypt32.dll|ncrypt.dll|bcrypt.dll|secur32.dll|ws2_32.dll|iphlpapi.dll) continue ;;
            cfgmgr32.dll|usp10.dll|dwrite.dll|d2d1.dll|mfplat.dll|mf.dll|mfreadwrite.dll) continue ;;
        esac
        # Anything else must be a sibling we bundled.
        if [ ! -f "$DIST/$dep" ]; then echo "   MISSING: $(basename "$f") -> $dep"; missing=1; fi
    done < <("$OBJDUMP" -p "$f" | awk '/DLL Name:/{print $3}')
done
[ "$missing" = 0 ] || { echo "ERROR: unresolved non-system deps; bundle is incomplete"; exit 1; }

echo ">> done -> $DIST ($(ls "$DIST" | wc -l | tr -d ' ') files)"
