using Koren.Core;
using Koren.Features.Status;
using Koren.Resource;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace Koren.UI.Factory.Page;

// Settings page for the Status HUD feature. Every control is wired straight to
// StatusOverlay's config and live-applied, so it doubles as the demo of the
// full widget set: toggle, input, slider, and color picker.
internal static class PageStatus {
    public static void Create(RectTransform parent) {
        StatusOverlay.EnsureConf();
        StatusSettings conf = StatusOverlay.Conf;
        StatusSettings def = new();

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

        void Save() => StatusOverlay.Save();

        // Header.
        GenerateUI.AddTextH1(GenerateUI.Row(content.transform)).text = "Status HUD";

        // Enable.
        GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.Enabled,
            conf.Enabled,
            v => { conf.Enabled = v; StatusOverlay.Apply(); Save(); },
            "Enable Status HUD",
            "status_enabled"
        ).Rect.AddToolTip("DESC_STATUS_ENABLED", "Show a draggable HUD with live game status.");

        // Stat field toggles (only shown on the HUD while in a level).
        GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.ShowProgress,
            conf.ShowProgress,
            v => { conf.ShowProgress = v; Save(); },
            "Show Progress",
            "status_progress"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.ShowAccuracy,
            conf.ShowAccuracy,
            v => { conf.ShowAccuracy = v; Save(); },
            "Show Accuracy",
            "status_accuracy"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.ShowXAccuracy,
            conf.ShowXAccuracy,
            v => { conf.ShowXAccuracy = v; Save(); },
            "Show XAccuracy",
            "status_xaccuracy"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.ShowMaxXAccuracy,
            conf.ShowMaxXAccuracy,
            v => { conf.ShowMaxXAccuracy = v; Save(); },
            "Show Max XAccuracy",
            "status_maxxaccuracy"
        );

        // Prefix text.
        UIInput prefix = GenerateUI.Input(
            GenerateUI.Row(content.transform),
            def.Prefix,
            conf.Prefix,
            v => { conf.Prefix = v; Save(); },
            "Prefix",
            MainCore.Spr.Get(UISprite.Text128),
            "status_prefix"
        );
        prefix.InputField.characterLimit = 32;

        // Font size.
        static float fontFilter(float v) => Mathf.Clamp(Mathf.Round(v), 12f, 48f);
        UISlider font = GenerateUI.Slider(
            GenerateUI.Row(content.transform),
            def.FontSize,
            12f,
            48f,
            conf.FontSize,
            fontFilter,
            null,
            null,
            "Font Size",
            "status_fontsize"
        );
        font.Format = "0 px";
        font.OnChanged = v => { conf.FontSize = v; StatusOverlay.Apply(); };
        font.OnComplete = v => { conf.FontSize = v; StatusOverlay.Apply(); Save(); };

        // Text color (color picker).
        GenerateUI.ColorPicker(
            GenerateUI.Row(content.transform),
            def.GetTextColor(),
            conf.GetTextColor(),
            c => { conf.SetTextColor(c); StatusOverlay.Apply(); },
            c => { conf.SetTextColor(c); StatusOverlay.Apply(); Save(); },
            "Text Color",
            "status_textcolor"
        );

        // Background.
        GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.BackgroundEnabled,
            conf.BackgroundEnabled,
            v => { conf.BackgroundEnabled = v; StatusOverlay.Apply(); Save(); },
            "Background Panel",
            "status_background"
        );
    }
}
