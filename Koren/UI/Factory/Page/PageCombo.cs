using Koren.Core;
using Koren.Features.Combo;
using Koren.Resource;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using UnityEngine;

namespace Koren.UI.Factory.Page;

// Combo settings section for the Overlay tab. The "Combo" collapsible's body
// holds three nested sub-sections — Count, Label, Animation — so the per-
// element knobs don't drown the top-level toggles.
internal static class PageCombo {
    public static void AppendTo(Transform content) {
        ComboOverlay.EnsureConf();
        ComboSettings conf = ComboOverlay.Conf;
        ComboSettings def = new();

        void Save() => ComboOverlay.Save();

        var sec = GenerateUI.Collapsible(content, "Combo", startExpanded: false);

        // === Top-level (Combo-wide) ===
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

        AddSlider(sec.Body, "Master Size", "combo_master_size",
            def.MasterSize, 0.25f, 3f, conf.MasterSize, "0.00 x", 0.01f,
            v => conf.MasterSize = v, ComboOverlay.Apply, Save);

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

        // === Count (value text + gradient) ===
        {
            var count = GenerateUI.Collapsible(sec.Body, "Count", startExpanded: false);

            static float fontFilter(float v) => Mathf.Clamp(Mathf.Round(v), 24f, 120f);
            UISlider font = GenerateUI.Slider(
                GenerateUI.Row(count.Body),
                def.FontSize,
                24f, 120f, conf.FontSize, fontFilter, null, null,
                "Font Size", "combo_fontsize"
            );
            font.Format = "0 px";
            font.OnChanged = v => { conf.FontSize = v; ComboOverlay.Apply(); };
            font.OnComplete = v => { conf.FontSize = v; ComboOverlay.Apply(); Save(); };

            AddSlider(count.Body, "Thickness", "combo_count_thickness",
                def.CountThickness, -0.5f, 0.5f, conf.CountThickness, "0.00", 0.01f,
                v => conf.CountThickness = v, ComboOverlay.Apply, Save);

            AddSlider(count.Body, "Shadow X", "combo_count_shadow_x",
                def.CountShadowX, -10f, 10f, conf.CountShadowX, "0.0 px", 0.1f,
                v => conf.CountShadowX = v, ComboOverlay.Apply, Save);

            AddSlider(count.Body, "Shadow Y", "combo_count_shadow_y",
                def.CountShadowY, -10f, 10f, conf.CountShadowY, "0.0 px", 0.1f,
                v => conf.CountShadowY = v, ComboOverlay.Apply, Save);

            GenerateUI.ColorPicker(
                GenerateUI.Row(count.Body),
                def.GetCountShadowColor(), conf.GetCountShadowColor(),
                c => { conf.SetCountShadowColor(c); ComboOverlay.Apply(); },
                c => { conf.SetCountShadowColor(c); ComboOverlay.Apply(); Save(); },
                "Shadow Color", "combo_count_shadow_color"
            );

            static float colorMaxFilter(float v) => Mathf.Clamp(Mathf.Round(v), 1f, 5000f);
            UISlider colorMax = GenerateUI.Slider(
                GenerateUI.Row(count.Body),
                def.ColorMax,
                1f, 5000f, conf.ColorMax, colorMaxFilter, null, null,
                "Color Max Combo", "combo_colormax"
            );
            colorMax.Format = "0";
            colorMax.OnChanged = v => { conf.ColorMax = Mathf.RoundToInt(v); };
            colorMax.OnComplete = v => { conf.ColorMax = Mathf.RoundToInt(v); Save(); };

            GenerateUI.ColorPicker(
                GenerateUI.Row(count.Body),
                def.GetColorLow(),
                conf.GetColorLow(),
                c => { conf.SetColorLow(c); },
                c => { conf.SetColorLow(c); Save(); },
                "Low Combo Color",
                "combo_colorlow"
            );

            GenerateUI.ColorPicker(
                GenerateUI.Row(count.Body),
                def.GetColorHigh(),
                conf.GetColorHigh(),
                c => { conf.SetColorHigh(c); },
                c => { conf.SetColorHigh(c); Save(); },
                "High Combo Color",
                "combo_colorhigh"
            );

            GenerateUI.Toggle(
                GenerateUI.Row(count.Body),
                def.SolidColor, conf.SolidColor,
                v => { conf.SolidColor = v; Save(); },
                "Solid Color", "combo_solidcolor"
            );

            GenerateUI.Toggle(
                GenerateUI.Row(count.Body),
                def.PerfectColorEnabled, conf.PerfectColorEnabled,
                v => { conf.PerfectColorEnabled = v; Save(); },
                "Perfect Color (at Max)", "combo_perfectcolor_enabled"
            );

            GenerateUI.ColorPicker(
                GenerateUI.Row(count.Body),
                def.GetPerfectColor(), conf.GetPerfectColor(),
                c => { conf.SetPerfectColor(c); },
                c => { conf.SetPerfectColor(c); Save(); },
                "Perfect Color", "combo_perfectcolor"
            );
        }

        // === Label (caption text) ===
        {
            var lbl = GenerateUI.Collapsible(sec.Body, "Label", startExpanded: false);

            GenerateUI.Toggle(
                GenerateUI.Row(lbl.Body),
                def.ShowCaption,
                conf.ShowCaption,
                v => { conf.ShowCaption = v; ComboOverlay.Apply(); Save(); },
                "Show Caption",
                "combo_caption"
            );

            UIInput caption = GenerateUI.Input(
                GenerateUI.Row(lbl.Body),
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
                GenerateUI.Row(lbl.Body),
                def.CaptionOffsetY,
                -200f, 200f, conf.CaptionOffsetY, capOffsetFilter, null, null,
                "Caption Offset", "combo_captionoffset"
            );
            capOffset.Format = "0 px";
            capOffset.OnChanged = v => { conf.CaptionOffsetY = v; ComboOverlay.Apply(); };
            capOffset.OnComplete = v => { conf.CaptionOffsetY = v; ComboOverlay.Apply(); Save(); };

            AddSlider(lbl.Body, "Size", "combo_caption_size",
                def.LabelSize, 0.1f, 1.5f, conf.LabelSize, "0.00 x", 0.01f,
                v => conf.LabelSize = v, null, Save);

            AddSlider(lbl.Body, "Shadow X", "combo_caption_shadow_x",
                def.LabelShadowX, -10f, 10f, conf.LabelShadowX, "0.0 px", 0.1f,
                v => conf.LabelShadowX = v, ComboOverlay.Apply, Save);

            AddSlider(lbl.Body, "Shadow Y", "combo_caption_shadow_y",
                def.LabelShadowY, -10f, 10f, conf.LabelShadowY, "0.0 px", 0.1f,
                v => conf.LabelShadowY = v, ComboOverlay.Apply, Save);

            GenerateUI.ColorPicker(
                GenerateUI.Row(lbl.Body),
                def.GetLabelShadowColor(), conf.GetLabelShadowColor(),
                c => { conf.SetLabelShadowColor(c); ComboOverlay.Apply(); },
                c => { conf.SetLabelShadowColor(c); ComboOverlay.Apply(); Save(); },
                "Shadow Color", "combo_caption_shadow_color"
            );
        }

        // === Animation (pulse) ===
        {
            var anim = GenerateUI.Collapsible(sec.Body, "Animation", startExpanded: false);

            GenerateUI.Toggle(
                GenerateUI.Row(anim.Body),
                def.NoPopAnim,
                conf.NoPopAnim,
                v => { conf.NoPopAnim = v; Save(); },
                "No Pop Animation",
                "combo_nopop"
            );

            GenerateUI.Toggle(
                GenerateUI.Row(anim.Body),
                def.FastAnim,
                conf.FastAnim,
                v => { conf.FastAnim = v; Save(); },
                "Fast Animation",
                "combo_fastanim"
            );

            AddSlider(anim.Body, "Pulse Peak Scale", "combo_pulse_peak",
                def.PulsePeakScale, 1f, 2f, conf.PulsePeakScale, "0.00 x", 0.01f,
                v => conf.PulsePeakScale = v, null, Save);

            AddSlider(anim.Body, "Caption Pulse Offset Y", "combo_pulse_caption_y",
                def.LabelPulseOffsetY, 0f, 60f, conf.LabelPulseOffsetY, "0 px", 1f,
                v => conf.LabelPulseOffsetY = v, null, Save);
        }

        GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => ComboOverlay.ResetPosition(),
            "Reset Position",
            "combo_resetposition"
        );
    }

    // Shared slider helper: stamps a slider, snaps values to `step`, and
    // routes both live and complete callbacks.
    private static void AddSlider(
        Transform body, string label, string id,
        float defVal, float min, float max, float val,
        string format, float step,
        System.Action<float> setter,
        System.Action live, System.Action save
    ) {
        float Snap(float v) {
            float snapped = Mathf.Round(v / step) * step;
            return Mathf.Clamp(snapped, min, max);
        }

        UISlider s = GenerateUI.Slider(
            GenerateUI.Row(body),
            defVal, min, max, val,
            Snap, null, null,
            label, id
        );
        s.Format = format;
        s.OnChanged = v => { setter(v); live?.Invoke(); };
        s.OnComplete = v => { setter(v); live?.Invoke(); save?.Invoke(); };
    }
}
