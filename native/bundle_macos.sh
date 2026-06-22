#!/usr/bin/env bash
# Vendor a macOS dylib and the full transitive closure of its non-system
# dependencies into one folder, rewriting every install name to @loader_path so
# the result loads with no Homebrew/MacPorts present. Ad-hoc re-signs each lib
# (Apple Silicon invalidates the signature when the load commands change).
#
#   usage: bundle_macos.sh <main.dylib> <dest-dir>
set -euo pipefail

SRC="${1:?source dylib}"
DEST="${2:?dest dir}"
mkdir -p "$DEST"

is_system() { case "$1" in /usr/lib/*|/System/*) return 0;; *) return 1;; esac; }

declare -A copied
queue=()

copy_in() {
    local src="$1" leaf
    leaf="$(basename "$src")"
    [[ -n "${copied[$leaf]:-}" ]] && return
    cp -fL "$src" "$DEST/$leaf"     # -L: resolve Homebrew's version symlinks
    chmod u+w "$DEST/$leaf"
    copied[$leaf]=1
    queue+=("$DEST/$leaf")
}

copy_in "$SRC"

# Breadth-first over dependencies, copying each non-system lib once.
i=0
while (( i < ${#queue[@]} )); do
    bin="${queue[$i]}"; ((i++)) || true
    while read -r dep; do
        [[ -z "$dep" || "$dep" == @* ]] && continue
        is_system "$dep" && continue
        [[ -f "$dep" ]] && copy_in "$dep"
    done < <(otool -L "$bin" | tail -n +2 | awk '{print $1}')
done

# Relocate ids + inter-lib references to @loader_path, then re-sign.
for f in "$DEST"/*.dylib; do
    leaf="$(basename "$f")"
    install_name_tool -id "@loader_path/$leaf" "$f"
    while read -r dep; do
        [[ -z "$dep" || "$dep" == @loader_path/* ]] && continue
        is_system "$dep" && continue
        dleaf="$(basename "$dep")"
        [[ -n "${copied[$dleaf]:-}" ]] && install_name_tool -change "$dep" "@loader_path/$dleaf" "$f"
    done < <(otool -L "$f" | tail -n +2 | awk '{print $1}')
    codesign --force --sign - "$f" 2>/dev/null || true
done

echo "bundled $(ls "$DEST"/*.dylib | wc -l | tr -d ' ') libs into $DEST"