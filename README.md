# KorenResourcePack v2

Koren is a MelonLoader mod for **A Dance of Fire and Ice**. It provides configurable gameplay overlays, key viewing, visual tweaks, profiles, localization, update handling, and Discord auto-deafen support.

## Requirements

- .NET 10 SDK
- A Dance of Fire and Ice
- MelonLoader installed in the game directory

## Setup

Copy the local build-path template, then edit it for your game installation:

```sh
cp Directory.Build.example.props Directory.Build.props
```

`Directory.Build.props` is ignored by Git because it contains machine-specific paths.

## Build

Compile and package without touching the game installation:

```sh
dotnet build Koren.slnx -c Debug
```

Output package: `dist/Koren.zip`.

Build, package, and install into the detected game directory:

```sh
./build.sh Debug
```

Supported configurations: `Debug`, `Release`, `Debug_IL2CPP`, and `Release_IL2CPP`.

## Tests

```sh
./test.sh
```

The smoke-test project covers version ordering, atomic persistence, and localization-key parity.

## Repository layout

- `Koren/Features` — gameplay and visual features
- `Koren/UI` — settings UI and overlay components
- `Koren/IO` — settings and profile persistence
- `Koren/Resource` — embedded and exported assets
- `tools/release.sh` — release-build number helper

## License

Source code: GPL-3.0. See `LICENSE`.
