using GTweens.Contexts;
using Quartz.Async;
using Quartz.Compat;
using Quartz.Compat.Interface;
using Quartz.Core.Service;
using Quartz.Features.PlayCount;
using Quartz.Features.Combo;
using Quartz.Features.Editor;
using Quartz.Features.EffectRemover;
using Quartz.Features.GameOverlayFont;
using Quartz.Features.Optimizer;
using Quartz.Features.Judgement;
using Quartz.Features.KeyViewer;
using Quartz.Features.Nostalgia;
using Quartz.Features.OttoIcon;
using Quartz.Features.Panels;
using Quartz.Features.PlanetColors;
using Quartz.Features.ProgressBar;
using Quartz.Features.SongTitle;
using Quartz.Features.Status;
using Quartz.Features.Tweaks;
using Quartz.Features.UiHider;
using Quartz.IO;
using Quartz.Resource;
using Quartz.Update;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Quartz.Core;

// Owns the whole mod lifecycle. Service-based, ported from Overlayer's runtime
// but trimmed to the lean skeleton: no module discovery, tag engine, overlay
// canvas engine or Harmony patch controller — the only runtime feature is Status.
public sealed class QuartzRuntime {
    public Version Version { get; }
    public Assembly Assembly { get; }

    public QuartzLogger Logger { get; }

    public ModState State { get; }

    public event Action<bool, bool> OnModEnabledChanged;

    public PathService Paths { get; }

    public SettingsFile<CoreSettings> Config { get; }

    public LocalizationService Localization { get; private set; }

    public ResourceManager Resource { get; }
    public SpriteManager Sprite { get; }

    public GameObject RootObject { get; private set; }

    public GTweensContext TweensContext { get; }

    public readonly IQuartzHost Host;

    private readonly RuntimeServices services;
    private readonly RuntimeTicks ticks;

    private UIService uiService;
    private TweenService tweenService;
    private HarmonyService harmonyService;
    private PlayCount playCount;

    public QuartzRuntime(IQuartzHost host) {
        Host = host;

        Version = new Version(Info.Version);
        Assembly = Assembly.GetExecutingAssembly();
        Logger = new QuartzLogger(
            host.QuartzLogger
        );
        State = new ModState();
        Paths = new PathService(
            Path.Combine(
                host.QuartzFilePath,
                "Quartz"
            )
        );
        Config = new SettingsFile<CoreSettings>(Paths.ConfigPath);
        Resource = new ResourceManager(
            Assembly,
            "Quartz.Resource.Embedded."
        );
        Sprite = new SpriteManager(Resource);
        services = new RuntimeServices();
        ticks = new RuntimeTicks();
        TweensContext = new GTweensContext();
    }

    // One-time migration for the KorenResourcePack v2 -> Quartz rename: bring the
    // user's settings over from the old UserData/Koren folder into UserData/Quartz.
    // Done per-entry so it never clobbers the freshly-installed shipped files
    // (Lang, native/) already sitting in the new folder.
    private void MigrateLegacyData() {
        try {
            string ud = Host.QuartzFilePath;
            string oldRoot = Path.Combine(ud, "Koren");
            string newRoot = Path.Combine(ud, "Quartz");
            if(!Directory.Exists(oldRoot) ||
               string.Equals(Path.GetFullPath(oldRoot), Path.GetFullPath(newRoot), StringComparison.OrdinalIgnoreCase)) {
                return;
            }
            Directory.CreateDirectory(newRoot);
            int moved = 0;
            foreach(string entry in Directory.GetFileSystemEntries(oldRoot)) {
                string dest = Path.Combine(newRoot, Path.GetFileName(entry));
                if(File.Exists(dest) || Directory.Exists(dest)) {
                    continue; // keep the newer/shipped copy
                }
                try {
                    if(Directory.Exists(entry)) {
                        Directory.Move(entry, dest);
                    } else {
                        File.Move(entry, dest);
                    }
                    moved++;
                } catch(Exception e) {
                    Logger.Wrn($"[Startup] migrate '{Path.GetFileName(entry)}' failed: {e.Message}");
                }
            }
            if(moved > 0) {
                Logger.Msg($"[Startup] migrated {moved} item(s) from UserData/Koren to UserData/Quartz");
            }
        } catch(Exception e) {
            Logger.Wrn($"[Startup] legacy data migration failed: {e.Message}");
        }
    }

    public void Initialize() {
        // Per-phase timing so "the game took forever to start" reports can be
        // pinned to a phase from the log instead of guessed at.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var total = System.Diagnostics.Stopwatch.StartNew();

        // Carry settings over from the pre-rename UserData/Koren folder.
        MigrateLegacyData();

        Paths.Initialize();

        // The updater renames the running Quartz.dll to Quartz.dll.old when it
        // can't overwrite the mapped file; this session loaded the new one, so
        // the leftover is safe to delete now.
        try {
            string oldDll = Path.Combine(Host.ModsPath, "Quartz.dll.old");
            if(File.Exists(oldDll)) {
                File.Delete(oldDll);
            }
        } catch(Exception e) {
            Logger.Wrn($"[Startup] couldn't remove Quartz.dll.old: {e.Message}");
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

        // Install the XPerfect reentry guard if XPerfect is already loaded, and
        // retry on each scene load in case it loads after us (load-order safe).
        Quartz.Features.Interop.XPerfectRecursionGuard.TryApply(harmonyService.Harmony);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded +=
            (_, _) => Quartz.Features.Interop.XPerfectRecursionGuard.TryApply(harmonyService.Harmony);

        sw.Restart();
        SetModEnabled(Config.Data.Active, false);
        Logger.Msg($"[Startup] SetModEnabled took {sw.ElapsedMilliseconds} ms");

        // Background check so the Settings page can show any available update.
        UpdateService.Check();

        Logger.Msg($"[Startup] total {total.ElapsedMilliseconds} ms");

        // Detect Unity Mod Manager + its loaded mods (e.g. xperfect). Logs the
        // exact mod ids so interop can target them by id.
        if(Quartz.Features.Interop.UmmInterop.IsPresent) {
            Logger.Msg($"[Umm] active mods: [{string.Join(", ", Quartz.Features.Interop.UmmInterop.ActiveModIds())}]");
        } else {
            Logger.Msg("[Umm] not present");
        }

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
        Quartz.Resource.FontManager.Dispose();
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
            Nostalgia.Refresh();

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
            Features.Recorder.Recorder.Restore();
            Nostalgia.Restore();
            Features.AutoDeafen.AutoDeafen.Stop();

            Logger.Msg("Mod Disabled");
        }
    }

    private void CreateRootObject() {
        RootObject = new GameObject(
            "Quartz"
        );

        Object.DontDestroyOnLoad(
            RootObject
        );
    }
}
