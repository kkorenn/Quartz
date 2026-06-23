using Quartz.Core;
using Quartz.Features.Combo;
using Quartz.Features.Interop;
using Quartz.Resource;
using Quartz.UI.Generator;
using Quartz.UI.Objects.Impl;
using UnityEngine;

namespace Quartz.UI.Factory.Page;

// Combo settings section for the Overlay tab. The layout (center value with a
// caption beneath, below the progress bar) is the original combo; the Label /
// Count / Animation / Color groups expose the extra knobs ported from the
// combo-progressbar-playcount branch. Defaults reproduce the original look.
internal static class PageCombo {
    public static void AppendTo(Transform content) {
        ComboOverlay.EnsureConf();
        ComboSettings conf = ComboOverlay.Conf;
        ComboSettings def = new();

        void Save() => ComboOverlay.Save();
        void Apply() => ComboOverlay.Apply();
        void ApplyCaptionShadow() => ComboOverlay.ApplyCaptionShadow();
        void ApplyCountShadow() => ComboOverlay.ApplyCountShadow();

        var sec = GenerateUI.Collapsible(
            content, "Combo", startExpanded: false,
            v => { conf.Enabled = v; Apply(); Save(); },
            conf.Enabled
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.CountAuto,
            conf.CountAuto,
            v => { conf.CountAuto = v; Save(); },
            "Combo Counts Auto Hits",
            "combo_auto"
        );

        // Only meaningful when the XPerfect mod is installed: count only its
        // dead-center X perfects toward the combo (caption becomes "XCombo").
        if(XPerfectBridge.Installed) {
            GenerateUI.Toggle(
                GenerateUI.Row(sec.Body),
                def.XPerfectComboEnabled,
                conf.XPerfectComboEnabled,
                v => { conf.XPerfectComboEnabled = v; Apply(); Save(); },
                "XPerfect Combo (X Only)",
                "combo_xperfect"
            );
        }

        AddSlider(sec.Body, "Font Size", "combo_fontsize",
            def.FontSize, 24f, 120f, conf.FontSize, "0 px", 1f,
            v => conf.FontSize = v, Apply, Save);

        AddSlider(sec.Body, "Master Size", "combo_master_size",
            def.MasterSize, 0.25f, 3f, conf.MasterSize, "0.00 x", 0.01f,
            v => conf.MasterSize = v, Apply, Save);

        // === Label ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_LABEL", "Label");

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.ShowCaption,
            conf.ShowCaption,
            v => { conf.ShowCaption = v; Apply(); Save(); },
            "Show Caption",
            "combo_caption"
        );

        UIInput caption = GenerateUI.Input(
            GenerateUI.Row(sec.Body),
            def.CaptionText,
            conf.CaptionText,
            v => { conf.CaptionText = v; Apply(); Save(); },
            "Caption Label",
            MainCore.Spr.Get(UISprite.Text128),
            "combo_captiontext"
        );
        caption.InputField.characterLimit = 24;

        AddSlider(sec.Body, "Caption Size", "combo_caption_size",
            def.CaptionScale, 0.1f, 1.5f, conf.CaptionScale, "0.00 x", 0.01f,
            v => conf.CaptionScale = v, Apply, Save);

        AddSlider(sec.Body, "Caption Offset", "combo_captionoffset",
            def.CaptionOffsetY, -200f, 200f, conf.CaptionOffsetY, "0 px", 1f,
            v => conf.CaptionOffsetY = v, Apply, Save);

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.CaptionShadowEnabled,
            conf.CaptionShadowEnabled,
            v => { conf.CaptionShadowEnabled = v; ApplyCaptionShadow(); Save(); },
            "Caption Shadow",
            "combo_caption_shadow_enabled"
        );

        AddSlider(sec.Body, "Caption Shadow X", "combo_caption_shadow_x",
            def.CaptionShadowX, -10f, 10f, conf.CaptionShadowX, "0.0 px", 0.1f,
            v => conf.CaptionShadowX = v, ApplyCaptionShadow, Save);

        AddSlider(sec.Body, "Caption Shadow Y", "combo_caption_shadow_y",
            def.CaptionShadowY, -10f, 10f, conf.CaptionShadowY, "0.0 px", 0.1f,
            v => conf.CaptionShadowY = v, ApplyCaptionShadow, Save);

        AddSlider(sec.Body, "Caption Shadow Softness", "combo_caption_shadow_softness",
            def.CaptionShadowSoftness, 0f, 20f, conf.CaptionShadowSoftness, "0.0 px", 0.1f,
            v => conf.CaptionShadowSoftness = v, ApplyCaptionShadow, Save);

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetCaptionShadowColor(),
            conf.GetCaptionShadowColor(),
            c => { conf.SetCaptionShadowColor(c); ApplyCaptionShadow(); },
            c => { conf.SetCaptionShadowColor(c); ApplyCaptionShadow(); Save(); },
            "Caption Shadow Color",
            "combo_caption_shadow_color"
        );

        // === Count ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_COUNT", "Count");

        AddSlider(sec.Body, "Thickness", "combo_count_thickness",
            def.CountThickness, -0.5f, 0.5f, conf.CountThickness, "0.00", 0.01f,
            v => conf.CountThickness = v, Apply, Save);

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.CountShadowEnabled,
            conf.CountShadowEnabled,
            v => { conf.CountShadowEnabled = v; ApplyCountShadow(); Save(); },
            "Count Shadow",
            "combo_count_shadow_enabled"
        );

        AddSlider(sec.Body, "Count Shadow X", "combo_count_shadow_x",
            def.CountShadowX, -10f, 10f, conf.CountShadowX, "0.0 px", 0.1f,
            v => conf.CountShadowX = v, ApplyCountShadow, Save);

        AddSlider(sec.Body, "Count Shadow Y", "combo_count_shadow_y",
            def.CountShadowY, -10f, 10f, conf.CountShadowY, "0.0 px", 0.1f,
            v => conf.CountShadowY = v, ApplyCountShadow, Save);

        AddSlider(sec.Body, "Count Shadow Softness", "combo_count_shadow_softness",
            def.CountShadowSoftness, 0f, 20f, conf.CountShadowSoftness, "0.0 px", 0.1f,
            v => conf.CountShadowSoftness = v, ApplyCountShadow, Save);

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetCountShadowColor(),
            conf.GetCountShadowColor(),
            c => { conf.SetCountShadowColor(c); ApplyCountShadow(); },
            c => { conf.SetCountShadowColor(c); ApplyCountShadow(); Save(); },
            "Count Shadow Color",
            "combo_count_shadow_color"
        );

        // === Animation ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_ANIMATION", "Animation");

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.NoPopAnim,
            conf.NoPopAnim,
            v => { conf.NoPopAnim = v; Save(); },
            "No Pop Animation",
            "combo_nopop"
        );

        AddSlider(sec.Body, "Pulse Duration", "combo_pulse_duration",
            def.PulseDuration, 0f, 1f, conf.PulseDuration, "0.00 s", 0.01f,
            v => conf.PulseDuration = v, null, Save);

        AddSlider(sec.Body, "Count Pulse Scale", "combo_pulse_count_scale",
            def.CountPulseScale, 0f, 1f, conf.CountPulseScale, "0.00 x", 0.01f,
            v => conf.CountPulseScale = v, null, Save);

        AddSlider(sec.Body, "Label Pulse Offset Y", "combo_pulse_label_offset",
            def.LabelPulseOffsetY, 0f, 60f, conf.LabelPulseOffsetY, "0 px", 1f,
            v => conf.LabelPulseOffsetY = v, null, Save);

        // === Color ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_COLOR", "Color");

        AddSlider(sec.Body, "Color Max Combo", "combo_colormax",
            def.ColorMax, 1f, 5000f, conf.ColorMax, "0", 1f,
            v => conf.ColorMax = Mathf.RoundToInt(v), null, Save);

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.SolidColor,
            conf.SolidColor,
            v => { conf.SolidColor = v; Save(); },
            "Solid Color",
            "combo_solidcolor"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.PerfectColorEnabled,
            conf.PerfectColorEnabled,
            v => { conf.PerfectColorEnabled = v; Save(); },
            "Perfect Color (at Max)",
            "combo_perfectcolor_enabled"
        );

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

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetPerfectColor(),
            conf.GetPerfectColor(),
            c => { conf.SetPerfectColor(c); },
            c => { conf.SetPerfectColor(c); Save(); },
            "Perfect Color",
            "combo_perfectcolor"
        );
    }

    // Shared slider helper: stamps a slider row, formats its readout, snaps
    // values to `step`, and routes both the live and complete callbacks. A
    // null `live` means the value only needs saving (no immediate re-apply).
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
