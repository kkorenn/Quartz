// MelonLoader entry point — compiled into the default (ML) build only. The UMM
// build (-p:LoaderTarget=UMM, which defines QUARTZ_UMM) excludes this file so the
// assembly carries no MelonLoader reference; LoaderUmm.cs is its counterpart.
#if !QUARTZ_UMM
using MelonLoader;
using MelonLoader.Utils;
using Quartz;
using Quartz.Core;
using Quartz.Compat.Interface;

// MelonInfo's version must be a compile-time constant, so it uses the numeric
// core version; the full channel + build string lives in Info.DisplayVersion
// (shown in-game and used for update checks).
[assembly: MelonInfo(typeof(Loader), Info.Name, Info.Version, Info.Author, Info.GithubLink)]
[assembly: MelonGame("7th Beat Games", "A Dance of Fire and Ice")]
// Since the loader merge, Quartz.dll is the melon assembly itself, so
// MelonLoader's HarmonyInit would auto-PatchAll every [HarmonyPatch] class on
// top of HarmonyService's own PatchAll — double-applying every patch (hits
// counted twice, ChatterBlocker's duplicated SkyHook prefix swallowing every
// key). HarmonyService stays the single owner of patch lifecycle.
[assembly: HarmonyDontPatchAll]

namespace Quartz;

// MelonLoader entry point. Lives in Quartz.dll itself (single-DLL install under
// Mods/, mirroring upstream Overlayer's loader merge). Acts as the host bridge
// (IQuartzHost/IQuartzLogger) into the runtime.
public class Loader : MelonMod, IQuartzHost, IQuartzLogger {

    public IQuartzLogger QuartzLogger => this;

    // Data root is <game>/UserData/Quartz — the runtime uses this verbatim.
    public string QuartzFilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "Quartz");
    public string ModsPath => MelonEnvironment.ModsDirectory;
    public string UserLibsPath => MelonEnvironment.UserLibsDirectory;

    // MelonLoader pulls Quartz.zip from GitHub and self-replaces over the game
    // root (entries are Mods/Quartz.dll, UserData/Quartz/*).
    public bool SupportsSelfUpdate => true;
    public string UpdateAssetName => "Quartz.zip";
    public string UpdateExtractRoot => Directory.GetParent(MelonEnvironment.ModsDirectory)?.FullName;

    public override void OnInitializeMelon() => MainCore.Initialize(this);

    public override void OnDeinitializeMelon() => MainCore.Dispose();

    public override void OnUpdate() => MainCore.Tick();

    public void QuartzMsg(string msg) => MelonLogger.Msg(msg);
    public void QuartzWrn(string msg) => MelonLogger.Warning(msg);
    public void QuartzErr(string msg) => MelonLogger.Error(msg);
}
#endif
