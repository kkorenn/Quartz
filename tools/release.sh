#!/usr/bin/env bash
# Build a Release and publish it to GitHub with Quartz.zip + Quartz.dll +
# QuartzUmm.zip (the UnityModManager build) attached.
# Pre-release channels (alpha/beta/rc) are flagged as pre-releases.
#
# Release identity (Version + Channel) comes from Quartz/Core/Info.cs — set those
# there first. The per-(version, channel) build number is auto-incremented and
# tracked in build.json (repo root, source-only — never shipped). build.json is
# the only source of truth: the csproj's GenerateBuildInfo target reads it at
# compile time and bakes BuildInfo.Number into the DLL. This never edits Info.cs.
#
# A "full" release also carries a NAME (codename) and a CHANGELOG body:
#
#   -n, --name TEXT        one-line codename, appended to the title after an em
#                          dash:  "v2.0.0-alpha-29 — Editor Readout & BGA"
#   -m, --notes TEXT       changelog body (markdown). Overrides auto-generation.
#   -F, --notes-file PATH  read the changelog body from a file ("-" = stdin).
#       --no-auto          don't fall back to a git-log changelog when no notes
#                          are given (publish with just the build footer).
#       --dry-run          print tag / title / range / body and exit. No build
#                          number bump, no build, no publish — safe to preview.
#   -h, --help             this help.
#
# With no notes flag the script auto-drafts a changelog from the Conventional
# Commit subjects since the previous build's tag, so a bare `./tools/release.sh`
# still ships real notes. NOTE: tags in this repo can sit on older commits and
# builds are sometimes re-cut from one commit, so that range OVER-counts (it can
# include already-released work). For curated, de-duplicated notes use the agent
# flow in agents/release.md — it cross-checks prior release bodies.
#
# The mod links the game's managed DLLs, so it can't build in CI — run locally.
# Requires `gh` (authenticated), `jq`, and `git`.
set -euo pipefail

cd "$(dirname "$0")/.."

command -v gh  >/dev/null || { echo "gh required: brew install gh (then 'gh auth login')" >&2; exit 1; }
command -v jq  >/dev/null || { echo "jq required: brew install jq" >&2; exit 1; }
command -v git >/dev/null || { echo "git required" >&2; exit 1; }

usage() { sed -n '2,31p' "$0" | sed 's/^# \{0,1\}//'; }

# --- Parse flags ---------------------------------------------------------
name="" ; notes="" ; notes_file="" ; auto=1 ; dry_run=0
while [ $# -gt 0 ]; do
  case "$1" in
    -n|--name)       name="${2:?--name needs a value}"; shift 2 ;;
    -m|--notes)      notes="${2:?--notes needs a value}"; shift 2 ;;
    -F|--notes-file) notes_file="${2:?--notes-file needs a value}"; shift 2 ;;
    --no-auto)       auto=0; shift ;;
    --dry-run)       dry_run=1; shift ;;
    -h|--help)       usage; exit 0 ;;
    *) echo "unknown arg: $1 (try --help)" >&2; exit 2 ;;
  esac
done

INFO="Quartz/Core/Info.cs"
BUILDS="build.json"
[ -f "$BUILDS" ] || echo '{}' > "$BUILDS"

ver=$(grep -E 'const string Version' "$INFO" | sed -E 's/.*"([^"]+)".*/\1/')
chan=$(grep -E 'const string Channel' "$INFO" | sed -E 's/.*"([^"]+)".*/\1/')

# --- Compute tag, release flags, and the previous tag (changelog base) ----
if [ "$chan" = "stable" ]; then
  tag="v${ver}"
  blurb="Stable release ${ver}."
  flags="--latest"
  bumped=0
  # Best-effort previous release (newest published), used only as a diff base.
  prev_tag=$(gh release list --limit 1 --json tagName -q '.[0].tagName' 2>/dev/null || true)
else
  cur=$(jq -r --arg v "$ver" --arg c "$chan" '.[$v][$c] // 0' "$BUILDS")
  next=$((cur + 1))
  tag="v${ver}-${chan}-${next}"
  blurb="${chan} build ${next} of ${ver}."
  flags="--prerelease --latest=false"
  bumped=1
  # Previous build of THIS lineage is deterministic from the counter.
  if [ "$cur" -gt 0 ]; then prev_tag="v${ver}-${chan}-${cur}"; else
    prev_tag=$(gh release list --limit 1 --json tagName -q '.[0].tagName' 2>/dev/null || true)
  fi
fi

# Only diff from a base we actually have locally.
if [ -n "${prev_tag:-}" ] && git rev-parse -q --verify "refs/tags/${prev_tag}" >/dev/null 2>&1; then
  range="${prev_tag}..HEAD"
else
  prev_tag="" ; range="HEAD"
fi

# --- Auto-draft a changelog from Conventional Commit subjects -------------
gen_changelog() {
  local subj type desc
  local -a feats=() fixes=() perfs=() others=()
  while IFS= read -r subj; do
    [ -n "$subj" ] || continue
    type=$(printf '%s' "$subj" | sed -E 's/^([a-z]+)(\([^)]*\))?!?:.*/\1/')
    desc=$(printf '%s' "$subj" | sed -E 's/^[a-z]+(\([^)]*\))?!?:[[:space:]]*//')
    case "$type" in
      feat) feats+=("$desc") ;;
      fix)  fixes+=("$desc") ;;
      perf) perfs+=("$desc") ;;
      build|chore|ci|docs|style|test|refactor) : ;;  # housekeeping — omit
      *)    others+=("$desc") ;;
    esac
  done < <(git log --no-merges --pretty=%s "$range")

  [ ${#feats[@]}  -gt 0 ] && { printf '### New\n';         printf -- '- %s\n' "${feats[@]}";  printf '\n'; }
  [ ${#fixes[@]}  -gt 0 ] && { printf '### Fixed\n';       printf -- '- %s\n' "${fixes[@]}";  printf '\n'; }
  [ ${#perfs[@]}  -gt 0 ] && { printf '### Performance\n'; printf -- '- %s\n' "${perfs[@]}";  printf '\n'; }
  [ ${#others[@]} -gt 0 ] && { printf '### Other\n';       printf -- '- %s\n' "${others[@]}"; printf '\n'; }
  return 0
}

# --- Assemble the release body -------------------------------------------
if   [ -n "$notes_file" ]; then
  if [ "$notes_file" = "-" ]; then changelog=$(cat); else changelog=$(cat "$notes_file"); fi
elif [ -n "$notes" ]; then
  changelog="$notes"
elif [ "$auto" -eq 1 ]; then
  changelog=$(gen_changelog)
  [ -n "$changelog" ] || changelog="_No notable changes._"
else
  changelog=""
fi

footer="$blurb"
if [ -n "$prev_tag" ]; then footer="${footer}  ·  ${prev_tag}…${tag}"; fi

if [ -n "$changelog" ]; then
  body=$(printf '%s\n\n---\n%s\n' "$changelog" "$footer")
else
  body="$footer"
fi

title="$tag"
if [ -n "$name" ]; then title="${tag} — ${name}"; fi

# --- Dry run: show everything, mutate nothing ----------------------------
if [ "$dry_run" -eq 1 ]; then
  printf '== DRY RUN (no bump, no build, no publish) ==\n'
  printf 'tag:    %s\ntitle:  %s\nrange:  %s\nflags:  %s\n\n----- body -----\n%s\n' \
    "$tag" "$title" "$range" "$flags" "$body"
  exit 0
fi

# --- Commit the build-number bump (prerelease only) ----------------------
if [ "$bumped" -eq 1 ]; then
  tmp=$(mktemp)
  jq --arg v "$ver" --arg c "$chan" --argjson n "$next" '.[$v][$c] = $n' "$BUILDS" > "$tmp" && mv "$tmp" "$BUILDS"
  echo "Build number: ${cur} -> ${next}  (${ver} ${chan})"
fi

echo "Building ${tag} (MelonLoader) ..."
dotnet build Quartz/Quartz.csproj -c Release
echo "Building ${tag} (UnityModManager) ..."
dotnet build Quartz/Quartz.csproj -c Release -p:LoaderTarget=UMM

# Quartz.zip is the full MelonLoader install (DLL + lang + fonts), built by the
# csproj PostBuild target. Ship the bare DLL alongside it too, so anyone still
# running an old updater (which only looks for a "Quartz.dll" asset) can update.
# QuartzUmm.zip is the self-contained UnityModManager mod folder — the UMM build's
# updater downloads THIS asset (UpdateAssetName), so it must ship every release.
quartz_dll="Quartz/bin/Release/netstandard2.1/Quartz.dll"
quartz_zip="dist/Quartz.zip"
quartz_umm_zip="dist/QuartzUmm.zip"
[ -f "$quartz_dll" ] || { echo "build output missing — aborting" >&2; exit 1; }
[ -f "$quartz_zip" ] || { echo "dist/Quartz.zip missing — aborting" >&2; exit 1; }
[ -f "$quartz_umm_zip" ] || { echo "dist/QuartzUmm.zip missing — aborting" >&2; exit 1; }

# gh wants the body from a file (preserves markdown + newlines).
notes_tmp=$(mktemp)
printf '%s\n' "$body" > "$notes_tmp"
trap 'rm -f "$notes_tmp"' EXIT

echo "Publishing ${title} ..."
if gh release view "$tag" >/dev/null 2>&1; then
  # Re-publish: refresh title + notes, then replace the assets.
  gh release edit "$tag" --title "$title" --notes-file "$notes_tmp"
  gh release upload "$tag" "$quartz_zip" "$quartz_dll" "$quartz_umm_zip" --clobber
else
  # shellcheck disable=SC2086
  gh release create "$tag" "$quartz_zip" "$quartz_dll" "$quartz_umm_zip" --title "$title" --notes-file "$notes_tmp" $flags
fi

echo "Done: ${title}"
if [ "$bumped" -eq 1 ]; then
  echo "Commit the bump:  git add ${BUILDS} && git commit -m \"build: bump ${chan} to ${next}\""
fi
