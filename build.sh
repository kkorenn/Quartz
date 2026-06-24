#!/usr/bin/env bash
# Quartz build script.
# Usage: ./build.sh [Config]
#   Config: Debug (default) | Release | Debug_IL2CPP | Release_IL2CPP
# Builds Quartz.dll, auto-installs into the game (Mods + UserData/Quartz),
# and writes dist/Quartz.zip.
set -euo pipefail

cd "$(dirname "$0")"

CONFIG="${1:-Debug}"

# --- Locate game install (auto-detect, override with GAMEPATH env var) ---
detect_gamepath() {
    if [[ -n "${GAMEPATH:-}" ]]; then
        echo "$GAMEPATH"; return
    fi
    local candidates=(
        "$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice"  # macOS
        "$HOME/.local/share/Steam/steamapps/common/A Dance of Fire and Ice"                 # Linux
        "/c/Program Files (x86)/Steam/steamapps/common/A Dance of Fire and Ice"             # Windows (git-bash)
        "C:/Program Files (x86)/Steam/steamapps/common/A Dance of Fire and Ice"
    )
    for c in "${candidates[@]}"; do
        [[ -d "$c" ]] && { echo "$c"; return; }
    done
    return 1
}

# --- Resolve GameData (where Managed/ lives — differs per OS) ---
resolve_gamedata() {
    local gp="$1"
    if [[ -d "$gp/ADanceOfFireAndIce.app/Contents/Resources/Data/Managed" ]]; then
        echo "ADanceOfFireAndIce.app/Contents/Resources/Data"; return
    fi
    if [[ -d "$gp/A Dance of Fire and Ice_Data/Managed" ]]; then
        echo "A Dance of Fire and Ice_Data"; return
    fi
    return 1
}

# --- Generate Directory.Build.props if absent ---
if [[ ! -f Directory.Build.props ]]; then
    echo ">> Directory.Build.props missing — generating..."
    GP="$(detect_gamepath)" || { echo "ERROR: game install not found. Set GAMEPATH env var."; exit 1; }
    GD="$(resolve_gamedata "$GP")" || { echo "ERROR: Managed/ folder not found under $GP"; exit 1; }
    cat > Directory.Build.props <<EOF
<Project>
    <PropertyGroup>
        <GamePath>$GP</GamePath>
        <GameData>$GD</GameData>
    </PropertyGroup>
</Project>
EOF
    echo ">> wrote Directory.Build.props (GamePath=$GP)"
fi

# --- Verify MelonLoader present ---
GP_CHECK="$(grep -oE '<GamePath>[^<]+' Directory.Build.props | sed 's/<GamePath>//')"
if [[ ! -f "$GP_CHECK/MelonLoader/net35/MelonLoader.dll" ]]; then
    echo "WARNING: MelonLoader not found at $GP_CHECK/MelonLoader — install MelonLoader first."
fi

# --- Native Windows encoder (cross-compiled, idempotent) ---
# The csproj bundles native/dist/** into the zips, so produce the Windows encoder
# here too (one zip serves every OS). Cross-compiling needs mingw-w64 and a ~75MB
# FFmpeg download, so it only runs when the output is missing (or FORCE_NATIVE_WIN=1)
# and degrades to a warning when mingw isn't installed.
WIN_DLL="native/dist/win/koren_encoder.dll"
if [[ "${FORCE_NATIVE_WIN:-0}" == "1" || ! -f "$WIN_DLL" ]]; then
    if command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1; then
        echo ">> building Windows native encoder (native/build_win.sh)..."
        ./native/build_win.sh
    else
        echo "WARNING: mingw-w64 not found — skipping Windows native encoder."
        echo "         install it (brew install mingw-w64); Windows render needs native/dist/win/."
    fi
else
    echo ">> Windows native encoder present ($WIN_DLL) — skipping (FORCE_NATIVE_WIN=1 to rebuild)."
fi

# --- Build (PostBuild targets auto-install into the game) ---
# Second arg picks the loader target(s): ML | UMM | both (default both).
TARGET="${2:-both}"

build_one() {
    local loader="$1"
    echo ">> building Quartz/Quartz.csproj ($CONFIG, LoaderTarget=$loader)..."
    dotnet build Quartz/Quartz.csproj -c "$CONFIG" -p:AutoInstall=true -p:LoaderTarget="$loader"
}

case "$TARGET" in
    ML)   build_one ML ;;
    UMM)  build_one UMM ;;
    both) build_one ML; build_one UMM ;;
    *)    echo "ERROR: unknown target '$TARGET' (use ML | UMM | both)"; exit 1 ;;
esac

echo ">> done."
[[ "$TARGET" == "ML"  || "$TARGET" == "both" ]] && echo ">> MelonLoader:     Mods/Quartz.dll + UserData/Quartz/* — dist/Quartz.zip"
[[ "$TARGET" == "UMM" || "$TARGET" == "both" ]] && echo ">> UnityModManager: UMMMods/Quartz (or Mods/Quartz) — dist/QuartzUmm.zip"
