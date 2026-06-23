using Quartz.Core;
using Quartz.Features.Panels;
using Quartz.Features.ProgressBar;
using Quartz.UI.Generator;
using Quartz.UI.Objects.Impl;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Quartz.UI.Factory.Page;

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

        // Fill: a flat colour, or a progress-driven gradient (white at 0% ...
        // red at 100%, by default). The gradient reuses the Panels StatColor.
        StatColor grad = conf.FillGradient;
        Action rebuildFill = null;

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            false,
            grad.Enabled,
            v => { grad.Enabled = v; ProgressBarOverlay.Apply(); Save(); rebuildFill?.Invoke(); },
            "Fill Color Gradient",
            "progressbar_fillgradient"
        ).Rect.AddToolTip(
            "DESC_PROGRESSBAR_FILLGRADIENT",
            "Shift the fill colour as the run progresses (0% to 100%) instead of using one flat colour."
        );

        RectTransform fillBody = MakeBody(sec.Body, "FillColorBody");

        rebuildFill = () => {
            for(int i = fillBody.childCount - 1; i >= 0; i--) {
                Object.Destroy(fillBody.GetChild(i).gameObject);
            }

            if(!grad.Enabled) {
                GenerateUI.ColorPicker(
                    GenerateUI.Row(fillBody),
                    def.GetFillColor(),
                    conf.GetFillColor(),
                    c => { conf.SetFillColor(c); ProgressBarOverlay.Apply(); },
                    c => { conf.SetFillColor(c); ProgressBarOverlay.Apply(); Save(); },
                    "Fill Color",
                    "progressbar_fillcolor"
                );
                return;
            }

            for(int i = 0; i < grad.Points.Count; i++) {
                ColorPoint point = grad.Points[i];
                int index = i + 1;

                GenerateUI.ColorPicker(
                    GenerateUI.Row(fillBody),
                    point.GetColor(),
                    point.GetColor(),
                    c => { point.SetColor(c); ProgressBarOverlay.Apply(); },
                    c => { point.SetColor(c); ProgressBarOverlay.Apply(); Save(); },
                    string.Format(MainCore.Tr.Get("PROGRESSBAR_STOP_COLOR", "Stop {0} Color"), index),
                    "progressbar_stopcolor_" + i
                );

                UISlider pos = GenerateUI.Slider(
                    GenerateUI.Row(fillBody),
                    point.Pos * 100f, 0f, 100f, point.Pos * 100f,
                    v => Mathf.Clamp(Mathf.Round(v), 0f, 100f), null, null,
                    string.Format(MainCore.Tr.Get("PROGRESSBAR_STOP_POS", "Stop {0} Position"), index),
                    "progressbar_stoppos_" + i
                );
                pos.Format = "0 %";
                pos.OnChanged = v => { point.Pos = v * 0.01f; ProgressBarOverlay.Apply(); };
                pos.OnComplete = v => {
                    point.Pos = v * 0.01f;
                    grad.SortPoints();
                    ProgressBarOverlay.Apply();
                    Save();
                    rebuildFill();
                };

                if(grad.Points.Count > 1) {
                    GenerateUI.Button(
                        GenerateUI.Row(fillBody),
                        () => {
                            grad.Points.Remove(point);
                            grad.SortPoints();
                            ProgressBarOverlay.Apply();
                            Save();
                            rebuildFill();
                        },
                        string.Format(MainCore.Tr.Get("PROGRESSBAR_STOP_REMOVE", "Remove Stop {0}"), index),
                        "progressbar_stopremove_" + i
                    ).SetSecondary();
                }
            }

            if(grad.Points.Count < 8) {
                GenerateUI.Button(
                    GenerateUI.Row(fillBody),
                    () => {
                        grad.Points.Add(new ColorPoint(0.5f, grad.Evaluate(0.5f)));
                        grad.SortPoints();
                        ProgressBarOverlay.Apply();
                        Save();
                        rebuildFill();
                    },
                    "Add Stop",
                    "progressbar_stopadd"
                ).SetSecondary();
            }
        };
        rebuildFill();

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

    // A self-sizing vertical container whose children can be rebuilt in place
    // (used for the gradient-stops editor, which adds/removes rows).
    private static RectTransform MakeBody(Transform parent, string name) {
        GameObject obj = new(name);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = obj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = obj.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return rect;
    }
}
