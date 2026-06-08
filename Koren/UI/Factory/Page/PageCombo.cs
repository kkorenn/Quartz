using Koren.Core;
using Koren.Features.Combo;
using Koren.Resource;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using UnityEngine;

namespace Koren.UI.Factory.Page;

// Combo settings section for the Overlay tab.
internal static class PageCombo {
    public static void AppendTo(Transform content) {
        ComboOverlay.EnsureConf();
        ComboSettings conf = ComboOverlay.Conf;
        ComboSettings def = new();

        void Save() => ComboOverlay.Save();

        var sec = GenerateUI.Collapsible(content, "Combo", startExpanded: false);

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.Enabled,
            conf.Enabled,
            v => { conf.Enabled = v; ComboOverlay.Apply(); Save(); },
            "Enable Combo",
            "combo_enabled"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.CountAuto,
            conf.CountAuto,
            v => { conf.CountAuto = v; Save(); },
            "Combo Counts Auto Hits",
            "combo_auto"
        );

        static float fontFilter(float v) => Mathf.Clamp(Mathf.Round(v), 24f, 120f);
        UISlider font = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.FontSize,
            24f, 120f, conf.FontSize, fontFilter, null, null,
            "Font Size", "combo_fontsize"
        );
        font.Format = "0 px";
        font.OnChanged = v => { conf.FontSize = v; ComboOverlay.Apply(); };
        font.OnComplete = v => { conf.FontSize = v; ComboOverlay.Apply(); Save(); };

        static float offsetFilter(float v) => Mathf.Clamp(Mathf.Round(v), -500f, 500f);
        UISlider offsetY = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.OffsetY,
            -500f, 500f, conf.OffsetY, offsetFilter, null, null,
            "Vertical Offset", "combo_offsety"
        );
        offsetY.Format = "0 px";
        offsetY.OnChanged = v => { conf.OffsetY = v; ComboOverlay.Apply(); };
        offsetY.OnComplete = v => { conf.OffsetY = v; ComboOverlay.Apply(); Save(); };

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.ShowCaption,
            conf.ShowCaption,
            v => { conf.ShowCaption = v; ComboOverlay.Apply(); Save(); },
            "Caption Text",
            "combo_caption"
        );

        UIInput caption = GenerateUI.Input(
            GenerateUI.Row(sec.Body),
            def.CaptionText,
            conf.CaptionText,
            v => { conf.CaptionText = v; ComboOverlay.Apply(); Save(); },
            "Caption Label",
            MainCore.Spr.Get(UISprite.Text128),
            "combo_captiontext"
        );
        caption.InputField.characterLimit = 24;

        static float capOffsetFilter(float v) => Mathf.Clamp(Mathf.Round(v), -200f, 200f);
        UISlider capOffset = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.CaptionOffsetY,
            -200f, 200f, conf.CaptionOffsetY, capOffsetFilter, null, null,
            "Caption Offset", "combo_captionoffset"
        );
        capOffset.Format = "0 px";
        capOffset.OnChanged = v => { conf.CaptionOffsetY = v; ComboOverlay.Apply(); };
        capOffset.OnComplete = v => { conf.CaptionOffsetY = v; ComboOverlay.Apply(); Save(); };

        static float colorMaxFilter(float v) => Mathf.Clamp(Mathf.Round(v), 1f, 5000f);
        UISlider colorMax = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.ColorMax,
            1f, 5000f, conf.ColorMax, colorMaxFilter, null, null,
            "Color Max Combo", "combo_colormax"
        );
        colorMax.Format = "0";
        colorMax.OnChanged = v => { conf.ColorMax = Mathf.RoundToInt(v); };
        colorMax.OnComplete = v => { conf.ColorMax = Mathf.RoundToInt(v); Save(); };

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetColorLow(),
            conf.GetColorLow(),
            c => { conf.SetColorLow(c); },
            c => { conf.SetColorLow(c); Save(); },
            "Low Combo Color",
            "combo_colorlow"
        );

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetColorHigh(),
            conf.GetColorHigh(),
            c => { conf.SetColorHigh(c); },
            c => { conf.SetColorHigh(c); Save(); },
            "High Combo Color",
            "combo_colorhigh"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.NoPopAnim,
            conf.NoPopAnim,
            v => { conf.NoPopAnim = v; Save(); },
            "No Pop Animation",
            "combo_nopop"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.FastAnim,
            conf.FastAnim,
            v => { conf.FastAnim = v; Save(); },
            "Fast Animation",
            "combo_fastanim"
        );
    }
}
