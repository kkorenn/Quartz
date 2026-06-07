using MelonLoader;
using MelonLoader.Utils;
using Koren.Core;
using Koren.Compat.Interface;

[assembly: MelonInfo(typeof(Koren.Loader.ML.Loader), Info.Name, Info.Version, Info.Author, Info.GithubLink)]
[assembly: MelonGame("7th Beat Games", "A Dance of Fire and Ice")]

namespace Koren.Loader.ML;

// MelonLoader entry point. Ships under Mods/; references Koren.dll in UserLibs.
// Acts as the host bridge (IKorenHost/IKorenLogger) into the runtime.
public class Loader : MelonMod, IKorenHost, IKorenLogger {

    public IKorenLogger KorenLogger => this;

    public string KorenFilePath => MelonEnvironment.UserDataDirectory;

    public override void OnInitializeMelon() {
        MainCore.Initialize(this);
    }

    public override void OnDeinitializeMelon() {
        MainCore.Dispose();
    }

    public override void OnUpdate() {
        MainCore.Tick();
    }

    public void KorenMsg(string msg) => MelonLogger.Msg(msg);
    public void KorenWrn(string msg) => MelonLogger.Warning(msg);
    public void KorenErr(string msg) => MelonLogger.Error(msg);
}
