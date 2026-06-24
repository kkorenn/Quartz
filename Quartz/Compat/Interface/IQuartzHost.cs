namespace Quartz.Compat.Interface;

public interface IQuartzHost {
    IQuartzLogger QuartzLogger { get; }

    // The mod's data root — the folder that directly holds Settings.json, Lang/,
    // Fonts/, Temp/, etc. (PathService treats this as its RootPath verbatim).
    //   MelonLoader: <game>/UserData/Quartz
    //   UnityModManager: the mod's own folder (<mods>/Quartz), so a UMM install
    //   is self-contained — DLL + Info.json + Lang/Fonts live together.
    string QuartzFilePath { get; }

    // Install locations, used by the updater to drop new builds in place.
    // ModsPath holds the mod assembly; UserLibsPath is only touched to clean up
    // the old two-DLL MelonLoader install.
    string ModsPath { get; }
    string UserLibsPath { get; }

    // Whether the in-mod updater may download and replace the mod files itself.
    // True for both loaders; the asset + layout differ via the two members below.
    bool SupportsSelfUpdate { get; }

    // The GitHub release asset the updater downloads + the directory its entries
    // extract relative to. These are what make the UMM build pull its OWN zip:
    //   MelonLoader: "Quartz.zip"  extracted over the game root
    //                (entries Mods/Quartz.dll, UserData/Quartz/*).
    //   UnityModManager: "QuartzUmm.zip" extracted over the UMM mods dir
    //                (entries Quartz/Quartz.dll, Quartz/Info.json, Quartz/Lang/*),
    //                so the self-contained mod folder is replaced in place.
    string UpdateAssetName { get; }
    string UpdateExtractRoot { get; }
}
