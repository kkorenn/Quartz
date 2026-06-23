using Quartz.Core;
using Quartz.Features.SongTitle;
using Quartz.Resource;
using Quartz.UI.Generator;
using Quartz.UI.Objects.Impl;
using UnityEngine;

namespace Quartz.UI.Factory.Page;

// Song Title settings section for the Overlay tab. Replaces the game's own
// in-game title/artist HUD with a customizable {artist}/{title} template.
internal static class PageSongTitle {
    public static void AppendTo(Transform content) {
        SongTitleOverlay.EnsureConf();
        SongTitleSettings conf = SongTitleOverlay.Conf;
        SongTitleSettings def = new();

        void Save() => SongTitleOverlay.Save();
        void Apply() => SongTitleOverlay.Apply();
        void ApplyShadow() => SongTitleOverlay.ApplyShadow();

        var sec = GenerateUI.Collapsible(
            content, "Song Title", startExpanded: false,
            v => { conf.Enabled = v; Apply(); Save(); },
            conf.Enabled
        );

        UIInput fmt = GenerateUI.Input(
            GenerateUI.Row(sec.Body),
            def.Format,
            conf.Format,
            v => { conf.Format = v; Save(); },
            "Format — use {artist} and {title}",
            MainCore.Spr.Get(UISprite.Text128),
            "songtitle_format"
        );
        fmt.InputField.characterLimit = 80;

        AddSlider(sec.Body, "Font Size", "songtitle_fontsize",
            def.FontSize, 12f, 120f, conf.FontSize, "0 px", 1f,
            v => conf.FontSize = v, Apply, Save);

        AddSlider(sec.Body, "Master Size", "songtitle_master",
            def.MasterSize, 0.25f, 3f, conf.MasterSize, "0.00 x", 0.01f,
            v => conf.MasterSize = v, Apply, Save);

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetColor(),
            conf.GetColor(),
            c => { conf.SetColor(c); Apply(); },
            c => { conf.SetColor(c); Apply(); Save(); },
            "Text Color",
            "songtitle_color"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.ShadowEnabled,
            conf.ShadowEnabled,
            v => { conf.ShadowEnabled = v; ApplyShadow(); Save(); },
            "Shadow",
            "songtitle_shadow"
        );

        AddSlider(sec.Body, "Shadow X", "songtitle_shadow_x",
            def.ShadowX, -10f, 10f, conf.ShadowX, "0.0 px", 0.1f,
            v => conf.ShadowX = v, ApplyShadow, Save);

        AddSlider(sec.Body, "Shadow Y", "songtitle_shadow_y",
            def.ShadowY, -10f, 10f, conf.ShadowY, "0.0 px", 0.1f,
            v => conf.ShadowY = v, ApplyShadow, Save);

        AddSlider(sec.Body, "Shadow Softness", "songtitle_shadow_soft",
            def.ShadowSoftness, 0f, 20f, conf.ShadowSoftness, "0.0 px", 0.1f,
            v => conf.ShadowSoftness = v, ApplyShadow, Save);

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetShadowColor(),
            conf.GetShadowColor(),
            c => { conf.SetShadowColor(c); ApplyShadow(); },
            c => { conf.SetShadowColor(c); ApplyShadow(); Save(); },
            "Shadow Color",
            "songtitle_shadow_color"
        );
    }

    // Shared slider helper (same shape as PageCombo's): snaps to step, formats
    // the readout, routes live + complete callbacks. Null `live` = save only.
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
