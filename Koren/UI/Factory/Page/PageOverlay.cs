using Koren.Core;
using Koren.Features.Combo;
using Koren.Features.Panels;
using Koren.Features.ProgressBar;
using Koren.Resource;
using Koren.UI;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using GTweens.Builders;
using GTweens.Easings;
using GTweens.Extensions;
using GTweens.Tweens;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityEngine.EventSystems.PointerEventData;

using TMPro;

namespace Koren.UI.Factory.Page;

// Settings page for the overlay HUDs. Master "Enable Overlays" toggle at the
// top (no category), then the Progress Bar / Combo / Judgement sections, then
// the Panels category: user-created, named panels that any catalog stat can
// be placed on — replacing the old fixed Left/Right HUD.
internal static class PageOverlay {
    private static GameObject panelsList;

    public static void Create(RectTransform parent) {
        PanelsOverlay.EnsureConf();
        PanelsSettings conf = PanelsOverlay.Conf;
        PanelsSettings def = new();

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

        // === Top: master toggle + reorganize, no category ===

        GenerateUI.Button(
            GenerateUI.Row(content.transform),
            () => UICore.EnterReorganize(),
            "Reorganize",
            "overlay_reorganize"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.Enabled,
            conf.Enabled,
            v => {
                conf.Enabled = v;
                PanelsOverlay.Save();
            },
            "Enable Overlays",
            "overlay_enabled"
        ).Rect.AddToolTip(
            "DESC_OVERLAY_ENABLED",
            "Master switch for every overlay HUD — panels, progress bar, combo and judgement."
        );

        // === Separate feature sections ===

        PageProgressBar.AppendTo(content.transform);
        PageCombo.AppendTo(content.transform);
        PageJudgement.AppendTo(content.transform);
        PageKeyViewer.AppendTo(content.transform);
        PageSongTitle.AppendTo(content.transform);

        // === Panels ===

        var panelsSec = GenerateUI.Collapsible(content.transform, "Panels", startExpanded: true);

        GenerateUI.Button(
            GenerateUI.Row(panelsSec.Body),
            () => {
                PanelConfig p = new() {
                    Name = "Panel " + (PanelsOverlay.Conf.Panels.Count + 1),
                };
                // Stagger new panels so they don't stack exactly on top of
                // each other.
                p.PosX += 24f * PanelsOverlay.Conf.Panels.Count;
                p.PosY -= 24f * PanelsOverlay.Conf.Panels.Count;
                PanelsOverlay.Conf.Panels.Add(p);
                PanelsOverlay.Save();
                PanelsOverlay.Rebuild();
                RebuildPanelsList();
            },
            "Create Panel",
            "panels_create"
        ).Rect.AddToolTip(
            "DESC_PANELS_CREATE",
            "Adds a new empty panel. Name it, put stats on it, then drag it into place with Reorganize."
        );

        panelsList = new GameObject("PanelsList");
        panelsList.transform.SetParent(panelsSec.Body, false);
        panelsList.AddComponent<RectTransform>();

        VerticalLayoutGroup listLayout = panelsList.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 12f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        ContentSizeFitter listFitter = panelsList.AddComponent<ContentSizeFitter>();
        listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RebuildPanelsList();

        // === Layout / Reset (panel positions reset inside each panel) ===
        {
            var sec = GenerateUI.Collapsible(content.transform, "Layout", startExpanded: false);

            GenerateUI.Button(
                GenerateUI.Row(sec.Body),
                () => ProgressBarOverlay.ResetPosition(),
                "Reset Progress Bar Position",
                "overlay_resetprogressbar"
            );

            GenerateUI.Button(
                GenerateUI.Row(sec.Body),
                () => ComboOverlay.ResetPosition(),
                "Reset Combo Position",
                "overlay_resetcombo"
            );

            GenerateUI.Button(
                GenerateUI.Row(sec.Body),
                () => Koren.Features.Judgement.JudgementOverlay.ResetPosition(),
                "Reset Judgement Position",
                "overlay_resetjudgement"
            );

            GenerateUI.Button(
                GenerateUI.Row(sec.Body),
                () => Koren.Features.SongTitle.SongTitleOverlay.ResetPosition(),
                "Reset Song Title Position",
                "overlay_resetsongtitle"
            );

            GenerateUI.Button(
                GenerateUI.Row(sec.Body),
                () => Koren.Features.KeyViewer.KeyViewerOverlay.ResetPosition(),
                "Reset Key Viewer Position",
                "overlay_resetkeyviewer"
            );
        }
    }

    // Rebuilds the per-panel sections (create/delete change the set).
    private static void RebuildPanelsList() {
        if(panelsList == null) {
            return;
        }

        for(int i = panelsList.transform.childCount - 1; i >= 0; i--) {
            Object.Destroy(panelsList.transform.GetChild(i).gameObject);
        }

        List<PanelConfig> panels = PanelsOverlay.Conf.Panels;

        if(panels.Count == 0) {
            var note = GenerateUI.AddText(GenerateUI.Row(panelsList.transform));
            GenerateUI.Localize(note, "PANEL_NO_PANELS", "No panels. Create one above.");
            note.fontSize = 19f;
            note.color = new Color(1f, 1f, 1f, 0.45f);
            return;
        }

        for(int i = 0; i < panels.Count; i++) {
            CreatePanelSection(panelsList.transform, panels[i], i);
        }
    }

    private static void CreatePanelSection(Transform parent, PanelConfig panel, int index) {
        PanelConfig def = new();
        string idp = "panel" + index;

        var sec = GenerateUI.Collapsible(parent, panel.Name, startExpanded: false);
        TMP_Text header = sec.Section.Find("Header/Bar/Label")?.GetComponent<TMP_Text>();

        void Save() => PanelsOverlay.Save();

        // === Panel settings ===

        UIInput name = GenerateUI.Input(
            GenerateUI.Row(sec.Body),
            def.Name,
            panel.Name,
            v => {
                panel.Name = string.IsNullOrWhiteSpace(v) ? "Panel" : v;
                if(header != null) {
                    header.text = panel.Name;
                }
                Save();
            },
            "Panel Name",
            MainCore.Spr.Get(UISprite.Text128),
            idp + "_name"
        );
        name.InputField.characterLimit = 24;
        name.Rect.AddToolTip("DESC_PANEL_NAME", "Shown on the panel while reorganizing, and as this section's title.");

        // === Stats on this panel: an ordered, editable list ===
        //
        // "+ Add Stat" opens a picker; each entry row has a 6-dot drag handle
        // (reorder = display order on the panel), an enable dot, a Swap
        // button (replace with another stat in place) and a delete X.

        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_STATS", "Stats");

        UIButton addBtn = null;
        GameObject picker = null;
        GameObject rows = null;

        // Picker slide animation plumbing (assigned after the container is
        // created below; the local functions only run after that).
        RectTransform pickerRect = null;
        VerticalLayoutGroup pickerLayout = null;
        ContentSizeFitter pickerFitter = null;
        LayoutElement pickerLE = null;
        CanvasGroup pickerCg = null;
        GTween pickerSeq = null;

        // null = picker adds to the end; set = picker replaces this entry.
        StatEntry replaceTarget = null;
        bool pickerOpen = false;

        // Slides the picker open/closed like a dropdown: lay the rows out
        // once, freeze the layout group, then drive a LayoutElement height +
        // fade (same idiom as the collapsibles).
        void AnimatePicker(bool open, Action onClosed = null) {
            pickerSeq?.Kill();

            pickerLayout.enabled = true;
            pickerFitter.enabled = true;
            pickerLE.preferredHeight = -1f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(sec.Section);
            float content = pickerRect.rect.height;

            pickerLayout.enabled = false;
            pickerFitter.enabled = false;

            pickerLE.preferredHeight = open ? 0f : content;
            pickerCg.alpha = open ? 0f : 1f;

            pickerSeq = GTweenSequenceBuilder.New()
                .Join(GTweenExtensions.Tween(
                    () => pickerLE.preferredHeight,
                    x => {
                        pickerLE.preferredHeight = Mathf.Max(0f, x);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(sec.Section);
                    },
                    open ? content : 0f,
                    0.16f
                ).SetEasing(open ? Easing.OutBack : Easing.OutSine))
                .Join(GTweenExtensions.Tween(
                    () => pickerCg.alpha,
                    x => pickerCg.alpha = x,
                    open ? 1f : 0f,
                    0.16f
                ).SetEasing(Easing.OutSine))
                .AppendCallback(() => {
                    if(open) {
                        // Hand sizing back so the rows stay laid out.
                        pickerLayout.enabled = true;
                        pickerFitter.enabled = true;
                        pickerLE.preferredHeight = -1f;
                        LayoutRebuilder.ForceRebuildLayoutImmediate(sec.Section);
                    } else {
                        ClearChildren(picker.transform);
                        pickerLE.preferredHeight = 0f;
                        onClosed?.Invoke();
                    }
                })
                .Build();
            MainCore.TC.Play(pickerSeq);
        }

        void CommitOrder() {
            List<StatEntry> order = [];
            for(int i = 0; i < rows.transform.childCount; i++) {
                StatRowMarker marker = rows.transform.GetChild(i).GetComponent<StatRowMarker>();
                if(marker != null) {
                    order.Add(marker.Entry);
                }
            }
            panel.Stats.Clear();
            panel.Stats.AddRange(order);
            Save();
        }

        void ClosePicker(bool animate = true) {
            pickerOpen = false;
            replaceTarget = null;
            if(addBtn != null) {
                addBtn.Label.text = MainCore.Tr.Get("PANEL_ADDSTAT", "+ Add Stat");
            }

            if(animate) {
                AnimatePicker(false);
            } else {
                pickerSeq?.Kill();
                ClearChildren(picker.transform);
                pickerLE.preferredHeight = 0f;
            }
        }

        void OpenPickerAnimated() {
            pickerOpen = true;
            if(addBtn != null) {
                addBtn.Label.text = MainCore.Tr.Get("CLOSE", "Close");
            }
            BuildPicker();
            AnimatePicker(true);
        }

        // Per-stat color settings live in a collapsible body under each row,
        // slid open/closed by the row's Color button (same animation idiom as
        // the stat picker above). Content is built on open and torn down
        // after the close animation.
        HashSet<StatEntry> colorExpanded = [];
        Dictionary<StatEntry, StatColorBody> colorBodies = [];

        void AnimateColorBody(StatColorBody body, bool open) {
            body.Seq?.Kill();

            body.Layout.enabled = true;
            body.Fitter.enabled = true;
            body.LE.preferredHeight = -1f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(sec.Section);
            float content = body.Rect.rect.height;

            body.Layout.enabled = false;
            body.Fitter.enabled = false;

            body.LE.preferredHeight = open ? 0f : content;
            body.CG.alpha = open ? 0f : 1f;

            body.Seq = GTweenSequenceBuilder.New()
                .Join(GTweenExtensions.Tween(
                    () => body.LE.preferredHeight,
                    x => {
                        body.LE.preferredHeight = Mathf.Max(0f, x);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(sec.Section);
                    },
                    open ? content : 0f,
                    0.16f
                ).SetEasing(open ? Easing.OutBack : Easing.OutSine))
                .Join(GTweenExtensions.Tween(
                    () => body.CG.alpha,
                    x => body.CG.alpha = x,
                    open ? 1f : 0f,
                    0.16f
                ).SetEasing(Easing.OutSine))
                .AppendCallback(() => {
                    if(open) {
                        // Hand sizing back so the content stays laid out.
                        body.Layout.enabled = true;
                        body.Fitter.enabled = true;
                        body.LE.preferredHeight = -1f;
                        LayoutRebuilder.ForceRebuildLayoutImmediate(sec.Section);
                    } else {
                        ClearChildren(body.Rect);
                        body.LE.preferredHeight = 0f;
                    }
                })
                .Build();
            MainCore.TC.Play(body.Seq);
        }

        void RebuildColorBody(StatEntry entry) {
            if(!colorBodies.TryGetValue(entry, out StatColorBody body)) {
                return;
            }

            ClearChildren(body.Rect);
            BuildStatColorSettings(body.Rect, entry, Save, () => RebuildColorBody(entry), idp);

            body.Layout.enabled = true;
            body.Fitter.enabled = true;
            body.LE.preferredHeight = -1f;
            body.CG.alpha = 1f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(sec.Section);
        }

        void ToggleColorBody(StatEntry entry) {
            if(!colorBodies.TryGetValue(entry, out StatColorBody body)) {
                return;
            }

            if(colorExpanded.Remove(entry)) {
                AnimateColorBody(body, false);
                return;
            }

            colorExpanded.Add(entry);
            ClearChildren(body.Rect);
            BuildStatColorSettings(body.Rect, entry, Save, () => RebuildColorBody(entry), idp);
            AnimateColorBody(body, true);
        }

        void RebuildRows() {
            ClearChildren(rows.transform);
            colorBodies.Clear();

            if(panel.Stats.Count == 0) {
                var note = GenerateUI.AddText(GenerateUI.Row(rows.transform));
                GenerateUI.Localize(note, "PANEL_NO_STATS", "No stats on this panel.");
                note.fontSize = 19f;
                note.color = new Color(1f, 1f, 1f, 0.45f);
                return;
            }

            foreach(StatEntry entry in panel.Stats) {
                BuildStatRow(rows.transform, entry, () => {
                    CommitOrder();
                    // Reordering moves only the marker rows; rebuild so any
                    // expanded color settings follow their row.
                    if(colorExpanded.Count > 0) {
                        RebuildRows();
                    }
                }, () => {
                    panel.Stats.Remove(entry);
                    colorExpanded.Remove(entry);
                    Save();
                    RebuildRows();
                }, () => {
                    // Swap: open the picker targeting this entry.
                    replaceTarget = entry;
                    OpenPickerAnimated();
                }, () => ToggleColorBody(entry), Save, idp);

                StatColorBody body = CreateColorBody(rows.transform);
                colorBodies[entry] = body;

                // Entries already expanded (rebuild after add/delete/reorder)
                // come back open without re-animating.
                if(colorExpanded.Contains(entry)) {
                    BuildStatColorSettings(body.Rect, entry, Save, () => RebuildColorBody(entry), idp);
                    body.LE.preferredHeight = -1f;
                    body.CG.alpha = 1f;
                }
            }
        }

        void BuildPicker() {
            ClearChildren(picker.transform);

            bool any = false;
            string[] categories = ["Accuracy", "Time", "BPM", "Map Stats", "Other"];
            foreach(string category in categories) {
                bool headerAdded = false;

                foreach(PanelsOverlay.StatDef stat in PanelsOverlay.Catalog) {
                    if(stat.Category != category) {
                        continue;
                    }
                    // Skip stats already on the panel (including the one
                    // being replaced — swapping to itself is a no-op). The
                    // "text" stat is exempt: each carries its own custom string,
                    // so any number can sit on one panel.
                    if(stat.Id != "text" && panel.Stats.Exists(e => e.Id == stat.Id)) {
                        continue;
                    }

                    if(!headerAdded) {
                        headerAdded = true;
                        var header = GenerateUI.AddText(GenerateUI.Row(picker.transform, 32f));
                        GenerateUI.Localize(
                            header,
                            GenerateUI.LocaleKeyFromText("PANEL_CATEGORY", category),
                            category
                        );
                        header.fontSize = 17f;
                        header.color = new Color(1f, 1f, 1f, 0.45f);
                    }

                    any = true;
                    string statId = stat.Id;
                    GenerateUI.Button(
                        GenerateUI.Row(picker.transform),
                        () => {
                            if(replaceTarget != null) {
                                replaceTarget.Id = statId;
                            } else {
                                StatEntry added = new(statId);
                                // A custom-text line has no meaningful "Text"
                                // label prefix, so default it to value-only.
                                if(statId == "text") {
                                    added.ShowLabel = false;
                                }
                                panel.Stats.Add(added);
                            }
                            Save();
                            ClosePicker();
                            RebuildRows();
                        },
                        stat.Label,
                        idp + "_pick_" + statId
                    ).SetSecondary();
                }
            }

            if(!any) {
                var note = GenerateUI.AddText(GenerateUI.Row(picker.transform));
                GenerateUI.Localize(note, "PANEL_ALL_STATS_ADDED", "All stats are already on this panel.");
                note.fontSize = 19f;
                note.color = new Color(1f, 1f, 1f, 0.45f);
            }
        }

        addBtn = GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => {
                if(pickerOpen) {
                    ClosePicker();
                    return;
                }
                replaceTarget = null;
                OpenPickerAnimated();
            },
            "+ Add Stat",
            idp + "_addstat"
        );
        addBtn.Rect.AddToolTip("DESC_PANEL_ADDSTAT", "Pick a stat to add to this panel.");

        GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => {
                panel.Stats.Clear();
                Save();
                ClosePicker();
                RebuildRows();
            },
            "Clear All Stats",
            idp + "_clearstats"
        ).SetSecondary();

        picker = MakeListContainer("StatPicker", sec.Body, 6f);
        pickerRect = picker.GetComponent<RectTransform>();
        pickerLayout = picker.GetComponent<VerticalLayoutGroup>();
        pickerFitter = picker.GetComponent<ContentSizeFitter>();
        pickerLE = picker.AddComponent<LayoutElement>();
        pickerCg = picker.AddComponent<CanvasGroup>();
        picker.AddComponent<RectMask2D>();

        rows = MakeListContainer("StatRows", sec.Body, 6f);

        RebuildRows();

        // === Appearance ===

        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_APPEARANCE", "Appearance");

        PanelAnchor[] anchors = (PanelAnchor[])Enum.GetValues(typeof(PanelAnchor));
        GenerateUI.DropDown(
            GenerateUI.Row(sec.Body),
            PanelAnchor.TopLeft,
            (PanelAnchor)panel.Anchor,
            anchors,
            AnchorName,
            // Snaps the offset to the new corner's default inset and rebuilds
            // without re-syncing this panel's stale live position.
            v => PanelsOverlay.SetAnchor(panel, v),
            idp + "_anchor",
            260f,
            "Anchor"
        );

        UIInput prefix = GenerateUI.Input(
            GenerateUI.Row(sec.Body),
            def.Prefix,
            panel.Prefix,
            v => { panel.Prefix = v; Save(); },
            "Prefix",
            MainCore.Spr.Get(UISprite.Text128),
            idp + "_prefix"
        );
        prefix.InputField.characterLimit = 32;
        prefix.Rect.AddToolTip("DESC_PANEL_PREFIX", "Extra line shown at the top of the panel.");

        UIInput sep = GenerateUI.Input(
            GenerateUI.Row(sec.Body),
            def.LabelSeparator,
            panel.LabelSeparator,
            v => { panel.LabelSeparator = v; Save(); },
            "Label Separator",
            MainCore.Spr.Get(UISprite.Text128),
            idp + "_separator"
        );
        sep.InputField.characterLimit = 8;

        static float fontFilter(float v) => Mathf.Clamp(Mathf.Round(v), 12f, 48f);
        UISlider font = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.FontSize, 12f, 48f, panel.FontSize,
            fontFilter, null, null,
            "Font Size", idp + "_fontsize"
        );
        font.Format = "0 px";
        font.OnChanged = v => { panel.FontSize = v; PanelsOverlay.Apply(); };
        font.OnComplete = v => { panel.FontSize = v; PanelsOverlay.Apply(); Save(); };

        static float lineFilter(float v) => Mathf.Clamp(Mathf.Round(v * 2f) * 0.5f, -50f, 50f);
        UISlider line = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.LineSpacing, -50f, 50f, panel.LineSpacing,
            lineFilter, null, null,
            "Line Spacing", idp + "_linespacing"
        );
        line.Format = "0.#";
        line.OnChanged = v => { panel.LineSpacing = v; PanelsOverlay.Apply(); };
        line.OnComplete = v => { panel.LineSpacing = v; PanelsOverlay.Apply(); Save(); };

        UISlider decimals = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.Decimals, 0f, 6f, panel.Decimals,
            v => Mathf.Round(v), null, null,
            "Percent Decimals", idp + "_decimals"
        );
        decimals.Format = "0";
        decimals.OnChanged = v => panel.Decimals = (int)v;
        decimals.OnComplete = v => { panel.Decimals = (int)v; Save(); };

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetTextColor(),
            panel.GetTextColor(),
            c => { panel.SetTextColor(c); PanelsOverlay.Apply(); },
            c => { panel.SetTextColor(c); PanelsOverlay.Apply(); Save(); },
            "Text Color",
            idp + "_textcolor"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.BackgroundEnabled,
            panel.BackgroundEnabled,
            v => { panel.BackgroundEnabled = v; PanelsOverlay.Apply(); Save(); },
            "Background Panel",
            idp + "_background"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.LocalizeStatLabels,
            panel.LocalizeStatLabels,
            v => { panel.LocalizeStatLabels = v; PanelsOverlay.Apply(); Save(); },
            "Localize Stat Labels",
            idp + "_localizestats"
        ).Rect.AddToolTip(
            "DESC_PANEL_LOCALIZESTATS",
            "Off: this panel's stat labels stay English (X-Acc, Max X-Acc…). On: they follow the UI language."
        );

        // === Shadow ===

        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_SHADOW", "Shadow");

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.TextShadowEnabled,
            panel.TextShadowEnabled,
            v => { panel.TextShadowEnabled = v; PanelsOverlay.Apply(); Save(); },
            "Text Shadow",
            idp + "_textshadow"
        );

        AddSlider(sec.Body, "Shadow X", idp + "_shadow_x",
            def.TextShadowX, -20f, 20f, panel.TextShadowX, "0.0 px", 0.1f,
            v => panel.TextShadowX = v, PanelsOverlay.Apply, Save);

        AddSlider(sec.Body, "Shadow Y", idp + "_shadow_y",
            def.TextShadowY, -20f, 20f, panel.TextShadowY, "0.0 px", 0.1f,
            v => panel.TextShadowY = v, PanelsOverlay.Apply, Save);

        AddSlider(sec.Body, "Shadow Softness", idp + "_shadow_softness",
            def.TextShadowSoftness, 0f, 20f, panel.TextShadowSoftness, "0.0 px", 0.1f,
            v => panel.TextShadowSoftness = v, PanelsOverlay.Apply, Save);

        GenerateUI.ColorPicker(
            GenerateUI.Row(sec.Body),
            def.GetTextShadowColor(),
            panel.GetTextShadowColor(),
            c => { panel.SetTextShadowColor(c); PanelsOverlay.Apply(); },
            c => { panel.SetTextShadowColor(c); PanelsOverlay.Apply(); Save(); },
            "Shadow Color",
            idp + "_shadow_color"
        );

        // === Panel actions ===

        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)), "HEADING_ACTIONS", "Actions");

        GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => PanelsOverlay.ResetPosition(panel),
            "Reset Position",
            idp + "_resetpos"
        ).SetSecondary();

        GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => {
                PanelsOverlay.Conf.Panels.Remove(panel);
                PanelsOverlay.Save();
                PanelsOverlay.Rebuild();
                RebuildPanelsList();
            },
            "Delete Panel",
            idp + "_delete"
        ).SetSecondary();
    }

    // ===== stat-list row plumbing =====

    private static void AddSlider(
        Transform body, string label, string id,
        float defVal, float min, float max, float val,
        string format, float step,
        Action<float> setter,
        Action live, Action save
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

    private static GameObject MakeListContainer(string name, Transform parent, float spacing) {
        GameObject obj = new(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();

        VerticalLayoutGroup layout = obj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = obj.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return obj;
    }

    private static void ClearChildren(Transform t) {
        for(int i = t.childCount - 1; i >= 0; i--) {
            Object.Destroy(t.GetChild(i).gameObject);
        }
    }

    // One stat entry row: [⠿ drag] [label] ... [enable dot] [Color] [Swap] [X]
    private static void BuildStatRow(
        Transform parent, StatEntry entry,
        Action commitOrder, Action onDelete, Action onSwap, Action onColor, Action save,
        string idp
    ) {
        RectTransform row = GenerateUI.Row(parent);
        row.gameObject.AddComponent<StatRowMarker>().Entry = entry;

        RectTransform bg = GenerateUI.BackGround();
        bg.SetParent(row, false);

        // 6-dot drag handle. Dragging reorders the row among its siblings;
        // dropping commits the new order to the panel config.
        GameObject handle = new("DragHandle");
        handle.transform.SetParent(bg, false);

        RectTransform handleRect = handle.AddComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0f, 0f);
        handleRect.anchorMax = new Vector2(0f, 1f);
        handleRect.pivot = new Vector2(0f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.sizeDelta = new Vector2(40f, 0f);

        handle.AddComponent<EmptyGraphic>().raycastTarget = true;

        StatRowDrag drag = handle.AddComponent<StatRowDrag>();
        drag.Row = row;
        drag.OnReordered = commitOrder;

        for(int col = 0; col < 2; col++) {
            for(int dotRow = 0; dotRow < 3; dotRow++) {
                GameObject dot = new("Dot");
                dot.transform.SetParent(handle.transform, false);

                RectTransform dotRect = dot.AddComponent<RectTransform>();
                dotRect.anchorMin = new Vector2(0.5f, 0.5f);
                dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.anchoredPosition = new Vector2(col * 8f - 4f, dotRow * 8f - 8f);
                dotRect.sizeDelta = new Vector2(4f, 4f);

                Image dotImg = dot.AddComponent<Image>();
                dotImg.sprite = MainCore.Spr.Get(UISprite.Circle256);
                dotImg.color = new Color(1f, 1f, 1f, 0.4f);
                dotImg.raycastTarget = false;
            }
        }

        // The "text" stat edits its custom string right here in the row; every
        // other stat shows its (localized) name as a static label.
        if(entry.Id == "text") {
            BuildTextEntryInput(bg, entry, save);
        } else {
            var label = GenerateUI.AddText(bg, true);
            GenerateUI.Localize(
                label,
                GenerateUI.LocaleKeyFromText("PANEL_STAT", entry.Id),
                StatDefaultLabel(entry.Id)
            );
            RectTransform labelRect = label.rectTransform;
            labelRect.offsetMin = new Vector2(48f, 0f);
            labelRect.offsetMax = new Vector2(-300f, 0f);
        }

        // Enable/disable dot: accent = shown, dim = hidden (kept in the list).
        GameObject toggleObj = new("EnableDot");
        toggleObj.transform.SetParent(bg, false);

        RectTransform toggleRect = toggleObj.AddComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(1f, 0.5f);
        toggleRect.anchorMax = new Vector2(1f, 0.5f);
        toggleRect.pivot = new Vector2(1f, 0.5f);
        toggleRect.anchoredPosition = new Vector2(-240f, 0f);
        toggleRect.sizeDelta = new Vector2(26f, 26f);

        Image toggleImg = toggleObj.AddComponent<Image>();
        toggleImg.sprite = MainCore.Spr.Get(UISprite.Circle256);

        void ApplyToggleColor() {
            toggleImg.color = entry.Enabled
                ? UIColors.ObjectActive
                : new Color(1f, 1f, 1f, 0.18f);
        }
        ApplyToggleColor();

        GenerateUI.AddButton(toggleObj, btn => {
            if(btn != InputButton.Left) {
                return;
            }
            entry.Enabled = !entry.Enabled;
            ApplyToggleColor();
            save();
        });

        // Show-label dot ("T"): accent = label shown, dim = value only. Sits just
        // left of the enable dot.
        GameObject labelDot = new("LabelDot");
        labelDot.transform.SetParent(bg, false);

        RectTransform labelDotRect = labelDot.AddComponent<RectTransform>();
        labelDotRect.anchorMin = new Vector2(1f, 0.5f);
        labelDotRect.anchorMax = new Vector2(1f, 0.5f);
        labelDotRect.pivot = new Vector2(1f, 0.5f);
        labelDotRect.anchoredPosition = new Vector2(-270f, 0f);
        labelDotRect.sizeDelta = new Vector2(26f, 26f);

        Image labelDotImg = labelDot.AddComponent<Image>();
        labelDotImg.sprite = MainCore.Spr.Get(UISprite.Circle256);

        var labelDotText = GenerateUI.AddText(labelDot.transform, true);
        labelDotText.text = "T";
        labelDotText.fontSize = 15f;
        labelDotText.alignment = TextAlignmentOptions.Center;
        labelDotText.raycastTarget = false;

        void ApplyLabelDotColor() {
            labelDotImg.color = entry.ShowLabel
                ? UIColors.ObjectActive
                : new Color(1f, 1f, 1f, 0.18f);
        }
        ApplyLabelDotColor();

        GenerateUI.AddButton(labelDot, btn => {
            if(btn != InputButton.Left) {
                return;
            }
            entry.ShowLabel = !entry.ShowLabel;
            ApplyLabelDotColor();
            save();
        });

        MiniButton(bg, "Color", "COLOR_SHORT", -144f, 88f, onColor);
        MiniButton(bg, "Swap", "SWAP", -56f, 84f, onSwap);
        MiniButton(bg, "X", "DELETE_SHORT", -8f, 44f, onDelete);
    }

    // Collapsible body that hosts a stat's color settings. Starts collapsed
    // and empty; the Color button slides it open/closed.
    private sealed class StatColorBody {
        public RectTransform Rect;
        public VerticalLayoutGroup Layout;
        public ContentSizeFitter Fitter;
        public LayoutElement LE;
        public CanvasGroup CG;
        public GTween Seq;
    }

    private static StatColorBody CreateColorBody(Transform parent) {
        GameObject obj = new("StatColorBody");
        obj.transform.SetParent(parent, false);

        StatColorBody body = new() {
            Rect = obj.AddComponent<RectTransform>(),
        };

        body.Layout = obj.AddComponent<VerticalLayoutGroup>();
        body.Layout.spacing = 6f;
        body.Layout.padding = new RectOffset(40, 0, 0, 6);
        body.Layout.childControlWidth = true;
        body.Layout.childControlHeight = true;
        body.Layout.childForceExpandWidth = true;
        body.Layout.childForceExpandHeight = false;

        body.Fitter = obj.AddComponent<ContentSizeFitter>();
        body.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        body.LE = obj.AddComponent<LayoutElement>();
        body.LE.preferredHeight = 0f;

        body.CG = obj.AddComponent<CanvasGroup>();
        body.CG.alpha = 0f;

        obj.AddComponent<RectMask2D>();

        return body;
    }

    // Per-stat color settings, expanded under the stat's row — v1's ColorRange
    // editor: enable toggle, gradient stops (position + color, add/delete),
    // perfect-color override, and Max BPM for the BPM-driven stats.
    private static void BuildStatColorSettings(
        Transform parent, StatEntry entry, Action save, Action rebuild, string idp
    ) {
        StatColor color = entry.EnsureColor();
        bool hasRatio = StatColor.HasRatio(entry.Id);

        GenerateUI.Toggle(
            GenerateUI.Row(parent),
            false,
            color.Enabled,
            v => { color.Enabled = v; save(); },
            "Custom Color",
            idp + "_statcolor_on"
        ).Rect.AddToolTip(
            "DESC_PANEL_STATCOLOR_ON",
            "Tints this stat's value by blending the colors below across the stat's own 0–100% range."
        );

        if(StatColor.IsBpm(entry.Id)) {
            UISlider maxBpm = GenerateUI.Slider(
                GenerateUI.Row(parent),
                8000f, 1f, 9999f, color.MaxBpm,
                v => Mathf.Clamp(Mathf.Round(v), 1f, 9999f), null, null,
                "Color Max BPM", idp + "_statcolor_maxbpm"
            );
            maxBpm.Format = "0";
            maxBpm.OnChanged = v => color.MaxBpm = v;
            maxBpm.OnComplete = v => { color.MaxBpm = v; save(); };
            maxBpm.Rect.AddToolTip(
                "DESC_PANEL_STATCOLOR_MAXBPM",
                "BPM that maps to the 100% end of the gradient."
            );
        }

        // === Gradient stops ===
        for(int i = 0; i < color.Points.Count; i++) {
            ColorPoint point = color.Points[i];

            if(hasRatio) {
                RectTransform posRow = GenerateUI.Row(parent);
                UISlider pos = GenerateUI.Slider(
                    posRow,
                    100f, 0f, 100f, point.Pos * 100f,
                    v => Mathf.Clamp(Mathf.Round(v * 2f) * 0.5f, 0f, 100f), null, null,
                    "Position", idp + "_statcolor_pos"
                );
                // '%' must be quoted: bare % in a .NET format string multiplies
                // the value by 100 (slider already holds 0..100).
                pos.Format = "0.#' %'";
                pos.OnChanged = v => point.Pos = v * 0.01f;
                pos.OnComplete = v => {
                    point.Pos = v * 0.01f;
                    color.SortPoints();
                    save();
                };

                if(color.Points.Count > 1) {
                    MiniButton(posRow, "X", "DELETE_SHORT", -8f, 44f, () => {
                        color.Points.Remove(point);
                        save();
                        rebuild();
                    });
                }
            }

            GenerateUI.ColorPicker(
                GenerateUI.Row(parent),
                Color.white,
                point.GetColor(),
                c => point.SetColor(c),
                c => { point.SetColor(c); save(); },
                "Color",
                idp + "_statcolor_color"
            );

            if(!hasRatio && color.Points.Count > 1) {
                // No position rows for static stats — extra stops are
                // meaningless there, offer delete on its own row.
                MiniButton(GenerateUI.Row(parent, 40f), "X", "DELETE_SHORT", -8f, 44f, () => {
                    color.Points.Remove(point);
                    save();
                    rebuild();
                });
            }
        }

        if(hasRatio && color.Points.Count < 8) {
            GenerateUI.Button(
                GenerateUI.Row(parent),
                () => {
                    float pos = color.Points.Count > 0 ? 0.5f : 1f;
                    color.Points.Add(new ColorPoint(pos, color.Evaluate(pos)));
                    color.SortPoints();
                    save();
                    rebuild();
                },
                "+ Add Color",
                idp + "_statcolor_add"
            ).SetSecondary();
        }

        // === Perfect color (v1 gold at 100%) ===
        if(hasRatio) {
            GenerateUI.Toggle(
                GenerateUI.Row(parent),
                false,
                color.UsePerfect,
                v => { color.UsePerfect = v; save(); },
                "Perfect Color (100%)",
                idp + "_statcolor_perfect"
            ).Rect.AddToolTip(
                "DESC_PANEL_STATCOLOR_PERFECT",
                "Overrides the gradient with this color while the stat sits at exactly 100% — v1's gold accuracy."
            );

            GenerateUI.ColorPicker(
                GenerateUI.Row(parent),
                new Color(1f, 0.854902f, 0f, 1f),
                color.Perfect.GetColor(),
                c => color.Perfect.SetColor(c),
                c => { color.Perfect.SetColor(c); save(); },
                "Perfect Color",
                idp + "_statcolor_perfectcolor"
            );
        }
    }

    private static string AnchorName(PanelAnchor anchor) => anchor switch {
        PanelAnchor.TopLeft => MainCore.Tr.Get("ANCHOR_TOP_LEFT", "Top Left"),
        PanelAnchor.TopCenter => MainCore.Tr.Get("ANCHOR_TOP_CENTER", "Top Center"),
        PanelAnchor.TopRight => MainCore.Tr.Get("ANCHOR_TOP_RIGHT", "Top Right"),
        PanelAnchor.MiddleLeft => MainCore.Tr.Get("ANCHOR_MIDDLE_LEFT", "Middle Left"),
        PanelAnchor.MiddleCenter => MainCore.Tr.Get("ANCHOR_MIDDLE_CENTER", "Middle Center"),
        PanelAnchor.MiddleRight => MainCore.Tr.Get("ANCHOR_MIDDLE_RIGHT", "Middle Right"),
        PanelAnchor.BottomLeft => MainCore.Tr.Get("ANCHOR_BOTTOM_LEFT", "Bottom Left"),
        PanelAnchor.BottomCenter => MainCore.Tr.Get("ANCHOR_BOTTOM_CENTER", "Bottom Center"),
        PanelAnchor.BottomRight => MainCore.Tr.Get("ANCHOR_BOTTOM_RIGHT", "Bottom Right"),
        _ => anchor.ToString(),
    };

    private static string StatDefaultLabel(string id) {
        foreach(PanelsOverlay.StatDef stat in PanelsOverlay.Catalog) {
            if(stat.Id == id) {
                return stat.Label;
            }
        }
        return id;
    }

    private static void MiniButton(Transform parent, string text, string key, float rightOffset, float width, Action onClick) {
        GameObject obj = new("MiniBtn_" + text);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(rightOffset, 0f);
        rect.sizeDelta = new Vector2(width, 36f);

        Image img = obj.AddComponent<Image>();
        img.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        img.type = Image.Type.Sliced;
        img.color = UIColors.ObjectButton;

        var label = GenerateUI.AddText(obj.transform, true);
        GenerateUI.Localize(label, key, text);
        label.fontSize = 18f;
        label.alignment = TextAlignmentOptions.Center;

        GenerateUI.AddButton(obj, btn => {
            if(btn == InputButton.Left) {
                onClick();
            }
        });
    }

    // Inline editor for a "text" stat row: a single-line input sitting where the
    // static stat label would be, bound to the entry's custom string. Edits flow
    // straight to entry.Text and the live panel picks them up next frame.
    private static void BuildTextEntryInput(Transform bg, StatEntry entry, Action save) {
        GameObject inputObj = new("TextInput");
        inputObj.transform.SetParent(bg, false);

        RectTransform inputRect = inputObj.AddComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0f, 0f);
        inputRect.anchorMax = new Vector2(1f, 1f);
        inputRect.pivot = new Vector2(0.5f, 0.5f);
        inputRect.offsetMin = new Vector2(48f, 6f);
        inputRect.offsetMax = new Vector2(-300f, -6f);

        Image fieldBg = inputObj.AddComponent<Image>();
        fieldBg.color = UIColors.ObjectBG;
        fieldBg.raycastTarget = true;

        inputObj.AddComponent<RectMask2D>();

        TMP_InputField field = inputObj.AddComponent<TMP_InputField>();

        var text = GenerateUI.AddText(inputObj.transform, true);
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        SetFullRect(text.rectTransform, 10f);

        var placeholder = GenerateUI.AddText(inputObj.transform, true);
        placeholder.alignment = TextAlignmentOptions.Left;
        placeholder.textWrappingMode = TextWrappingModes.NoWrap;
        placeholder.color = new Color(1f, 1f, 1f, 0.3f);
        GenerateUI.Localize(placeholder, "PANEL_TEXT_PLACEHOLDER", "Custom text…");
        SetFullRect(placeholder.rectTransform, 10f);

        field.textViewport = inputRect;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.richText = false;
        field.characterLimit = 64;
        field.SetTextWithoutNotify(entry.Text ?? "");
        field.onValueChanged.AddListener(v => { entry.Text = v; save(); });
    }

    private static void SetFullRect(RectTransform rect, float xPad) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(xPad, 0f);
        rect.offsetMax = new Vector2(-xPad, 0f);
    }

    // Ties a list row back to its config entry so a reorder commit can read
    // the new order straight off the hierarchy.
    private sealed class StatRowMarker : MonoBehaviour {
        public StatEntry Entry;
    }

    // Drag-to-reorder for stat rows. The dragged row leaves the layout and
    // floats with the pointer (slightly scaled up); a placeholder gap slides
    // through the list to mark the drop slot. On release the row glides into
    // the gap, then the hierarchy order is committed to the panel config.
    private sealed class StatRowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler {
        public RectTransform Row;
        public Action OnReordered;

        private LayoutElement rowLE;
        private RectTransform placeholder;
        private float grabOffsetY;
        private GTween scaleSeq;
        private GTween dropSeq;
        private bool dragging;

        // Per-row slide tweens for the NOT-dragged rows: every placeholder
        // move reflows the layout instantly, which would teleport the rows
        // shoved aside — instead their old visual position is captured, the
        // layout is rebuilt, and they glide from old to new slot.
        private readonly Dictionary<RectTransform, GTween> rowSlides = [];
        private readonly List<(RectTransform rt, Vector2 oldPos)> reflowCapture = [];

        private void AnimateReflow(Transform container) {
            reflowCapture.Clear();
            for(int i = 0; i < container.childCount; i++) {
                Transform child = container.GetChild(i);
                if(child == Row || child.GetComponent<StatRowMarker>() == null) {
                    continue;
                }
                RectTransform rt = (RectTransform)child;
                reflowCapture.Add((rt, rt.anchoredPosition));
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)container);

            foreach((RectTransform rt, Vector2 oldPos) in reflowCapture) {
                Vector2 target = rt.anchoredPosition;
                if((target - oldPos).sqrMagnitude < 0.01f) {
                    continue;
                }

                rt.anchoredPosition = oldPos;

                if(rowSlides.TryGetValue(rt, out GTween running)) {
                    running?.Kill();
                }

                GTween slide = GTweenExtensions.Tween(
                    () => rt.anchoredPosition.y,
                    y => rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y),
                    target.y,
                    0.15f
                ).SetEasing(Easing.OutCubic);
                rowSlides[rt] = slide;
                MainCore.TC.Play(slide);
            }
        }

        public void OnBeginDrag(PointerEventData eventData) {
            if(Row == null || Row.parent == null) {
                return;
            }

            // A previous drop animation still running: jump it to its end so
            // the placeholder/layout state is clean before re-grabbing.
            if(dropSeq != null) {
                dropSeq.Complete();
                dropSeq.Kill();
                dropSeq = null;
            }

            dragging = true;

            rowLE = Row.GetComponent<LayoutElement>();

            // Gap that holds the row's slot while it floats.
            GameObject ph = new("DragPlaceholder");
            ph.transform.SetParent(Row.parent, false);
            placeholder = ph.AddComponent<RectTransform>();
            LayoutElement phLE = ph.AddComponent<LayoutElement>();
            phLE.preferredHeight = Row.rect.height;
            phLE.minHeight = Row.rect.height;
            placeholder.SetSiblingIndex(Row.GetSiblingIndex());

            if(rowLE != null) {
                rowLE.ignoreLayout = true;
            }
            Row.SetAsLastSibling();

            grabOffsetY = Row.position.y - eventData.position.y;

            PlayScale(1.04f);
        }

        public void OnDrag(PointerEventData eventData) {
            if(!dragging || Row == null || placeholder == null) {
                return;
            }

            Vector3 pos = Row.position;
            pos.y = eventData.position.y + grabOffsetY;
            Row.position = pos;

            // Slot index = how many other rows sit above the pointer.
            Transform container = Row.parent;
            int target = 0;
            for(int i = 0; i < container.childCount; i++) {
                Transform child = container.GetChild(i);
                if(child == Row || child == placeholder) {
                    continue;
                }
                if(child.GetComponent<StatRowMarker>() == null) {
                    continue;
                }
                if(((RectTransform)child).position.y > eventData.position.y) {
                    target++;
                }
            }

            if(placeholder.GetSiblingIndex() != target) {
                placeholder.SetSiblingIndex(target);
                AnimateReflow(container);
            }
        }

        public void OnEndDrag(PointerEventData eventData) {
            if(!dragging || Row == null || placeholder == null) {
                return;
            }

            dragging = false;

            Transform container = Row.parent;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)container);

            float targetY = placeholder.position.y;
            int finalIndex = placeholder.GetSiblingIndex();

            RectTransform ph = placeholder;
            placeholder = null;

            PlayScale(1f);

            // Glide into the gap, then rejoin the layout at the gap's slot.
            dropSeq = GTweenSequenceBuilder.New()
                .Append(GTweenExtensions.Tween(
                    () => Row.position.y,
                    y => {
                        Vector3 pos = Row.position;
                        pos.y = y;
                        Row.position = pos;
                    },
                    targetY,
                    0.12f
                ).SetEasing(Easing.OutCubic))
                .AppendCallback(() => {
                    if(ph != null) {
                        ph.gameObject.SetActive(false);
                        Object.Destroy(ph.gameObject);
                    }
                    if(rowLE != null) {
                        rowLE.ignoreLayout = false;
                    }
                    if(Row != null) {
                        Row.SetSiblingIndex(finalIndex);
                        Row.localScale = Vector3.one;
                        // Rejoining the layout reflows once more — let any
                        // rows still mid-slide glide to their final slots
                        // instead of snapping.
                        AnimateReflow(Row.parent);
                    }
                    OnReordered?.Invoke();
                })
                .Build();
            MainCore.TC.Play(dropSeq);
        }

        private void PlayScale(float target) {
            if(Row == null) {
                return;
            }

            scaleSeq?.Kill();
            scaleSeq = GTweenExtensions.Tween(
                () => Row.localScale.x,
                x => Row.localScale = new Vector3(x, x, 1f),
                target,
                0.12f
            ).SetEasing(Easing.OutSine);
            MainCore.TC.Play(scaleSeq);
        }
    }
}
