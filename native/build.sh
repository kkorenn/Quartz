#!/usr/bin/env bash
# Build the koren_encoder native library and stage it (self-contained) under
# native/dist/<platform>/ for the Quartz csproj to bundle into the mod.
#
#   usage: ./build.sh [--no-bundle] [--no-test]
#
# Requires: cmake, a C toolchain, and FFmpeg dev libraries discoverable via
# pkg-config (libavformat/libavcodec/libavutil/libswscale/libswresample).
# On macOS: `brew install ffmpeg`. On Debian/Ubuntu: the libav*-dev packages.
set -euo pipefail
cd "$(dirname "$0")"

BUNDLE=1
TEST=1
for a in "$@"; do
    case "$a" in
        --no-bundle) BUNDLE=0 ;;
        --no-test)   TEST=0 ;;
        *) echo "unknown flag: $a" >&2; exit 2 ;;
    esac
done

SRC_DIR="koren_encoder"
BUILD_DIR="$SRC_DIR/build"

case "$(uname -s)" in
    Darwin) PLATFORM="osx";   LIBNAME="koren_encoder.dylib" ;;
    Linux)  PLATFORM="linux"; LIBNAME="koren_encoder.so" ;;
    *)      PLATFORM="win";   LIBNAME="koren_encoder.dll" ;;
esac
DIST_DIR="dist/$PLATFORM"

echo ">> configuring ($PLATFORM)..."
cmake -S "$SRC_DIR" -B "$BUILD_DIR" -DCMAKE_BUILD_TYPE=Release >/dev/null

echo ">> building..."
cmake --build "$BUILD_DIR" -j >/dev/null

if [[ "$TEST" == 1 ]]; then
    echo ">> self-test..."
    "$BUILD_DIR/koren_encoder_test" "$BUILD_DIR/selftest.mp4" 1 >/dev/null
    echo "   ok: $("$BUILD_DIR/koren_encoder_test" --version 2>/dev/null || true)"
fi

rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"

if [[ "$BUNDLE" == 1 && "$PLATFORM" == "osx" ]]; then
    echo ">> vendoring dependencies..."
    ./bundle_macos.sh "$BUILD_DIR/$LIBNAME" "$DIST_DIR"
else
    cp -f "$BUILD_DIR/$LIBNAME" "$DIST_DIR/$LIBNAME"
    echo ">> staged $LIBNAME (no bundling; relies on system FFmpeg at runtime)"
fi

echo ">> done -> $DIST_DIR"