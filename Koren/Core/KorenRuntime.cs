using GTweens.Contexts;
using Koren.Async;
using Koren.Compat;
using Koren.Compat.Interface;
using Koren.Core.Service;
using Koren.Features.PlayCount;
using Koren.Features.ProgressBar;
using Koren.Features.Status;
using Koren.IO;
using Koren.Resource;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Koren.Core;

// Owns the whole mod lifecycle. Service-based, ported from Overlayer's runtime
// but trimmed to the lean skeleton: no module discovery, tag engine, overlay
// canvas engine or Harmony patch controller — the only runtime feature is Status.
public sealed class KorenRuntime {
    public Version Version { get; }
    public Assembly Assembly { get; }

    public KorenLogger Logger { get; }

    public ModState State { get; }

    public event Action<bool, bool> OnModEnabledChanged;

    public PathService Paths { get; }

    public SettingsFile<CoreSettings> Config { get; }

    public LocalizationService Localization { get; private set; }

    public ResourceManager Resource { get; }
    public SpriteManager Sprite { get; }

    public GameObject RootObject { get; private set; }

    public GTweensContext TweensContext { get; }

    public readonly IKorenHost Host;

    private readonly RuntimeServices services;
    private readonly RuntimeTicks ticks;

    private UIService uiService;
    private TweenService tweenService;
    private HarmonyService harmonyService;
    private PlayCount playCount;

    public KorenRuntime(IKorenHost host) {
        Host = host;

        Version = new Version(Info.Version);
        Assembly = Assembly.GetExecutingAssembly();
        Logger = new KorenLogger(
            host.KorenLogger
        );
        State = new ModState();
        Paths = new PathService(
            Path.Combine(
                host.KorenFilePath,
                "Koren"
            )
        );
        Config = new SettingsFile<CoreSettings>(Paths.ConfigPath);
        Resource = new ResourceManager(
            Assembly,
            "Koren.Resource.Embedded."
        );
        Sprite = new SpriteManager(Resource);
        services = new RuntimeServices();
        ticks = new RuntimeTicks();
        TweensContext = new GTweensContext();
    }

    public void Initialize() {
        Paths.Initialize();

        CreateRootObject();

        RootObject.AddComponent<MainThread>();

        Config.Load();

        FontManager.Initialize();

        Localization = new LocalizationService(Paths.LangPath, Config, Logger);

        uiService = new UIService();
        tweenService = new TweenService(TweensContext);
        harmonyService = new HarmonyService();
        playCount = new PlayCount();

        services.Add(Localization);
        services.Add(uiService);
        services.Add(tweenService);
        services.Add(playCount);
        services.Add(harmonyService);

        ticks.Add(playCount);

        ticks.Add(uiService);
        ticks.Add(tweenService);

        services.Initialize();

        SetModEnabled(Config.Data.Active, false);

        Logger.Msg("Hello");
    }

    public void Tick() => ticks.Tick();

    public void Dispose() {
        SetModEnabled(false, true);

        Config.Save();

        services.Dispose();

        Sprite.Dispose();
        Resource.Dispose();

        if(RootObject != null) {
            Object.Destroy(RootObject);

            RootObject = null;
        }

        Logger.Msg("Bye");
    }

    public void SetModEnabled(bool enabled, bool isDispose) {
        if(State.IsEnabled == enabled) {
            return;
        }

        State.IsEnabled = enabled;

        if(!isDispose) {
            Config.Data.Active = enabled;
            Config.RequestSave();
        }

        if(enabled) {
            StatusOverlay.Initialize(RootObject);
            ProgressBarOverlay.Initialize(RootObject);

            OnModEnabledChanged?.Invoke(true, isDispose);

            Logger.Msg("Mod Enabled");
        } else {
            OnModEnabledChanged?.Invoke(false, isDispose);

            ProgressBarOverlay.Dispose();
            StatusOverlay.Dispose();

            Logger.Msg("Mod Disabled");
        }
    }

    private void CreateRootObject() {
        RootObject = new GameObject(
            "Koren"
        );

        Object.DontDestroyOnLoad(
            RootObject
        );
    }
}
