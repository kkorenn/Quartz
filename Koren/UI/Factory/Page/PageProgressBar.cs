using Koren.Core;
using Koren.Features.ProgressBar;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace Koren.UI.Factory.Page;

// Settings page for the top-of-screen Progress Bar HUD. Mirrors PageStatus's
// scrolling-content pattern: toggles, sliders, and color pickers wired
// straight to ProgressBarOverlay's config and live-applied.
internal static class PageProgressBar {
    public static void Create(RectTransform parent) {
        ProgressBarOverlay.EnsureConf();
        ProgressBarSettings conf = ProgressBarOverlay.Conf;
        ProgressBarSettings def = new();

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

        void Save() => ProgressBarOverlay.Save();

        GenerateUI.AddTextH1(GenerateUI.Row(content.transform)).text = "Progress Bar";

        GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.Enabled,
            conf.Enabled,
            v => { conf.Enabled = v; ProgressBarOverlay.Apply(); Save(); },
            "Enable Progress Bar",
            "progressbar_enabled"
        );

        // Geometry.
        static float widthFilter(float v) => Mathf.Clamp(Mathf.Round(v), 200f, 1800f);
        UISlider width = GenerateUI.Slider(
            GenerateUI.Row(content.transform),
            def.Width,
            200f, 1800f, conf.Width, widthFilter, null, null,
            "Width", "progressbar_width"
        );
        width.Format = "0 px";
        width.OnChanged = v => { conf.Width = v; ProgressBarOverlay.Apply(); };
        width.OnComplete = v => { conf.Width = v; ProgressBarOverlay.Apply(); Save(); };

        static float heightFilter(float v) => Mathf.Clamp(Mathf.Round(v), 2f, 60f);
        UISlider height = GenerateUI.Slider(
            GenerateUI.Row(content.transform),
            def.Height,
            2f, 60f, conf.Height, heightFilter, null, null,
            "Height", "progressbar_height"
        );
        height.Format = "0 px";
        height.OnChanged = v => { conf.Height = v; ProgressBarOverlay.Apply(); };
        height.OnComplete = v => { conf.Height = v; ProgressBarOverlay.Apply(); Save(); };

        static float offsetFilter(float v) => Mathf.Clamp(Mathf.Round(v), 0f, 200f);
        UISlider offset = GenerateUI.Slider(
            GenerateUI.Row(content.transform),
            def.TopOffset,
            0f, 200f, conf.TopOffset, offsetFilter, null, null,
            "Top Offset", "progressbar_topoffset"
        );
        offset.Format = "0 px";
        offset.OnChanged = v => { conf.TopOffset = v; ProgressBarOverlay.Apply(); };
        offset.OnComplete = v => { conf.TopOffset = v; ProgressBarOverlay.Apply(); Save(); };

        static float roundingFilter(float v) => Mathf.Clamp(Mathf.Round(v), 0f, 30f);
        UISlider rounding = GenerateUI.Slider(
            GenerateUI.Row(content.transform),
            def.Rounding,
            0f, 30f, conf.Rounding, roundingFilter, null, null,
            "Corner Rounding", "progressbar_rounding"
        );
        rounding.Format = "0 px";
        rounding.OnChanged = v => { conf.Rounding = v; ProgressBarOverlay.Apply(); };
        rounding.OnComplete = v => { conf.Rounding = v; ProgressBarOverlay.Apply(); Save(); };

        static float outlineFilter(float v) => Mathf.Clamp(Mathf.Round(v * 4f) * 0.25f, 0f, 8f);
        UISlider outlineThick = GenerateUI.Slider(
            GenerateUI.Row(content.transform),
            def.OutlineThickness,
            0f, 8f, conf.OutlineThickness, outlineFilter, null, null,
            "Outline Thickness", "progressbar_outlinethickness"
        );
        outlineThick.Format = "0.## px";
        outlineThick.OnChanged = v => { conf.OutlineThickness = v; ProgressBarOverlay.Apply(); };
        outlineThick.OnComplete = v => { conf.OutlineThickness = v; ProgressBarOverlay.Apply(); Save(); };

        // Colors.
        GenerateUI.ColorPicker(
            GenerateUI.Row(content.transform),
            def.GetFillColor(),
            conf.GetFillColor(),
            c => { conf.SetFillColor(c); ProgressBarOverlay.Apply(); },
            c => { conf.SetFillColor(c); ProgressBarOverlay.Apply(); Save(); },
            "Fill Color",
            "progressbar_fillcolor"
        );

        GenerateUI.ColorPicker(
            GenerateUI.Row(content.transform),
            def.GetBackColor(),
            conf.GetBackColor(),
            c => { conf.SetBackColor(c); ProgressBarOverlay.Apply(); },
            c => { conf.SetBackColor(c); ProgressBarOverlay.Apply(); Save(); },
            "Background Color",
            "progressbar_backcolor"
        );

        GenerateUI.ColorPicker(
            GenerateUI.Row(content.transform),
            def.GetOutlineColor(),
            conf.GetOutlineColor(),
            c => { conf.SetOutlineColor(c); ProgressBarOverlay.Apply(); },
            c => { conf.SetOutlineColor(c); ProgressBarOverlay.Apply(); Save(); },
            "Outline Color",
            "progressbar_outlinecolor"
        );

        GenerateUI.Button(
            GenerateUI.Row(content.transform),
            () => ProgressBarOverlay.ResetPosition(),
            "Reset Position",
            "progressbar_resetposition"
        );
    }
}
