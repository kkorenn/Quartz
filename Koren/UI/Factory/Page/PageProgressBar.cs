using Koren.Core;
using Koren.Features.ProgressBar;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using UnityEngine;

namespace Koren.UI.Factory.Page;

// Progress Bar settings section for the Overlay tab.
internal static class PageProgressBar {
    public static void AppendTo(Transform content) {
        ProgressBarOverlay.EnsureConf();
        ProgressBarSettings conf = ProgressBarOverlay.Conf;
        ProgressBarSettings def = new();

        void Save() => ProgressBarOverlay.Save();

        var sec = GenerateUI.Collapsible(
            content, "Progress Bar", startExpanded: false,
            v => { conf.Enabled = v; ProgressBarOverlay.Apply(); Save(); },
            conf.Enabled
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.PrefillStart,
            conf.PrefillStart,
            v => { conf.PrefillStart = v; Save(); },
            "Pre-fill to Start Position",
            "progressbar_prefillstart"
        ).Rect.AddToolTip(
            "DESC_PROGRESSBAR_PREFILLSTART",
            "When a run starts mid-chart (checkpoint or editor play), the bar starts already filled up to that point instead of starting empty."
        );

        // === Size ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_SIZE", "Size");

        static float widthFilter(float v) => Mathf.Clamp(Mathf.Round(v), 200f, 1800f);
        UISlider width = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.Width,
            200f, 1800f, conf.Width, widthFilter, null, null,
            "Width", "progressbar_width"
        );
        width.Format = "0 px";
        width.OnChanged = v => { conf.Width = v; ProgressBarOverlay.Apply(); };
        width.OnComplete = v => { conf.Width = v; ProgressBarOverlay.Apply(); Save(); };

        static float heightFilter(float v) => Mathf.Clamp(Mathf.Round(v), 2f, 60f);
        UISlider height = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.Height,
            2f, 60f, conf.Height, heightFilter, null, null,
            "Height", "progressbar_height"
        );
        height.Format = "0 px";
        height.OnChanged = v => { conf.Height = v; ProgressBarOverlay.Apply(); };
        height.OnComplete = v => { conf.Height = v; ProgressBarOverlay.Apply(); Save(); };

        static float offsetFilter(float v) => Mathf.Clamp(Mathf.Round(v), 0f, 200f);
        UISlider offset = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.TopOffset,
            0f, 200f, conf.TopOffset, offsetFilter, null, null,
            "Top Offset", "progressbar_topoffset"
        );
        offset.Format = "0 px";
        offset.OnChanged = v => { conf.TopOffset = v; ProgressBarOverlay.Apply(); };
        offset.OnComplete = v => { conf.TopOffset = v; ProgressBarOverlay.Apply(); Save(); };

        static float roundingFilter(float v) => Mathf.Clamp(Mathf.Round(v), 0f, 30f);
        UISlider rounding = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.Rounding,
            0f, 30f, conf.Rounding, roundingFilter, null, null,
            "Corner Rounding", "progressbar_rounding"
        );
        rounding.Format = "0 px";
        rounding.OnChanged = v => { conf.Rounding = v; ProgressBarOverlay.Apply(); };
        rounding.OnComplete = v => { conf.Rounding = v; ProgressBarOverlay.Apply(); Save(); };

        static float outlineFilter(float v) => Mathf.Clamp(Mathf.Round(v * 4f) * 0.25f, 0f, 8f);
        UISlider outlineThick = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.OutlineThickness,
            0f, 8f, conf.OutlineThickness, outlineFilter, null, null,
            "Outline Thickness", "progressbar_outlinethickness"
        );
        outlineThick.Format = "0.## px";
        outlineThick.OnChanged = v => { conf.OutlineThickness = v; ProgressBarOverlay.Apply(); };
        outlineThick.OnComplete = v => { conf.OutlineThickness = v; ProgressBarOverlay.Apply(); Save(); };

        // === Color ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_COLOR", "Color");

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetFillColor(),
            conf.GetFillColor(),
            c => { conf.SetFillColor(c); ProgressBarOverlay.Apply(); },
            c => { conf.SetFillColor(c); ProgressBarOverlay.Apply(); Save(); },
            "Fill Color",
            "progressbar_fillcolor"
        );

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetBackColor(),
            conf.GetBackColor(),
            c => { conf.SetBackColor(c); ProgressBarOverlay.Apply(); },
            c => { conf.SetBackColor(c); ProgressBarOverlay.Apply(); Save(); },
            "Background Color",
            "progressbar_backcolor"
        );

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetOutlineColor(),
            conf.GetOutlineColor(),
            c => { conf.SetOutlineColor(c); ProgressBarOverlay.Apply(); },
            c => { conf.SetOutlineColor(c); ProgressBarOverlay.Apply(); Save(); },
            "Outline Color",
            "progressbar_outlinecolor"
        );
    }
}
