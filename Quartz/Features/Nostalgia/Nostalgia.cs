using Quartz.Core;
using Quartz.IO;

namespace Quartz.Features.Nostalgia;

// "Back To The Past" — a faithful port of tjwogud's BackToThePast mod. Reverts
// modern ADOFAI behaviour, visuals and sounds to how the game used to be:
// legacy result/flash/twirl, hide difficulty/no-fail, old practice mode, SFX
// mutes, old editor buttons, old menu background, the alpha-warning skip, and
// the OldXO secret-level easter egg.
//
// Every toggle gates on MainCore.IsModEnabled && its own setting, the same way
// the Tweaks feature does. The patches live in the NostalgiaPatches.* partial
// files and are auto-applied by HarmonyService.PatchAll; scene-applied tweaks
// (hide difficulty/no-fail/announce, old background, old editor buttons) are
// re-run from their own Start/Awake postfixes and from Refresh().
public static partial class Nostalgia {
    public static SettingsFile<NostalgiaSettings> ConfMgr { get; private set; }
    public static NostalgiaSettings Conf => ConfMgr?.Data;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<NostalgiaSettings>(
            Path.Combine(MainCore.Paths.RootPath, "Nostalgia.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    public static bool Enabled {
        get {
            EnsureConf();
            return MainCore.IsModEnabled;
        }
    }

    // === Per-toggle gates (read by the patches) ===
    public static bool ShouldLegacyResult => Enabled && Conf.LegacyResult;
    public static bool ShouldNoResult => Enabled && Conf.NoResult;
    public static bool ShouldHideDifficulty => Enabled && Conf.HideDifficulty;
    public static bool ShouldHideNoFail => Enabled && Conf.HideNoFail;
    public static bool ShouldOldPracticeMode => Enabled && Conf.OldPracticeMode;
    public static bool ShouldShowSmallSpeedChange => Enabled && Conf.ShowSmallSpeedChange;
    public static bool ShouldLegacyFlash => Enabled && Conf.LegacyFlash;
    public static bool ShouldNoJudgeAnimation => Enabled && Conf.NoJudgeAnimation;
    public static bool ShouldLateJudgement => Enabled && Conf.LateJudgement;
    public static bool ShouldForceJudgeCount => Enabled && Conf.ForceJudgeCount;
    public static bool ShouldLegacyTwirl => Enabled && Conf.LegacyTwirl;
    public static bool ShouldSpace360Tile => Enabled && Conf.Space360Tile;
    public static bool ShouldWeakAuto => Enabled && Conf.WeakAuto;
    public static bool ShouldWhiteAuto => Enabled && Conf.WhiteAuto;
    public static bool ShouldLegacyTexts => Enabled && Conf.LegacyTexts;
    public static bool ShouldDisablePurePerfectSound => Enabled && Conf.DisablePurePerfectSound;
    public static bool ShouldDisableWindSound => Enabled && Conf.DisableWindSound;
    public static bool ShouldDisableCountdownSound => Enabled && Conf.DisableCountdownSound;
    public static bool ShouldDisableEndingSound => Enabled && Conf.DisableEndingSound;
    public static bool ShouldDisableNewBestSound => Enabled && Conf.DisableNewBestSound;
    public static bool ShouldDisableAlphaWarning => Enabled && Conf.DisableAlphaWarning;
    public static bool ShouldDisableAnnounceSign => Enabled && Conf.DisableAnnounceSign;
    public static bool ShouldLegacyCLS => Enabled && Conf.LegacyCLS;

    // Applies the death-sound mute to the live game static. BTTP flips
    // GCS.playDeathSound directly; we mirror that whenever settings change or a
    // scene/mod-state refresh happens.
    public static void ApplyDeathSound() {
        try {
            if(Enabled) {
                GCS.playDeathSound = !Conf.DisableDeathSound;
            }
        } catch { }
    }

    // Rebuild editor floor icons/events — used after toggling the speed-change
    // or twirl display so the change shows immediately in the open editor.
    public static void ApplyEditorFloors() {
        try {
            if(scnEditor.instance != null) {
                scnEditor.instance.ApplyEventsToFloors();
            }
        } catch { }
    }

    // Re-apply every scene-dependent tweak to whatever scene is live. Called on
    // mod-enable and after a toggle that needs an immediate visible effect.
    public static void Refresh() {
        EnsureConf();
        ApplyDeathSound();
        try { RDC.useOldAuto = ShouldWeakAuto; } catch { }
        ToggleDifficulty(!ShouldHideDifficulty);
        ToggleNoFail(!ShouldHideNoFail);
        ToggleSign(!ShouldDisableAnnounceSign);
        SetBackground();
        ChangeEditorButtons(Enabled && Conf.LegacyEditorButtonsPositions);
        RemoveShadowAddOutline(Enabled && Conf.LegacyEditorButtonsDesigns);
    }

    // Live-toggle the Legacy CLS panel (real implementation in the LegacyCLS
    // partial). Stubbed here so the page compiles before that phase lands.
    static partial void ToggleLegacyCLSImpl(bool active);
    public static void ToggleLegacyCLS(bool active) => ToggleLegacyCLSImpl(active);

    // Mod turned off — put back everything the scene-applied tweaks changed.
    public static void Restore() {
        try { GCS.playDeathSound = true; } catch { }
        try { RDC.useOldAuto = false; } catch { }
        ToggleDifficulty(true);
        ToggleNoFail(true);
        ToggleSign(true);
        ChangeEditorButtons(false);
        RemoveShadowAddOutline(false);
        // OldBackground: index 2 is the live/default background.
        SetBackground(forceDefault: true);
    }
}
