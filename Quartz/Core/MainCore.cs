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

        // Runtime must be set before Initialize() runs — the static facades above
        // (Conf, Paths, Tr, ...) read through it during startup. But if Initialize
        // throws, leave Runtime null so a later call can retry from clean state
        // instead of short-circuiting on a half-built instance (UnityModManager
        // can re-enter Load after an errored mod).
        Runtime = new QuartzRuntime(host);
        try {
            Runtime.Initialize();
        } catch {
            try {
                Runtime.Dispose();
            } catch {
                // best-effort teardown of the partial runtime
            }
            Runtime = null;
            throw;
        }
    }

    public static void Tick() => Runtime?.Tick();

    public static void Dispose() {
        if(Runtime == null) {
            return;
        }

        Runtime.Dispose();
        Runtime = null;
    }

    // Null-guarded like Tick/Dispose: UnityModManager can fire OnToggle after a
    // teardown that already nulled Runtime.
    public static void SetModEnabled(bool enabled) => Runtime?.SetModEnabled(enabled, false);
}