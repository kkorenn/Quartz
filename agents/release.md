# Release Agent

How to cut a "full" release — a named build with a real, de-duplicated changelog — and publish it to GitHub. Read this before running `tools/release.sh` by hand.

## Shorthand

- **`release`** — do the whole flow below: figure out the next build, draft + de-dup the changelog, pick a name, preview, publish, then commit the bump.

## What "full" means

Old releases shipped boilerplate bodies (`alpha build 27 of 2.0.0.`). A full release adds two things:

| Piece | What | Where it goes |
|-------|------|---------------|
| **name** | one-line codename for the build's headline change | title, after an em dash: `v2.0.0-alpha-29 — Editor Readout & BGA` |
| **changelog** | categorized, user-facing bullets | release body (markdown) |

## The script is the engine — you supply the words

`tools/release.sh` owns identity + mechanics; you own the prose. Identity (Version, Channel) is read from `Koren/Core/Info.cs`; the per-(version, channel) build number lives in `build.json` and is auto-incremented. **Never edit `Info.cs` or `build.json` by hand for a release** — the script and csproj handle them.

```
tools/release.sh [flags]
  -n, --name TEXT        codename, appended to the title
  -m, --notes TEXT       changelog body (markdown)
  -F, --notes-file PATH  changelog body from a file ("-" = stdin)
      --no-auto          don't auto-draft from git when no notes given
      --dry-run          print tag/title/range/body; no bump, no build, no publish
```

With no notes flag it auto-drafts from commit subjects since the previous build's tag. **Treat that draft as raw material, not the final answer** — see the next section.

## The core gotcha: ranges over-count, so de-dup against prior notes

Tags in this repo are not a clean one-per-commit timeline:

- Builds are sometimes **re-cut from the same commit** (`alpha-27` and `alpha-28` both point at `b052c5b`).
- A build's tag can sit **behind** later commits (the `build: bump` commit lands *after* the tag).

So `git log <prev-tag>..HEAD` — what `--dry-run` shows as `range:` — will re-list work that already shipped in an earlier build. `git describe` is just as unreliable. **The authoritative "what's already announced" is the text of the previous release bodies, not the commit graph.** Your job is to diff against those and drop anything already listed.

## Procedure

1. **Preconditions.** On `main`, `gh auth status` OK. Feature work that belongs in this release should already be **committed** (the changelog reflects committed history) — if the tree has uncommitted features, stop and commit them first per [`agents/commits.md`](commits.md).
2. **See identity + the raw range.** Run `./tools/release.sh --dry-run`. Note `tag`, `range`, and read the auto-draft. This is the starting pile, not the release.
3. **Read the real commits.** `git log <range> --pretty='%h %s%n%b'` (use the `range` from step 2). Subjects + bodies tell you *what* and *why*.
4. **De-dup against shipped notes.** List recent releases and read their bodies:
   ```
   gh release list --limit 10
   gh release view <tag> --json name,body -q '.name + "\n" + .body'
   ```
   Skip boilerplate bodies (`alpha build N of …`). For every candidate change, if an earlier release already announced it, **drop it**. What's left is genuinely new since the last *substantive* release.
5. **Curate the changelog.** Write it for a player, not a committer:
   - Group by **concern**, one bullet per feature even if it spans several commits (e.g. the two `editor tile readout` commits → one bullet).
   - Categories, in this order, omit empties: `### New`, `### Improved`, `### Fixed`, `### Performance`.
   - Noun-phrase the change ("Editor floor readout: total angle, beats, duration") — don't paste the `feat:` subject.
   - **Omit pure housekeeping** — `build`/`chore`/`ci`/`docs`/`test`/`style`/`refactor` — unless it's actually user-visible.
   - Write it to a scratch file (`/tmp/notes.md`); don't commit it.
6. **Pick the name.** Short codename = the build's headline. Past-style examples: *KeyViewer CSS Engine*, *Nostalgia Tab*, *Settings Importer*, *Editor Readout & BGA*. One feature stands out → name it after that; a grab-bag → name the biggest piece.
7. **Preview.** `./tools/release.sh -n "Name" -F /tmp/notes.md --dry-run`. Show the user the `title` and `body`. Publishing is outward-facing and hard to undo — **get a go-ahead before the real run.**
8. **Publish.** Same command, drop `--dry-run`. The script bumps `build.json`, builds Release, and uploads `Koren.zip` + `Koren.dll`. (Re-running an existing tag refreshes title + notes and re-uploads assets.)
9. **Commit the bump.** The build-number bump leaves `build.json` dirty. Commit it per [`agents/commits.md`](commits.md): `build: bump <chan> to <next>`. **Push only if asked.**

## Don'ts

- Don't hand-edit `build.json` / `Info.cs`, or pass a hand-made tag — identity is derived.
- Don't trust `range:` / the auto-draft as the changelog — it over-counts (step 4 exists for this).
- Don't re-list a feature a prior build already announced.
- Don't bump Version or change Channel as part of a routine release — that's a deliberate human call in `Info.cs`.
- Don't push after publishing unless the user asks.
