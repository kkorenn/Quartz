using GTweens.Contexts;
using Koren.Async;
using Koren.Compat;
using Koren.Compat.Interface;
using Koren.Core.Service;
using Koren.Features.PlayCount;
using Koren.Features.Combo;
using Koren.Features.Editor;
using Koren.Features.EffectRemover;
using Koren.Features.GameOverlayFont;
using Koren.Features.Optimizer;
using Koren.Features.Judgement;
using Koren.Features.KeyViewer;
using Koren.Features.OttoIcon;
using Koren.Features.Panels;
using Koren.Features.PlanetColors;
using Koren.Features.ProgressBar;
using Koren.Features.SongTitle;
using Koren.Features.Status;
using Koren.Features.Tweaks;
using Koren.Features.UiHider;
using Koren.IO;
using Koren.Resource;
using Koren.Update;
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
        // Per-phase timing so "the game took forever to start" reports can be
        // pinned to a phase from the log instead of guessed at.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var total = System.Diagnostics.Stopwatch.StartNew();

        Paths.Initialize();

        // The updater renames the running Koren.dll to Koren.dll.old when it
        // can't overwrite the mapped file; this session loaded the new one, so
        // the leftover is safe to delete now.
        try {
            string oldDll = Path.Combine(Host.ModsPath, "Koren.dll.old");
            if(File.Exists(oldDll)) {
                File.Delete(oldDll);
            }
        } catch(Exception e) {
            Logger.Wrn($"[Startup] couldn't remove Koren.dll.old: {e.Message}");
        }

        CreateRootObject();

        RootObject.AddComponent<MainThread>();

        Config.Load();

        // Needs the config loaded; first run captures the live settings as
        // the initial "Default" profile.
        ProfileManager.Initialize();

        Logger.Msg($"[Startup] paths + config took {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        FontManager.Initialize();
        // Re-apply the font to ADOFAI's own text whenever the selection changes
        // or a new scene loads.
        FontManager.OnFontChanged += GameOverlayFont.ApplyFontChange;
        GameOverlayFont.Initialize();
        Logger.Msg($"[Startup] FontManager took {sw.ElapsedMilliseconds} ms");

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

        // Engine-level performance toggles (GC scheduling, process priority,
        // background execution). Static feature, so it just registers its tick.
        Optimizer.Initialize();

        ticks.Add(playCount);

        ticks.Add(uiService);
        ticks.Add(tweenService);
        ticks.Add(Optimizer.Ticker);

        // Keeps the editor's property-row template in sync with the
        // "Horizontal Properties" toggle (and reverts it when the mod is off).
        ticks.Add(EditorFeature.Ticker);

        services.Initialize(Logger);

        sw.Restart();
        SetModEnabled(Config.Data.Active, false);
        Logger.Msg($"[Startup] SetModEnabled took {sw.ElapsedMilliseconds} ms");

        // Background check so the Settings page can show any available update.
        UpdateService.Check();

        Logger.Msg($"[Startup] total {total.ElapsedMilliseconds} ms");
        Logger.Msg("Hello");
    }

    public void Tick() => ticks.Tick();

    public void Dispose() {
        SetModEnabled(false, true);

        Config.Save();

        // Keep the active profile in sync with the final on-disk settings so
        // switching profiles next session starts from what the user last saw.
        ProfileManager.CaptureActive();

        services.Dispose();

        Sprite.Dispose();
        // Destroy the dynamically-built font assets (atlas + material + source
        // Font) before ResourceManager tears down the default font they fall
        // back to.
        Koren.Resource.FontManager.Dispose();
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
            PanelsOverlay.Initialize(RootObject);
            ComboOverlay.Initialize(RootObject);
            ProgressBarOverlay.Initialize(RootObject);
            JudgementOverlay.Initialize(RootObject);
            KeyViewerOverlay.Initialize(RootObject);
            SongTitleOverlay.Initialize(RootObject);

            // Re-disable editor Save buttons if the Effect Remover is on.
            EffectRemover.RefreshEditorSaveButtons();

            // Re-apply the visual tweaks and planet colors to whatever scene
            // is live.
            Tweaks.RefreshAll();
            PlanetColors.Refresh();
            OttoIcon.Refresh();
            Optimizer.Apply();
            GameOverlayFont.Refresh();

            OnModEnabledChanged?.Invoke(true, isDispose);

            Logger.Msg("Mod Enabled");
        } else {
            OnModEnabledChanged?.Invoke(false, isDispose);

            SongTitleOverlay.Dispose();
            KeyViewerOverlay.Dispose();
            JudgementOverlay.Dispose();
            ProgressBarOverlay.Dispose();
            ComboOverlay.Dispose();
            PanelsOverlay.Dispose();

            // The remover's editor-save lock shouldn't outlive the mod.
            EffectRemover.RestoreEditorSaveButtons();

            // Put back the particles/glows/colors/UI the features changed.
            Tweaks.RestoreAll();
            PlanetColors.Restore();
            OttoIcon.Restore();
            UiHider.Restore();
            Optimizer.Restore();
            GameOverlayFont.Restore();
            EditorFeature.Restore();
            Features.AutoDeafen.AutoDeafen.Stop();

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
