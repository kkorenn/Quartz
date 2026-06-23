//using Microsoft.ClearScript.V8;
using GTweens.Contexts;
using Quartz.Compat;
using Quartz.Compat.Interface;
using Quartz.Core.Service;
using Quartz.IO;
using Quartz.Localization;
using Quartz.Resource;
using System.Reflection;

namespace Quartz.Core;

public static class MainCore {
    public static QuartzRuntime Runtime { get; private set; }

    public static event Action<bool, bool> OnModEnabledChanged {
        add => Runtime.OnModEnabledChanged += value;
        remove => Runtime.OnModEnabledChanged -= value;
    }

    public static Version Version => Runtime.Version;
    public static Assembly Asm => Runtime.Assembly;
    public static QuartzLogger Log => Runtime.Logger;
    public static PathService Paths => Runtime.Paths;
    public static SettingsFile<CoreSettings> ConfMgr => Runtime.Config;
    public static CoreSettings Conf => Runtime.Config.Data;
    public static Translator Tr => Runtime.Localization.Translator;
    public static ResourceManager Res => Runtime.Resource;
    public static SpriteManager Spr => Runtime.Sprite;
    public static IQuartzHost Host => Runtime.Host;
    public static UnityEngine.GameObject Root => Runtime.RootObject;
    public static GTweensContext TC => Runtime.TweensContext;
    //public static V8ScriptEngine V8 => Runtime.V8Engine;
    public static bool IsModEnabled => Runtime.State.IsEnabled;

    public static void Initialize(IQuartzHost host) {
        if(Runtime != null) {
            return;
        }

        Runtime = new QuartzRuntime(host);

        Runtime.Initialize();
    }

    public static void Tick() => Runtime?.Tick();

    public static void Dispose() {
        if(Runtime == null) {
            return;
        }

        Runtime.Dispose();
        Runtime = null;
    }

    public static void SetModEnabled(bool enabled) => Runtime.SetModEnabled(enabled, false);
}