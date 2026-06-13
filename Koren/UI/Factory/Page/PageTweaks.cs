using Koren.Features.Tweaks;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace Koren.UI.Factory.Page;

// Tweaks tab. Hosts v1's non-visual tweaks — Disable Auto Pause and Block
// Scroll While Playing. The visual tweaks from the same v1 section live in
// the Visuals tab's "Visual Tweaks" category.
internal static class PageTweaks {
    public static void Create(RectTransform parent) {
        Tweaks.EnsureConf();
        TweaksSettings conf = Tweaks.Conf;
        TweaksSettings def = new();

        GameObject pad = new("Pad");
        pad.transform.SetParent(parent, false);

        RectTransform padRect = pad.AddComponent<RectTransform>();
        padRect.anchorMin = Vector2.zero;
        padRect.anchorMax = Vector2.one;
        padRect.pivot = new Vector2(0.5f, 0.5f);
        padRect.offsetMin = new Vector2(18f, 18f);
        padRect.offsetMax = new Vector2(-18f, -18f);

        GameObject viewport = new("Viewport");
        viewport.transform.SetParent(pad.transform, false);

        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportRect.pivot = new Vector2(0.5f, 0.5f);

        viewport.AddComponent<EmptyGraphic>().raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        GameObject content = new("Content");
        content.transform.SetParent(viewport.transform, false);

        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        pad.AddComponent<UIScrollController>().SetContent(contentRect, viewportRect);

        var autoPause = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.DisableAutoPause,
            conf.DisableAutoPause,
            v => {
                conf.DisableAutoPause = v;
                Tweaks.Save();
            },
            "Disable Auto Pause",
            "tw_nopause"
        );
        autoPause.Rect.AddToolTip(
            "DESC_TW_NOPAUSE",
            "While auto-play is on, the game pauses itself (e.g. when the window loses focus). This blocks those automatic pauses — pausing manually still works."
        );

        var blockScroll = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.BlockMouseWheelScrollWhilePlaying,
            conf.BlockMouseWheelScrollWhilePlaying,
            v => {
                conf.BlockMouseWheelScrollWhilePlaying = v;
                Tweaks.Save();
            },
            "Block Scroll While Playing",
            "tw_scroll"
        );
        blockScroll.Rect.AddToolTip(
            "DESC_TW_SCROLL",
            "Ignores mouse wheel input while a level is being played, so accidental scrolling can't affect the game mid-run."
        );

        var mainMenuSec = GenerateUI.Collapsible(content.transform, "Main Menu", startExpanded: false);

        var menuMusic = GenerateUI.Toggle(
            GenerateUI.Row(mainMenuSec.Body),
            def.DisableMenuMusic,
            conf.DisableMenuMusic,
            v => {
                conf.DisableMenuMusic = v;
                Tweaks.Save();
            },
            "Disable Menu Music",
            "tw_menumusic"
        );
        menuMusic.Rect.AddToolTip(
            "DESC_TW_MENUMUSIC",
            "Mutes the theme song on the title and island-select screens. Takes effect immediately; gameplay music is untouched."
        );

        var menuBpm = GenerateUI.Toggle(
            GenerateUI.Row(mainMenuSec.Body),
            def.MenuBpmEnabled,
            conf.MenuBpmEnabled,
            v => {
                conf.MenuBpmEnabled = v;
                Tweaks.Save();
            },
            "Custom Menu BPM",
            "tw_menubpm"
        );
        menuBpm.Rect.AddToolTip(
            "DESC_TW_MENUBPM",
            "Sets the menu rabbit's two speeds to the BPMs below instead of the default 1x / 2x. Re-open the menu to apply."
        );

        UISlider slowBpm = GenerateUI.Slider(
            GenerateUI.Row(mainMenuSec.Body),
            def.MenuSlowBpm, 30f, 600f, conf.MenuSlowBpm,
            Mathf.Round, v => conf.MenuSlowBpm = v,
            v => { conf.MenuSlowBpm = v; Tweaks.Save(); },
            "Slow BPM", "tw_menuslowbpm"
        );
        slowBpm.Format = "0";

        UISlider highBpm = GenerateUI.Slider(
            GenerateUI.Row(mainMenuSec.Body),
            def.MenuHighBpm, 30f, 600f, conf.MenuHighBpm,
            Mathf.Round, v => conf.MenuHighBpm = v,
            v => { conf.MenuHighBpm = v; Tweaks.Save(); },
            "High BPM", "tw_menuhighbpm"
        );
        highBpm.Format = "0";

        var resultsSec = GenerateUI.Collapsible(content.transform, "Detailed Results", startExpanded: false);

        var resultXAcc = GenerateUI.Toggle(
            GenerateUI.Row(resultsSec.Body),
            def.HideResultXAccuracy,
            conf.HideResultXAccuracy,
            v => {
                conf.HideResultXAccuracy = v;
                Tweaks.Save();
            },
            "Hide X-Accuracy",
            "tw_result_xacc"
        );
        resultXAcc.Rect.AddToolTip(
            "DESC_TW_RESULT_XACC",
            "Removes the X-Accuracy row from the detailed results screen."
        );

        var resultAcc = GenerateUI.Toggle(
            GenerateUI.Row(resultsSec.Body),
            def.HideResultAccuracy,
            conf.HideResultAccuracy,
            v => {
                conf.HideResultAccuracy = v;
                Tweaks.Save();
            },
            "Hide Accuracy",
            "tw_result_acc"
        );
        resultAcc.Rect.AddToolTip(
            "DESC_TW_RESULT_ACC",
            "Removes the Accuracy row from the detailed results screen."
        );

        var resultCheckpoints = GenerateUI.Toggle(
            GenerateUI.Row(resultsSec.Body),
            def.HideResultCheckpoints,
            conf.HideResultCheckpoints,
            v => {
                conf.HideResultCheckpoints = v;
                Tweaks.Save();
            },
            "Hide Checkpoints Used",
            "tw_result_checkpoints"
        );
        resultCheckpoints.Rect.AddToolTip(
            "DESC_TW_RESULT_CHECKPOINTS",
            "Removes the Checkpoints Used row from the detailed results screen."
        );

        var resultMaxKeys = GenerateUI.Toggle(
            GenerateUI.Row(resultsSec.Body),
            def.HideResultMaximumUsedKeys,
            conf.HideResultMaximumUsedKeys,
            v => {
                conf.HideResultMaximumUsedKeys = v;
                Tweaks.Save();
            },
            "Hide Maximum Used Keys",
            "tw_result_maxkeys"
        );
        resultMaxKeys.Rect.AddToolTip(
            "DESC_TW_RESULT_MAXKEYS",
            "Removes the Maximum Used Keys row from the detailed results screen."
        );
    }
}
