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

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

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
            note.text = "No panels. Create one above.";
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

        GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)).text = "Stats";

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
                addBtn.Label.text = "+ Add Stat";
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
                addBtn.Label.text = "Close";
            }
            BuildPicker();
            AnimatePicker(true);
        }

        void RebuildRows() {
            ClearChildren(rows.transform);

            if(panel.Stats.Count == 0) {
                var note = GenerateUI.AddText(GenerateUI.Row(rows.transform));
                note.text = "No stats on this panel.";
                note.fontSize = 19f;
                note.color = new Color(1f, 1f, 1f, 0.45f);
                return;
            }

            foreach(StatEntry entry in panel.Stats) {
                BuildStatRow(rows.transform, entry, CommitOrder, () => {
                    panel.Stats.Remove(entry);
                    Save();
                    RebuildRows();
                }, () => {
                    // Swap: open the picker targeting this entry.
                    replaceTarget = entry;
                    OpenPickerAnimated();
                }, Save, idp);
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
                    // being replaced — swapping to itself is a no-op).
                    if(panel.Stats.Exists(e => e.Id == stat.Id)) {
                        continue;
                    }

                    if(!headerAdded) {
                        headerAdded = true;
                        var header = GenerateUI.AddText(GenerateUI.Row(picker.transform, 32f));
                        header.text = category;
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
                                panel.Stats.Add(new StatEntry(statId));
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
                note.text = "All stats are already on this panel.";
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

        GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)).text = "Appearance";

        PanelAnchor[] anchors = (PanelAnchor[])Enum.GetValues(typeof(PanelAnchor));
        GenerateUI.DropDown(
            GenerateUI.Row(sec.Body),
            PanelAnchor.TopLeft,
            (PanelAnchor)panel.Anchor,
            anchors,
            AnchorName,
            v => {
                panel.Anchor = (int)v;
                // Offsets are relative to the anchor; jumping anchors keeps
                // the old offset meaningless, so snap to the new corner's
                // default inset and let the user drag from there.
                Vector2 def = PanelConfig.DefaultOffset(v);
                panel.PosX = def.x;
                panel.PosY = def.y;
                Save();
                PanelsOverlay.Rebuild();
            },
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

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.BackgroundEnabled,
            panel.BackgroundEnabled,
            v => { panel.BackgroundEnabled = v; PanelsOverlay.Apply(); Save(); },
            "Background Panel",
            idp + "_background"
        );

        // === Panel actions ===

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

    // One stat entry row: [⠿ drag] [label] ......... [enable dot] [Swap] [X]
    private static void BuildStatRow(
        Transform parent, StatEntry entry,
        Action commitOrder, Action onDelete, Action onSwap, Action save,
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

        var label = GenerateUI.AddText(bg, true);
        label.text = StatLabel(entry.Id);
        RectTransform labelRect = label.rectTransform;
        labelRect.offsetMin = new Vector2(48f, 0f);

        // Enable/disable dot: accent = shown, dim = hidden (kept in the list).
        GameObject toggleObj = new("EnableDot");
        toggleObj.transform.SetParent(bg, false);

        RectTransform toggleRect = toggleObj.AddComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(1f, 0.5f);
        toggleRect.anchorMax = new Vector2(1f, 0.5f);
        toggleRect.pivot = new Vector2(1f, 0.5f);
        toggleRect.anchoredPosition = new Vector2(-150f, 0f);
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

        MiniButton(bg, "Swap", -56f, 84f, onSwap);
        MiniButton(bg, "X", -8f, 44f, onDelete);
    }

    private static string AnchorName(PanelAnchor anchor) => anchor switch {
        PanelAnchor.TopLeft => "Top Left",
        PanelAnchor.TopCenter => "Top Center",
        PanelAnchor.TopRight => "Top Right",
        PanelAnchor.MiddleLeft => "Middle Left",
        PanelAnchor.MiddleCenter => "Middle Center",
        PanelAnchor.MiddleRight => "Middle Right",
        PanelAnchor.BottomLeft => "Bottom Left",
        PanelAnchor.BottomCenter => "Bottom Center",
        PanelAnchor.BottomRight => "Bottom Right",
        _ => anchor.ToString(),
    };

    private static string StatLabel(string id) {
        foreach(PanelsOverlay.StatDef stat in PanelsOverlay.Catalog) {
            if(stat.Id == id) {
                return stat.Label;
            }
        }
        return id;
    }

    private static void MiniButton(Transform parent, string text, float rightOffset, float width, Action onClick) {
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
        label.text = text;
        label.fontSize = 18f;
        label.alignment = TextAlignmentOptions.Center;

        GenerateUI.AddButton(obj, btn => {
            if(btn == InputButton.Left) {
                onClick();
            }
        });
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
