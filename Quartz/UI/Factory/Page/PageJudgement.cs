using Quartz.Features.Judgement;
using Quartz.UI.Generator;
using Quartz.UI.Objects.Impl;
using UnityEngine;

namespace Quartz.UI.Factory.Page;

// Judgement counts section for the Overlay tab — the original
// KorenResourcePack judgement overlay (top-center row of colored per-
// judgement hit counts). Slider ranges match v1.
internal static class PageJudgement {
    public static void AppendTo(Transform content) {
        JudgementOverlay.EnsureConf();
        JudgementSettings conf = JudgementOverlay.Conf;
        JudgementSettings def = new();

        void Save() => JudgementOverlay.Save();
        void Apply() => JudgementOverlay.Apply();

        var sec = GenerateUI.Collapsible(
            content, "Judgement", startExpanded: false,
            v => { conf.Enabled = v; Apply(); Save(); },
            conf.Enabled
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.ShowXPerfect,
            conf.ShowXPerfect,
            v => { conf.ShowXPerfect = v; Apply(); Save(); },
            "Show XPerfect",
            "judgement_xperfect"
        ).Rect.AddToolTip(
            "DESC_JUDGEMENT_XPERFECT",
            "Split the Perfect count into +Perfect / X / -Perfect when the XPerfect mod is active."
        );

        // === Layout ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_LAYOUT", "Layout");

        AddSlider(sec.Body, "Size", "judgement_size",
            def.Size, 0.3f, 3f, conf.Size, "0.00 x", 0.01f,
            v => conf.Size = v, Apply, Save);

        AddSlider(sec.Body, "Spacing", "judgement_spacing",
            def.Spacing, -20f, 80f, conf.Spacing, "0 px", 1f,
            v => conf.Spacing = v, Apply, Save);

        // === Shadow ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_SHADOW", "Shadow");

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.TextShadowEnabled,
            conf.TextShadowEnabled,
            v => { conf.TextShadowEnabled = v; Apply(); Save(); },
            "Text Shadow",
            "judgement_shadow_enabled"
        );

        AddSlider(sec.Body, "Shadow X", "judgement_shadow_x",
            def.TextShadowX, -20f, 20f, conf.TextShadowX, "0.0 px", 0.1f,
            v => conf.TextShadowX = v, Apply, Save);

        AddSlider(sec.Body, "Shadow Y", "judgement_shadow_y",
            def.TextShadowY, -20f, 20f, conf.TextShadowY, "0.0 px", 0.1f,
            v => conf.TextShadowY = v, Apply, Save);

        AddSlider(sec.Body, "Shadow Softness", "judgement_shadow_softness",
            def.TextShadowSoftness, 0f, 20f, conf.TextShadowSoftness, "0.0 px", 0.1f,
            v => conf.TextShadowSoftness = v, Apply, Save);

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetTextShadowColor(),
            conf.GetTextShadowColor(),
            c => { conf.SetTextShadowColor(c); Apply(); },
            c => { conf.SetTextShadowColor(c); Apply(); Save(); },
            "Shadow Color",
            "judgement_shadow_color"
        );
    }

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
