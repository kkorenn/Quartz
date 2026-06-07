using Koren.Core;
using Koren.Features.Status;
using Koren.Resource;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace Koren.UI.Factory.Page;

// Settings page for the Status HUD feature. Stats are grouped into
// collapsible category sections. Each stat row has a "Show X" toggle plus
// a Left | Right side selector.
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

        // Per-stat row: existing "Show X" toggle plus a Left | Right radio
        // for which panel renders it. Both fed straight into Conf and saved.
        void StatRow(
            Transform body, string id, string label,
            bool defShow, bool show, System.Action<bool> setShow,
            bool defLeft, bool isLeft, System.Action<bool> setLeft
        ) {
            GenerateUI.Toggle(
                GenerateUI.Row(body),
                defShow, show,
                v => { setShow(v); Save(); },
                "Show " + label,
                "status_" + id
            );

            GenerateUI.SideRadio(
                GenerateUI.Row(body),
                "Side",
                isLeft,
                v => { setLeft(v); Save(); }
            );
        }

        GenerateUI.AddTextH1(GenerateUI.Row(content.transform)).text = "Status HUD";

        GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.Enabled,
            conf.Enabled,
            v => { conf.Enabled = v; StatusOverlay.Apply(); Save(); },
            "Enable Status HUD",
            "status_enabled"
        ).Rect.AddToolTip("DESC_STATUS_ENABLED", "Show a draggable HUD with live game status.");

        // === Accuracy ===
        {
            var sec = GenerateUI.Collapsible(content.transform, "Accuracy", startExpanded: false);

            StatRow(sec.Body, "progress", "Progress",
                def.ShowProgress, conf.ShowProgress, v => conf.ShowProgress = v,
                !def.ProgressOnRight, !conf.ProgressOnRight, v => conf.ProgressOnRight = !v);

            StatRow(sec.Body, "accuracy", "Accuracy",
                def.ShowAccuracy, conf.ShowAccuracy, v => conf.ShowAccuracy = v,
                !def.AccuracyOnRight, !conf.AccuracyOnRight, v => conf.AccuracyOnRight = !v);

            StatRow(sec.Body, "xaccuracy", "XAccuracy",
                def.ShowXAccuracy, conf.ShowXAccuracy, v => conf.ShowXAccuracy = v,
                !def.XAccuracyOnRight, !conf.XAccuracyOnRight, v => conf.XAccuracyOnRight = !v);

            StatRow(sec.Body, "maxaccuracy", "Max Accuracy",
                def.ShowMaxAccuracy, conf.ShowMaxAccuracy, v => conf.ShowMaxAccuracy = v,
                !def.MaxAccuracyOnRight, !conf.MaxAccuracyOnRight, v => conf.MaxAccuracyOnRight = !v);

            StatRow(sec.Body, "maxxaccuracy", "Max XAccuracy",
                def.ShowMaxXAccuracy, conf.ShowMaxXAccuracy, v => conf.ShowMaxXAccuracy = v,
                !def.MaxXAccuracyOnRight, !conf.MaxXAccuracyOnRight, v => conf.MaxXAccuracyOnRight = !v);
        }

        // === Time ===
        {
            var sec = GenerateUI.Collapsible(content.transform, "Time", startExpanded: false);

            StatRow(sec.Body, "musictime", "Music Time",
                def.ShowMusicTime, conf.ShowMusicTime, v => conf.ShowMusicTime = v,
                !def.MusicTimeOnRight, !conf.MusicTimeOnRight, v => conf.MusicTimeOnRight = !v);

            StatRow(sec.Body, "maptime", "Map Time",
                def.ShowMapTime, conf.ShowMapTime, v => conf.ShowMapTime = v,
                !def.MapTimeOnRight, !conf.MapTimeOnRight, v => conf.MapTimeOnRight = !v);
        }

        // === BPM ===
        {
            var sec = GenerateUI.Collapsible(content.transform, "BPM", startExpanded: false);

            StatRow(sec.Body, "tbpm", "True BPM",
                def.ShowTbpm, conf.ShowTbpm, v => conf.ShowTbpm = v,
                !def.TbpmOnRight, !conf.TbpmOnRight, v => conf.TbpmOnRight = !v);

            StatRow(sec.Body, "cbpm", "Current BPM",
                def.ShowCbpm, conf.ShowCbpm, v => conf.ShowCbpm = v,
                !def.CbpmOnRight, !conf.CbpmOnRight, v => conf.CbpmOnRight = !v);

            StatRow(sec.Body, "kps", "KPS",
                def.ShowKps, conf.ShowKps, v => conf.ShowKps = v,
                !def.KpsOnRight, !conf.KpsOnRight, v => conf.KpsOnRight = !v);
        }

        // === Map Stats ===
        {
            var sec = GenerateUI.Collapsible(content.transform, "Map Stats", startExpanded: false);

            StatRow(sec.Body, "checkpoint", "Checkpoints",
                def.ShowCheckpoint, conf.ShowCheckpoint, v => conf.ShowCheckpoint = v,
                !def.CheckpointOnRight, !conf.CheckpointOnRight, v => conf.CheckpointOnRight = !v);

            StatRow(sec.Body, "attempt", "Attempt",
                def.ShowAttempt, conf.ShowAttempt, v => conf.ShowAttempt = v,
                !def.AttemptOnRight, !conf.AttemptOnRight, v => conf.AttemptOnRight = !v);

            StatRow(sec.Body, "totalattempt", "Total Attempts",
                def.ShowTotalAttempt, conf.ShowTotalAttempt, v => conf.ShowTotalAttempt = v,
                !def.TotalAttemptOnRight, !conf.TotalAttemptOnRight, v => conf.TotalAttemptOnRight = !v);

            StatRow(sec.Body, "best", "Best",
                def.ShowBest, conf.ShowBest, v => conf.ShowBest = v,
                !def.BestOnRight, !conf.BestOnRight, v => conf.BestOnRight = !v);
        }

        // === Combo ===
        {
            var sec = GenerateUI.Collapsible(content.transform, "Combo", startExpanded: false);

            StatRow(sec.Body, "combo", "Combo",
                def.ShowCombo, conf.ShowCombo, v => conf.ShowCombo = v,
                !def.ComboOnRight, !conf.ComboOnRight, v => conf.ComboOnRight = !v);

            GenerateUI.Toggle(
                GenerateUI.Row(sec.Body),
                def.ComboCountAuto,
                conf.ComboCountAuto,
                v => { conf.ComboCountAuto = v; Save(); },
                "Combo Counts Auto Hits",
                "status_combo_auto"
            );
        }

        // === Other ===
        {
            var sec = GenerateUI.Collapsible(content.transform, "Other", startExpanded: false);

            StatRow(sec.Body, "hold", "Hold Behavior",
                def.ShowHold, conf.ShowHold, v => conf.ShowHold = v,
                !def.HoldOnRight, !conf.HoldOnRight, v => conf.HoldOnRight = !v);

            StatRow(sec.Body, "timingscale", "Timing Scale",
                def.ShowTimingScale, conf.ShowTimingScale, v => conf.ShowTimingScale = v,
                !def.TimingScaleOnRight, !conf.TimingScaleOnRight, v => conf.TimingScaleOnRight = !v);

            StatRow(sec.Body, "fps", "FPS",
                def.ShowFps, conf.ShowFps, v => conf.ShowFps = v,
                !def.FpsOnRight, !conf.FpsOnRight, v => conf.FpsOnRight = !v);
        }

        // === Appearance ===
        {
            var sec = GenerateUI.Collapsible(content.transform, "Appearance", startExpanded: false);

            UIInput prefix = GenerateUI.Input(
                GenerateUI.Row(sec.Body),
                def.Prefix,
                conf.Prefix,
                v => { conf.Prefix = v; Save(); },
                "Prefix",
                MainCore.Spr.Get(UISprite.Text128),
                "status_prefix"
            );
            prefix.InputField.characterLimit = 32;

            UIInput sep = GenerateUI.Input(
                GenerateUI.Row(sec.Body),
                def.LabelSeparator,
                conf.LabelSeparator,
                v => { conf.LabelSeparator = v; Save(); },
                "Label Separator",
                MainCore.Spr.Get(UISprite.Text128),
                "status_separator"
            );
            sep.InputField.characterLimit = 8;

            static float fontFilter(float v) => Mathf.Clamp(Mathf.Round(v), 12f, 48f);
            UISlider font = GenerateUI.Slider(
                GenerateUI.Row(sec.Body),
                def.FontSize,
                12f, 48f, conf.FontSize, fontFilter, null, null,
                "Font Size", "status_fontsize"
            );
            font.Format = "0 px";
            font.OnChanged = v => { conf.FontSize = v; StatusOverlay.Apply(); };
            font.OnComplete = v => { conf.FontSize = v; StatusOverlay.Apply(); Save(); };

            static float lineFilter(float v) => Mathf.Clamp(Mathf.Round(v * 2f) * 0.5f, -50f, 50f);
            UISlider line = GenerateUI.Slider(
                GenerateUI.Row(sec.Body),
                def.LineSpacing,
                -50f, 50f, conf.LineSpacing, lineFilter, null, null,
                "Line Spacing", "status_linespacing"
            );
            line.Format = "0.#";
            line.OnChanged = v => { conf.LineSpacing = v; StatusOverlay.Apply(); };
            line.OnComplete = v => { conf.LineSpacing = v; StatusOverlay.Apply(); Save(); };

            GenerateUI.ColorPicker(
                GenerateUI.Row(sec.Body),
                def.GetTextColor(),
                conf.GetTextColor(),
                c => { conf.SetTextColor(c); StatusOverlay.Apply(); },
                c => { conf.SetTextColor(c); StatusOverlay.Apply(); Save(); },
                "Text Color",
                "status_textcolor"
            );

            GenerateUI.Toggle(
                GenerateUI.Row(sec.Body),
                def.BackgroundEnabled,
                conf.BackgroundEnabled,
                v => { conf.BackgroundEnabled = v; StatusOverlay.Apply(); Save(); },
                "Background Panel",
                "status_background"
            );
        }

        // === Layout / Reset ===
        {
            var sec = GenerateUI.Collapsible(content.transform, "Layout", startExpanded: false);

            GenerateUI.Button(
                GenerateUI.Row(sec.Body),
                () => StatusOverlay.ResetLeftPosition(),
                "Reset Left Panel Position",
                "status_resetleft"
            );

            GenerateUI.Button(
                GenerateUI.Row(sec.Body),
                () => StatusOverlay.ResetRightPosition(),
                "Reset Right Panel Position",
                "status_resetright"
            );
        }
    }
}
