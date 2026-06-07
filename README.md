# KorenResourcePack v2

A clean-slate **v2** of KorenResourcePack — a [MelonLoader](https://melonloader.co/) mod for
**A Dance of Fire and Ice**. The UI is ported from [Overlayer](https://github.com/modlist-org/Overlayer)
(the project the original was based on); v2 starts fresh with a lean core and builds features on top.

## What's in the skeleton

- **Full Overlayer UI** (uGUI): draggable/resizable panel, slide-out menu, theming, tooltips, search,
  UI-scale, first-run helper. Toggle in-game with **Alt + `** (hold 0.4s to reset position/scale).
- **Every widget**: toggle, button, slider, dropdown, text input, and a full **HSV + RGBA color picker**.
- **Accent color picker** (Settings page) recolors the entire UI live.
- **First feature — Status**: a draggable HUD showing live FPS / frame time / mod state, configured from
  its own settings page (toggles, prefix input, font-size slider, text-color picker, background toggle).
- **Auto-install on build** — see below.

## Project layout

| Project | Output | Goes to |
|---------|--------|---------|
| `Koren` | `Koren.dll` (core mod + UI + features) | `UserLibs/` |
| `Koren.Loader.ML` | `Koren.Loader.ML.dll` (MelonLoader entry) | `Mods/` |

Language files are exported to `UserData/Koren/`. Settings live in `UserData/Koren/Settings.json` and
`UserData/Koren/Status.json`.

## Build & install

Requires the .NET SDK and a MelonLoader-patched copy of the game.

```bash
./build.sh            # Debug; auto-detects the Steam install and writes Directory.Build.props
# or
dotnet build Koren.slnx -c Debug
```

The build's PostBuild step copies the DLLs straight into the game (`Mods/` + `UserLibs/`) and exports the
language files — launch the game and the mod is loaded. Override the game path with the `GAMEPATH` env var
or by editing `Directory.Build.props` (see `Directory.Build.example.props`).

## Credits

UI and architecture derived from **Overlayer** by modlist.org (GPLv3). GTweens tween library vendored under
`Koren/GTweens`.
