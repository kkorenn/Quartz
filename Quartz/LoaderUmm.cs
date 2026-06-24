// UnityModManager entry point — compiled into the UMM build only
// (-p:LoaderTarget=UMM, which defines QUARTZ_UMM). This is the counterpart to
// Loader.cs (MelonLoader): same host-agnostic runtime, different loader bridge.
//
// Deliberately NO modEntry.OnGUI: Quartz's settings live in its own uGUI menu
// (toggled by the in-mod keybind), not UMM's IMGUI panel. UMM here is purely a
// bootstrap + lifecycle host, so a UMM user gets the identical in-game UI a
// MelonLoader user does.
#if QUARTZ_UMM
using Quartz.Core;
using Quartz.Compat.Interface;
using UnityModManagerNet;

namespace Quartz;

public sealed class LoaderUmm : IQuartzHost, IQuartzLogger {
    private static LoaderUmm instance;

    private readonly UnityModManager.ModEntry.ModLogger logger;
    private readonly string modPath;

    private LoaderUmm(UnityModManager.ModEntry modEntry) {
        logger = modEntry.Logger;
        // The mod's own folder (UMM guarantees a trailing separator). Quartz's
        // data lives here too, so a UMM install is fully self-contained:
        // Quartz.dll + Info.json + Settings.json + Lang/ + Fonts/ side by side.
        modPath = modEntry.Path;
    }

    // EntryMethod in Info.json points here: "Quartz.LoaderUmm.Load".
    public static bool Load(UnityModManager.ModEntry modEntry) {
        if(instance != null) {
            return true;
        }
        instance = new LoaderUmm(modEntry);

        // If startup throws, reset instance (MainCore already nulled its Runtime)
        // so UMM can re-enter Load on a reload instead of being wedged until a
        // full game restart. UMM still flags the mod as errored.
        try {
            MainCore.Initialize(instance);
        } catch(System.Exception e) {
            modEntry.Logger.Error($"Quartz failed to initialize: {e}");
            instance = null;
            return false;
        }

        // Per-frame tick, identical to MelonMod.OnUpdate.
        modEntry.OnUpdate = (_, _) => MainCore.Tick();

        // UMM's enable/disable toggle drives the mod's master switch (and is
        // persisted through CoreSettings.Active, same as the in-mod power
        // button). The in-mod uGUI power toggle stays fully functional too.
        modEntry.OnToggle = (_, value) => {
            MainCore.SetModEnabled(value);
            return true;
        };

        // Full teardown on reload/quit so a subsequent Load re-initializes clean.
        // Clear the delegates too, so UMM can't fire a stale OnUpdate/OnToggle
        // into a disposed runtime after unload.
        modEntry.OnUnload = entry => {
            MainCore.Dispose();
            entry.OnUpdate = null;
            entry.OnToggle = null;
            instance = null;
            return true;
        };

        return true;
    }

    public IQuartzLogger QuartzLogger => this;

    // Data root = the mod's own folder (self-contained UMM layout). The updater
    // paths point here too, but they're never used: SupportsSelfUpdate is false.
    public string QuartzFilePath => modPath;
    public string ModsPath => modPath;
    public string UserLibsPath => modPath;

    // Self-update pulls the UMM-specific zip and extracts it over the UMM mods
    // dir (modPath's parent), so the self-contained Quartz/ mod folder — DLL,
    // Info.json, Lang/Fonts — is replaced in place. modPath carries a trailing
    // separator, so trim before walking up to the mods dir.
    public bool SupportsSelfUpdate => true;
    public string UpdateAssetName => "QuartzUmm.zip";
    public string UpdateExtractRoot =>
        Directory.GetParent(modPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;

    public void QuartzMsg(string msg) => logger.Log(msg);
    public void QuartzWrn(string msg) => logger.Warning(msg);
    public void QuartzErr(string msg) => logger.Error(msg);
}
#endif
