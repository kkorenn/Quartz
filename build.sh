#!/usr/bin/env bash
# KorenResourcePack v2 build script.
# Usage: ./build.sh [Config]
#   Config: Debug (default) | Release | Debug_IL2CPP | Release_IL2CPP
# Builds both projects and auto-installs into the game (UserLibs + Mods + UserData/Koren).
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

# --- Build (PostBuild targets auto-install into the game) ---
echo ">> building Koren.slnx ($CONFIG)..."
dotnet build Koren.slnx -c "$CONFIG"

echo ">> done. Installed: Mods/Koren.Loader.ML.dll, UserLibs/Koren.dll, UserData/Koren/*"
