# Project Agent Guide

This is the high-level map for AI agents working in this repo. Read this first, then read the narrower guides in this folder when they match the task:

- `agents/commits.md` — commit format and staging rules.
- `agents/i18n.md` — localization keys, JSON parity, and translation audits.
- `agents/release.md` — full GitHub release flow and changelog de-duplication.

## What this project is

Quartz is an all-in-one mod for **A Dance of Fire and Ice**. It builds one shared C# runtime for two loader targets:

- **MelonLoader**: primary/recommended build, packaged as `dist/Quartz.zip` with `Mods/Quartz.dll` plus `UserData/Quartz/*`.
- **UnityModManager**: alternate build, packaged as `dist/QuartzUmm.zip` as a self-contained `Quartz/` mod folder.

The codebase was formerly KorenResourcePack v2 and still contains migration/self-heal code for old `Koren` user data and `Koren.dll` installs. Treat this as intentional compatibility unless the task is explicitly about removing legacy support.

## Tech stack and build model

- Language: C# with `netstandard2.1` for the mod (`Quartz/Quartz.csproj`).
- Tests: small plain .NET console test project targeting `net10.0` (`Quartz.Tests/Quartz.Tests.csproj`).
- SDK pin: `global.json` asks for .NET SDK `10.0.100` with `rollForward: latestFeature`.
- Native renderer: C + FFmpeg wrapper under `native/koren_encoder/`, staged to `native/dist/<platform>/` and bundled into the mod zips.
- Formatting: `.editorconfig` uses 4-space C# indentation, tabs for project files, 2-space JSON/Markdown.
- Nullable: mod uses `<Nullable>warnings</Nullable>`; tests use nullable enabled.
- C# style seen in repo: file-scoped namespaces, opening braces on the same line, explicit static feature classes, settings objects with public fields and manual JSON serialize/deserialize.

Local game DLL references come from `Directory.Build.props` / `Directory.Build.example.props` and point at the user's ADOFAI install. Do not assume the project builds on a clean CI machine without the game assemblies.

## Important commands

From repo root:

```sh
./test.sh
```

Runs the console tests in Release:

```sh
dotnet run --project Quartz.Tests/Quartz.Tests.csproj -c Release
```

Build and auto-install/package both loader targets:

```sh
./build.sh [Debug|Release|Debug_IL2CPP|Release_IL2CPP] [ML|UMM|both]
```

Examples:

```sh
./build.sh Debug ML
./build.sh Release both
```

Direct builds:

```sh
dotnet build Quartz/Quartz.csproj -c Debug -p:LoaderTarget=ML
dotnet build Quartz/Quartz.csproj -c Debug -p:LoaderTarget=UMM
```

Native renderer:

```sh
./native/build.sh              # host platform, runs self-test by default
./native/build.sh --no-test
./native/build_win.sh          # cross-compiles Windows DLL; needs mingw-w64, curl, unzip
```

Release publishing is handled by `tools/release.sh`; read `agents/release.md` before using it.

## Repo map

Top level:

- `README.md` — user-facing install/readme, screenshots.
- `Quartz.slnx` — solution file.
- `Quartz/` — main mod source.
- `Quartz.Tests/` — plain .NET tests for Unity-free code and localization parity.
- `native/` — C FFmpeg encoder and platform bundles used by the recorder.
- `tools/release.sh` — GitHub release automation.
- `build.sh` — local build/auto-install/package script.
- `test.sh` — test runner.
- `build.json` — per-version/channel build counter; generated/read by build and release tooling.
- `dist/` — generated release zips; do not hand-edit.
- `agents/` — workflow docs for AI agents.

Main `Quartz/` layout:

- `Loader.cs` — MelonLoader entry point, compiled only when `LoaderTarget != UMM`.
- `LoaderUmm.cs` — UnityModManager entry point, compiled only when `LoaderTarget=UMM`.
- `Core/` — runtime lifecycle, version info, keybinds, service/tick orchestration.
- `Compat/` — small host abstraction interfaces used by both loaders.
- `IO/` — settings persistence, atomic files, profile manager, path utilities.
- `Localization/` — language loader and TMP text localization behavior.
- `Resource/` — embedded images/fonts, exported fonts/lang/presets, resource managers.
- `UI/` — in-game settings UI, page factories, reusable controls, drag/resize/reorganize utilities.
- `Features/` — feature modules and their settings/patches/overlays.
- `Update/` — self-update logic against GitHub releases.
- `Async/`, `Tween/`, `GTweens/`, `Utility/` — supporting runtime utilities.

## Runtime architecture

`Loader.cs` and `LoaderUmm.cs` are thin host bridges. Both call `MainCore.Initialize(IQuartzHost)`, which creates a `QuartzRuntime`.

Key files:

- `Quartz/Core/MainCore.cs` — static facade used throughout the mod (`MainCore.Conf`, `MainCore.Tr`, `MainCore.Log`, `MainCore.Root`, etc.). It null-guards teardown paths.
- `Quartz/Core/QuartzRuntime.cs` — owns the lifecycle. It creates paths/config/resources/root object, initializes services, registers ticks, toggles features, and disposes in reverse order.
- `Quartz/Core/RuntimeServices.cs` — service initialize/dispose list with startup timing logs.
- `Quartz/Core/RuntimeTicks.cs` — per-frame tick list.
- `Quartz/Core/Service/*` — `PathService`, `LocalizationService`, `UIService`, `TweenService`, `HarmonyService`.
- `Quartz/Core/Info.cs` — project identity (`Version`, `Channel`, GitHub repo info). `Build` comes from generated `BuildInfo.g.cs` based on `build.json`.

`QuartzRuntime.Initialize()` is where most global feature setup happens. Before adding another startup hook, check that file for the existing ordering and disposal expectations.

## Loader-specific behavior

MelonLoader (`Loader.cs`):

- Data root is `<game>/UserData/Quartz`.
- Installed DLL is `<game>/Mods/Quartz.dll`.
- Self-update downloads `Quartz.zip` and extracts over the game root.
- Has `[assembly: HarmonyDontPatchAll]`; `HarmonyService` owns patch lifecycle to avoid double patching.

UnityModManager (`LoaderUmm.cs`):

- Data root is the mod's own folder (`.../Quartz/`).
- Build defines `QUARTZ_UMM` and outputs assembly name `QuartzUmm` while keeping root namespace `Quartz`.
- Entry method is `Quartz.LoaderUmm.Load` via `Quartz/Resource/Umm/Info.json`.
- No UMM IMGUI settings panel; settings live in Quartz's own uGUI menu.
- Self-update downloads `QuartzUmm.zip` and extracts over the UMM mods directory.

When changing loader behavior, check both loader files and the packaging targets in `Quartz/Quartz.csproj`.

## Feature modules

Most features live under `Quartz/Features/<Name>/` and commonly have:

- `<Feature>.cs` — runtime/static feature logic.
- `<Feature>Settings.cs` — persisted settings object.
- `<Feature>Patches.cs` or focused patch files — Harmony patches.
- `<Feature>Overlay.cs` — UI shown during gameplay/editor.
- Matching UI controls under `Quartz/UI/Factory/Page/`.

Current feature areas include:

- `AutoDeafen` — Discord/RPC-driven deafen behavior.
- `ChatterBlocker` — input/chat blocking behavior.
- `Combo`, `Judgement`, `ProgressBar`, `SongTitle`, `Panels` — gameplay HUD overlays.
- `Editor` — editor-focused tweaks/readouts/BGA/difficulty behavior.
- `EffectRemover`, `Nostalgia`, `PlanetColors`, `UiHider`, `Tweaks`, `GameOverlayFont` — visual/gameplay presentation tweaks.
- `KeyLimiter`, `Restriction` — input/gameplay restrictions.
- `KeyViewer` — key display overlay and DM Note-compatible CSS parser/rendering.
- `Optimizer` — performance/background execution/process priority toggles.
- `OttoIcon` — Otto icon customization.
- `PlayCount` — run/play count tracking service.
- `Recorder` — offline/native FFmpeg renderer from the Editor tab.
- `Status` — live stat calculations used by panels and overlays.
- `Interop` — compatibility bridges/importers for other mods such as XPerfect and UMM settings.

Before editing a feature, trace all three places: the feature module, its settings class, and the page factory that exposes it.

## UI system

The in-game menu is built in code under `Quartz/UI`.

- `Quartz/UI/UICore.cs` creates the top-level canvas, panel, tabs, first-run helper, reorganize mode, resize handle, tooltips, and global open/close behavior.
- `Quartz/UI/Factory/MenuFactory.cs` and `PageFactory.cs` build tabs/pages.
- `Quartz/UI/Factory/Page/Page*.cs` are the actual settings pages.
- `Quartz/UI/Generator/GenerateUI*.cs` creates common rows/controls: toggles, buttons, sliders, inputs, dropdowns, color pickers, keybinds, collapsibles.
- `Quartz/UI/Objects/Impl/*` are backing objects for those controls.
- `Quartz/UI/Utility/Reorganizer.cs` and related utility files handle draggable overlay layout.

Localization convention for UI controls is important: many `GenerateUI` controls auto-localize by normalizing the control `id` into a key. Read `agents/i18n.md` before adding or changing user-facing strings.

## Settings and data files

Core settings:

- `Quartz/IO/CoreSettings.cs` persists main UI/mod settings to the loader data root.
- `MainCore.Conf` is the active `CoreSettings`; `MainCore.ConfMgr.RequestSave()` schedules writes.

Feature settings:

- Use `SettingsFile<T>` with settings classes implementing `ISettingsFile`.
- Most settings classes expose public fields and implement manual `Serialize()` / `Deserialize(JToken)` using `IOUtils.Read`.
- Save through each feature's `ConfMgr.RequestSave()` or helper, not by writing files directly.

Resources:

- Embedded resources: `Quartz/Resource/Embedded/**` are compiled into the DLL.
- Exported resources: `Quartz/Resource/Export/**` are copied into `UserData/Quartz` or UMM mod folder during packaging.
- Languages: `Quartz/Resource/Export/Lang/en-US.json` and `ko-KR.json` must stay key-for-key identical.
- Presets/fonts are shipped under `Quartz/Resource/Export/`.

## Recorder and native encoder

The recorder is one of the most cross-cutting areas:

- `Quartz/Features/Recorder/Recorder.cs` — state machine (`Idle`, `Armed`, `Recording`, `Finalizing`), native availability, arm/cancel/session lifecycle.
- `Quartz/Features/Recorder/RecorderSession.cs` — frame/audio capture session.
- `Quartz/Features/Recorder/RecorderPatches.cs` — game hooks for starting, completing, failing, pausing, and controlled time.
- `Quartz/Features/Recorder/RecorderAudioTap.cs` and `AutoHitSnap.cs` — audio/timing support.
- `Quartz/Features/Recorder/Native/*` — C# P/Invoke wrapper and platform loading.
- `native/koren_encoder/*` — C wrapper around FFmpeg.
- `native/dist/osx` and `native/dist/win` — bundled native runtime artifacts included by the csproj.

The renderer depends on deterministic time control and native library loading. Be careful with Unity time/audio APIs, pause/exit patches, and platform paths. If changing native code, run the native build/self-test and then the C# tests.

## Tests

`Quartz.Tests/Program.cs` currently covers:

- semantic version parsing and prerelease ordering,
- atomic file replacement,
- `en-US`/`ko-KR` localization key parity,
- Unity-free KeyViewer CSS parser behavior.

Prefer putting tests here for logic that can be kept free of Unity/ADOFAI types. The test project links selected source files from `Quartz/` instead of referencing the whole Unity mod assembly.

## Localization rules

Read `agents/i18n.md` before touching strings. Short version:

- Add every key to both `Quartz/Resource/Export/Lang/en-US.json` and `ko-KR.json`.
- Builder controls often derive keys from IDs; do not assume the visible label string is enough.
- Missing keys silently fall back to English, so tests may pass while Korean leaks English unless you audit.
- Preserve placeholders, TMP tags, acronyms, and brand terms.

## Build and packaging details

`Quartz/Quartz.csproj` contains important custom MSBuild targets:

- `GenerateBuildInfo` reads `Quartz/Core/Info.cs` and `build.json`, then emits `BuildInfo.g.cs` into `obj/`.
- MelonLoader `PostBuild` copies `Quartz.dll`, exported resources, and native files into package staging and writes `dist/Quartz.zip`.
- UMM `PostBuildUmm` creates a self-contained `Quartz/` mod folder with `Info.json`, exported files, native files, and writes `dist/QuartzUmm.zip`.
- UMM uses separate `obj/umm/...` and `bin/umm/...` paths so it does not clobber the MelonLoader output.

`build.sh` also attempts to build the Windows native encoder if `native/dist/win/koren_encoder.dll` is missing and `mingw-w64` is installed.

Do not hand-edit `build.json` or `Info.cs` for routine release bumps; use `tools/release.sh` per `agents/release.md`.

## Working-tree caution

This repo often has in-progress user edits. At the time this guide was created, the working tree already had modified and untracked recorder/tweaks files. Before editing:

```sh
git status --short
git diff --stat
```

Do not overwrite or casually reformat unrelated work. If a file is already modified, inspect the diff before touching it.

## Common agent workflow

1. Read this file plus any task-specific guide in `agents/`.
2. Check `git status --short` and `git diff --stat`.
3. Locate the relevant feature/page/settings files with search before editing.
4. For UI/user-facing strings, update both language JSON files and run the i18n parity check or `./test.sh`.
5. For feature changes, update the feature logic, settings serialization, and UI page together when needed.
6. Run the narrowest useful verification:
   - `./test.sh` for Unity-free logic/localization/parser changes.
   - `dotnet build Quartz/Quartz.csproj -c Debug -p:LoaderTarget=ML` for mod code if local game references are available.
   - Add `-p:LoaderTarget=UMM` when loader/packaging/shared runtime changes may affect UMM.
   - Native build scripts for encoder changes.
7. Summarize changed paths and actual verification output. Do not commit or push unless the user asks.

## Quick path lookup

- Project identity/version: `Quartz/Core/Info.cs`, `build.json`.
- Runtime startup/teardown: `Quartz/Core/QuartzRuntime.cs`, `Quartz/Core/MainCore.cs`.
- MelonLoader host: `Quartz/Loader.cs`.
- UMM host: `Quartz/LoaderUmm.cs`, `Quartz/Resource/Umm/Info.json`.
- Packaging/build targets: `Quartz/Quartz.csproj`, `build.sh`.
- Release script: `tools/release.sh`, `agents/release.md`.
- Commit rules: `agents/commits.md`.
- Localization: `Quartz/Localization/*`, `Quartz/Resource/Export/Lang/*.json`, `agents/i18n.md`.
- Settings persistence: `Quartz/IO/SettingsFile.cs`, `Quartz/IO/CoreSettings.cs`, feature `*Settings.cs` files.
- Main UI: `Quartz/UI/UICore.cs`, `Quartz/UI/Factory/Page/*.cs`, `Quartz/UI/Generator/*.cs`.
- Feature modules: `Quartz/Features/<Feature>/`.
- Stat panels: `Quartz/Features/Panels/PanelsOverlay.cs`, `PanelsSettings.cs`, `PageOverlay.cs`.
- Key viewer CSS parser: `Quartz/Features/KeyViewer/KeyViewerCss.cs`, tests in `Quartz.Tests/Program.cs`.
- Recorder: `Quartz/Features/Recorder/*`, `native/koren_encoder/*`.
- Native staged libraries: `native/dist/<os|win|linux>/`.
- Tests: `Quartz.Tests/Program.cs`, `test.sh`.
