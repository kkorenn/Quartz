using System;
using Quartz.Features.Nostalgia;
using Quartz.UI.Generator;
using Quartz.UI.Utility;
using UnityEngine;

namespace Quartz.UI.Factory.Page;

// The BackToThePast ("Nostalgia") toggles, dropped into each host page (Editor /
// Visuals / Gameplay / Tweaks) under a big "Nostalgia" heading (not a
// collapsible). The feature backend lives in Quartz.Features.Nostalgia; this just
// builds the rows. Localization ids are unchanged (nost_*), so the existing
// NOST_* / DESC_NOST_* lang keys still apply.
internal static class NostalgiaUI {
    private static NostalgiaSettings Conf => Nostalgia.Conf;
    private static NostalgiaSettings Def => def ??= new NostalgiaSettings();
    private static NostalgiaSettings def;

    private static void Heading(Transform p, string text) {
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(p)), "NOSTALGIA", text);
    }

    private static void Toggle(Transform p, bool dft, bool val, Action<bool> set,
                               string label, string id, string desc) {
        var t = GenerateUI.Toggle(GenerateUI.Row(p), dft, val, set, label, id);
        t.Rect.AddToolTip("DESC_" + id.ToUpperInvariant(), desc);
    }

    private static void Slider(Transform p, float dft, float min, float max, float val,
                               Func<float, float> filter, Action<float> done,
                               string label, string id, string desc) {
        var s = GenerateUI.Slider(GenerateUI.Row(p), dft, min, max, val, filter, _ => { }, done, label, id);
        s.Rect.AddToolTip("DESC_" + id.ToUpperInvariant(), desc);
    }

    // === Editor tab ===
    public static void AddEditorSection(Transform b) {
        Nostalgia.EnsureConf();
        Heading(b, "Nostalgia");

        Toggle(b, Def.HideDifficulty, Conf.HideDifficulty,
            v => { Conf.HideDifficulty = v; Nostalgia.Save(); Nostalgia.Refresh(); },
            "Hide Difficulty", "nost_hidedifficulty",
            "Hides the difficulty stars/selector and forces Strict, like the old game.");

        Toggle(b, Def.HideNoFail, Conf.HideNoFail,
            v => { Conf.HideNoFail = v; Nostalgia.Save(); Nostalgia.Refresh(); },
            "Hide No-Fail", "nost_hidenofail",
            "Hides the editor No-Fail button (and level-select indicator) and blocks turning No-Fail on.");

        Toggle(b, Def.Space360Tile, Conf.Space360Tile,
            v => { Conf.Space360Tile = v; Nostalgia.Save(); },
            "Space 360 Tile", "nost_space360",
            "Pressing Space on a single selected tile adds a 360° spin tile, like the old editor shortcut.");

        Toggle(b, Def.WeakAuto, Conf.WeakAuto,
            v => { Conf.WeakAuto = v; Nostalgia.Save(); Nostalgia.Refresh(); },
            "Weak Auto", "nost_weakauto",
            "Restores the old, weaker auto-play behaviour.");

        Toggle(b, Def.WhiteAuto, Conf.WhiteAuto,
            v => { Conf.WhiteAuto = v; Nostalgia.Save(); },
            "White Auto", "nost_whiteauto",
            "Keeps the auto indicator white on high-BPM levels, as it used to be.");

        Toggle(b, Def.LegacyEditorButtonsPositions, Conf.LegacyEditorButtonsPositions,
            v => { Conf.LegacyEditorButtonsPositions = v; Nostalgia.Save(); Nostalgia.Refresh(); },
            "Legacy Editor Button Positions", "nost_editorbtnpos",
            "Moves the Auto / No-Fail / Difficulty editor buttons back to their old positions.");

        Toggle(b, Def.LegacyEditorButtonsDesigns, Conf.LegacyEditorButtonsDesigns,
            v => { Conf.LegacyEditorButtonsDesigns = v; Nostalgia.Save(); Nostalgia.Refresh(); },
            "Legacy Editor Button Designs", "nost_editorbtndesign",
            "Gives the editor buttons the old outline-instead-of-shadow styling.");

        Toggle(b, Def.LegacyTexts, Conf.LegacyTexts,
            v => { Conf.LegacyTexts = v; Nostalgia.Save(); },
            "Legacy Texts (KR)", "nost_legacytexts",
            "Reverts a few Korean editor strings to their old wording. Korean only; a scene reload applies it fully.");
    }

    // === Visuals tab ===
    public static void AddVisualsSection(Transform b) {
        Nostalgia.EnsureConf();
        Heading(b, "Nostalgia");

        Toggle(b, Def.NoResult, Conf.NoResult,
            v => { Conf.NoResult = v; Nostalgia.Save(); },
            "Hide Result", "nost_noresult",
            "Blanks the congratulations/results text shown when you finish a level.");

        Toggle(b, Def.ShowSmallSpeedChange, Conf.ShowSmallSpeedChange,
            v => { Conf.ShowSmallSpeedChange = v; Nostalgia.Save(); Nostalgia.ApplyEditorFloors(); },
            "Show Small Speed Change", "nost_speedchange",
            "Shows a rabbit/snail icon on tiles whose speed changed even slightly, like the old editor.");

        Toggle(b, Def.ShowDetailSpeedChange, Conf.ShowDetailSpeedChange,
            v => { Conf.ShowDetailSpeedChange = v; Nostalgia.Save(); Nostalgia.ApplyEditorFloors(); },
            "Detailed Speed Change", "nost_speedchange_detail",
            "Only show the speed-change icon when the change exceeds the minimum below.");

        Slider(b, Def.MinBpmToShowSpeedChange, 0f, 0.5f, Conf.MinBpmToShowSpeedChange,
            f => Mathf.Round(f * 10000f) / 10000f,
            v => { Conf.MinBpmToShowSpeedChange = v; Nostalgia.Save(); Nostalgia.ApplyEditorFloors(); },
            "Min Speed Change", "nost_speedchange_min",
            "Relative speed difference under which a tile counts as 'same speed'.");

        Toggle(b, Def.LegacyFlash, Conf.LegacyFlash,
            v => { Conf.LegacyFlash = v; Nostalgia.Save(); },
            "Legacy Flash", "nost_legacyflash",
            "Re-fires the old red full-screen flash when you take a hit.");

        Toggle(b, Def.NoJudgeAnimation, Conf.NoJudgeAnimation,
            v => { Conf.NoJudgeAnimation = v; Nostalgia.Save(); },
            "No Judgement Animation", "nost_nojudgeanim",
            "Snaps the hit-judgement text into place instead of the modern pop animation.");

        Toggle(b, Def.LateJudgement, Conf.LateJudgement,
            v => { Conf.LateJudgement = v; Nostalgia.Save(); },
            "Late Judgement", "nost_latejudge",
            "Shows the judgement text on the previous tile, the way old builds placed it.");

        Toggle(b, Def.LegacyTwirl, Conf.LegacyTwirl,
            v => { Conf.LegacyTwirl = v; Nostalgia.Save(); Nostalgia.ApplyEditorFloors(); },
            "Legacy Twirl", "nost_legacytwirl",
            "Draws spin tiles with the old swirl-and-arrow sprite instead of the modern icon.");

        Toggle(b, Def.TwirlWithoutArrow, Conf.TwirlWithoutArrow,
            v => { Conf.TwirlWithoutArrow = v; Nostalgia.Save(); Nostalgia.ApplyEditorFloors(); },
            "Twirl Without Arrow", "nost_twirlnoarrow",
            "Use the legacy swirl but omit the direction arrow.");
    }

    // === Gameplay tab ===
    public static void AddGameplaySection(Transform b) {
        Nostalgia.EnsureConf();
        Heading(b, "Nostalgia");

        Toggle(b, Def.OldPracticeMode, Conf.OldPracticeMode,
            v => { Conf.OldPracticeMode = v; Nostalgia.Save(); },
            "Old Practice Mode", "nost_oldpractice",
            "Press P on the fail screen to drop straight into practice mode, the old way.");
    }

    // === Tweaks tab (SFX + Etc) ===
    public static void AddTweaksSection(Transform b) {
        Nostalgia.EnsureConf();
        Heading(b, "Nostalgia");

        Toggle(b, Def.DisablePurePerfectSound, Conf.DisablePurePerfectSound,
            v => { Conf.DisablePurePerfectSound = v; Nostalgia.Save(); },
            "Disable Pure Perfect Sound", "nost_sfx_pureperfect",
            "Mutes the chime played on a pure-perfect hit.");

        Toggle(b, Def.DisableWindSound, Conf.DisableWindSound,
            v => { Conf.DisableWindSound = v; Nostalgia.Save(); },
            "Disable Wind Sound", "nost_sfx_wind",
            "Mutes the screen-wipe wind whoosh.");

        Toggle(b, Def.DisableDeathSound, Conf.DisableDeathSound,
            v => { Conf.DisableDeathSound = v; Nostalgia.Save(); Nostalgia.ApplyDeathSound(); },
            "Disable Death Sound", "nost_sfx_death",
            "Mutes the death/explosion sound on a fail.");

        Toggle(b, Def.DisableCountdownSound, Conf.DisableCountdownSound,
            v => { Conf.DisableCountdownSound = v; Nostalgia.Save(); },
            "Disable Countdown Sound", "nost_sfx_countdown",
            "Mutes the pre-level countdown takeoff sound.");

        Toggle(b, Def.DisableEndingSound, Conf.DisableEndingSound,
            v => { Conf.DisableEndingSound = v; Nostalgia.Save(); },
            "Disable Ending Sound", "nost_sfx_ending",
            "Mutes the ending cymbal at the end of a level.");

        Toggle(b, Def.DisableNewBestSound, Conf.DisableNewBestSound,
            v => { Conf.DisableNewBestSound = v; Nostalgia.Save(); },
            "Disable New Best Sound", "nost_sfx_newbest",
            "Plays the normal explosion instead of the new-best jingle.");

        Toggle(b, Def.LegacyCLS, Conf.LegacyCLS,
            v => { Conf.LegacyCLS = v; Nostalgia.Save(); Nostalgia.ToggleLegacyCLS(v); },
            "Legacy Custom Level Select", "nost_legacycls",
            "Restores the old custom-level-select search bar and F/S/N/O/Delete keyboard shortcuts.");

        Toggle(b, Def.DisableAlphaWarning, Conf.DisableAlphaWarning,
            v => { Conf.DisableAlphaWarning = v; Nostalgia.Save(); },
            "Disable Alpha Warning", "nost_alphawarning",
            "Skips the alpha-build warning splash on startup.");

        Toggle(b, Def.DisableAnnounceSign, Conf.DisableAnnounceSign,
            v => { Conf.DisableAnnounceSign = v; Nostalgia.Save(); Nostalgia.Refresh(); },
            "Disable Announcement Sign", "nost_announce",
            "Hides the announcement/news sign in the lobby.");

        Toggle(b, Def.OldBackground, Conf.OldBackground,
            v => { Conf.OldBackground = v; Nostalgia.Save(); Nostalgia.SetBackground(); },
            "Old Background", "nost_oldbg",
            "Replaces the lobby background with the old one.");

        Toggle(b, Conf.OldBackgroundIndex == 1, Conf.OldBackgroundIndex == 1,
            v => { Conf.OldBackgroundIndex = v ? 1 : 0; Nostalgia.Save(); Nostalgia.SetBackground(); },
            "Use Variant B", "nost_oldbg_b",
            "Choose the second old-background variant (off = A, on = B).");
    }
}
