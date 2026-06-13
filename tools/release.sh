#!/usr/bin/env bash
# Builds a Release and publishes it as a GitHub release with Koren.dll
# attached. Pre-release channels (alpha/beta/rc) are marked as pre-releases.
#
# The build number is auto-incremented per (version, channel) and tracked in
# build.json (repo root, source-only — never shipped). build.json is the only
# source of truth: the csproj's GenerateBuildInfo target reads it at compile
# time and bakes BuildInfo.Number into the DLL. This script never edits Info.cs.
#
# Version + Channel come from Koren/Core/Info.cs — set those there, then run:
#   ./tools/release.sh
#
# The mod references the game's managed DLLs, so it can't build in CI — run
# this locally. Requires `gh` (authenticated) and `jq`.
set -euo pipefail

cd "$(dirname "$0")/.."

command -v gh >/dev/null || { echo "gh required: brew install gh (then 'gh auth login')" >&2; exit 1; }
command -v jq >/dev/null || { echo "jq required: brew install jq" >&2; exit 1; }

INFO="Koren/Core/Info.cs"
BUILDS="build.json"
[ -f "$BUILDS" ] || echo '{}' > "$BUILDS"

ver=$(grep -E 'const string Version' "$INFO" | sed -E 's/.*"([^"]+)".*/\1/')
chan=$(grep -E 'const string Channel' "$INFO" | sed -E 's/.*"([^"]+)".*/\1/')

if [ "$chan" = "stable" ]; then
  tag="v${ver}"
  title="v${ver}"
  notes="Stable release ${ver}."
  # Normal release, marked as the repo's "Latest".
  flags="--latest"
else
  # Increment the per-(version, channel) build counter.
  cur=$(jq -r --arg v "$ver" --arg c "$chan" '.[$v][$c] // 0' "$BUILDS")
  next=$((cur + 1))

  tmp=$(mktemp)
  jq --arg v "$ver" --arg c "$chan" --argjson n "$next" '.[$v][$c] = $n' "$BUILDS" > "$tmp" && mv "$tmp" "$BUILDS"

  # No sed into Info.cs — the build reads this number straight from build.json
  # via the csproj GenerateBuildInfo target.

  tag="v${ver}-${chan}-${next}"
  title="$tag"
  notes="${chan} build ${next} of ${ver}."
  # Pre-release, never the "Latest" (that stays on the newest stable build).
  flags="--prerelease --latest=false"
  echo "Build number: ${cur} -> ${next}  (${ver} ${chan})"
fi

echo "Building ${tag} ..."
dotnet build Koren/Koren.csproj -c Release

# Koren.zip is the full install (DLL + lang + fonts), built by the csproj
# PostBuild target. Ship the bare DLL alongside it too, so anyone still running
# an old updater (which only looks for a "Koren.dll" asset) can still update.
koren_dll="Koren/bin/Release/netstandard2.1/Koren.dll"
koren_zip="dist/Koren.zip"
[ -f "$koren_dll" ] || { echo "build output missing — aborting" >&2; exit 1; }
[ -f "$koren_zip" ] || { echo "dist/Koren.zip missing — aborting" >&2; exit 1; }

echo "Publishing ${tag} ..."
if gh release view "$tag" >/dev/null 2>&1; then
  gh release upload "$tag" "$koren_zip" "$koren_dll" --clobber
else
  # shellcheck disable=SC2086
  gh release create "$tag" "$koren_zip" "$koren_dll" --title "$title" --notes "$notes" $flags
fi

echo "Done: ${tag}"
echo "Commit the bump:  git add ${BUILDS} && git commit -m \"Release ${tag}\""
