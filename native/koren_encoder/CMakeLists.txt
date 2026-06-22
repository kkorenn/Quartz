cmake_minimum_required(VERSION 3.16)
project(koren_encoder C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_VISIBILITY_PRESET hidden)

find_package(PkgConfig REQUIRED)
pkg_check_modules(FFMPEG REQUIRED IMPORTED_TARGET
    libavformat
    libavcodec
    libavutil
    libswscale
    libswresample
)

# --- the shared library the mod loads (.dylib / .so / .dll) ---
add_library(koren_encoder SHARED koren_encoder.c)
target_link_libraries(koren_encoder PRIVATE PkgConfig::FFMPEG)
set_target_properties(koren_encoder PROPERTIES PREFIX "" OUTPUT_NAME "koren_encoder")

# On macOS, resolve bundled libav* siblings next to the dylib (see bundle step
# in build.sh) so the shipped library is self-contained.
if(APPLE)
    set_target_properties(koren_encoder PROPERTIES
        INSTALL_RPATH "@loader_path"
        BUILD_WITH_INSTALL_RPATH TRUE)
elseif(UNIX)
    set_target_properties(koren_encoder PROPERTIES
        INSTALL_RPATH "$ORIGIN"
        BUILD_WITH_INSTALL_RPATH TRUE)
endif()

# --- standalone correctness test (not shipped) ---
add_executable(koren_encoder_test test_encoder.c)
target_link_libraries(koren_encoder_test PRIVATE koren_encoder PkgConfig::FFMPEG)
