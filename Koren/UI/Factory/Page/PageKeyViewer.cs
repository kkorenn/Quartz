using Koren.Core;
using Koren.Features.KeyViewer;
using Koren.Resource;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityEngine.EventSystems.PointerEventData;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI.Factory.Page;

// Key Viewer settings section for the Overlay tab — v1's "simple" key viewer.
// The Keys group is an interactive preview of the grid: click a key, press
// the new binding (Esc cancels), and edit the selected key's label below.
// Rain and foot keys come with later port slices.
internal static class PageKeyViewer {
    private static readonly int[] styles = [0, 1, 2, 3];

    public static void AppendTo(Transform content) {
        KeyViewerOverlay.EnsureConf();
        KeyViewerSettings conf = KeyViewerOverlay.Conf;
        KeyViewerSettings def = new();

        void Save() => KeyViewerOverlay.Save();
        void Apply() => KeyViewerOverlay.Apply();

        var sec = GenerateUI.Collapsible(
            content, "Key Viewer", startExpanded: false,
            v => { conf.Enabled = v; Save(); },
            conf.Enabled
        );

        RectTransform simpleBody = null;
        RectTransform dmNoteBody = null;
        Image simpleModeBg = null;
        Image dmNoteModeBg = null;
        TextMeshProUGUI simpleModeLabel = null;
        TextMeshProUGUI dmNoteModeLabel = null;

        void RefreshMode() {
            bool simple = conf.IsSimpleMode;
            ApplyModeButton(simpleModeBg, simpleModeLabel, simple);
            ApplyModeButton(dmNoteModeBg, dmNoteModeLabel, !simple);

            simpleBody?.gameObject.SetActive(simple);
            dmNoteBody?.gameObject.SetActive(!simple);

            if(sec.Body != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(sec.Body);
            }
        }

        void SetMode(string mode) {
            mode = KeyViewerSettings.NormalizeMode(mode);
            if(conf.Mode == mode) {
                RefreshMode();
                return;
            }

            conf.Mode = mode;
            KeyViewerOverlay.Rebuild();
            Save();
            RefreshMode();
            // Sync only runs in simple mode — leaving/entering it changes
            // whether the Key Limiter is locked.
            KeyViewerOverlay.RaiseSyncSettingChanged();
        }

        RectTransform modeRow = GenerateUI.Row(sec.Body);
        AddModeButton(modeRow, "Simple", "KEYVIEWER_MODE_SIMPLE", () => SetMode(KeyViewerSettings.ModeSimple), out simpleModeBg, out simpleModeLabel);
        AddModeButton(modeRow, "DM Note", "KEYVIEWER_MODE_DMNOTE", () => SetMode(KeyViewerSettings.ModeDmNote), out dmNoteModeBg, out dmNoteModeLabel);

        simpleBody = AddModeBody(sec.Body, "SimpleMode");
        dmNoteBody = AddModeBody(sec.Body, "DmNoteMode");

        // Declared ahead: the style dropdown rebuilds the preview.
        Action rebuildPreview = null;

        GenerateUI.DropDown(
            GenerateUI.Row(simpleBody),
            def.Style,
            Mathf.Clamp(conf.Style, 0, 3),
            styles,
            StyleName,
            v => {
                conf.Style = v;
                KeyViewerOverlay.Rebuild();
                Save();
                rebuildPreview?.Invoke();
            },
            "keyviewer_style",
            260f,
            "Style"
        );

        UISlider size = GenerateUI.Slider(
            GenerateUI.Row(simpleBody),
            def.Size, 0.25f, 3f, conf.Size,
            v => Mathf.Round(v * 100f) * 0.01f, null, null,
            "Size", "keyviewer_size"
        );
        size.Format = "0.00 x";
        size.OnChanged = v => { conf.Size = v; Apply(); };
        size.OnComplete = v => { conf.Size = v; Apply(); Save(); };

        // === Keys: interactive rebind preview ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(simpleBody)), "HEADING_KEYS", "Keys");

        var hint = GenerateUI.AddText(GenerateUI.Row(simpleBody, 30f));
        GenerateUI.Localize(hint, "KEYVIEWER_KEYS_HINT", "Click a key, then press the new binding. Esc cancels.");
        hint.fontSize = 17f;
        hint.color = new Color(1f, 1f, 1f, 0.45f);

        // Sized for the tallest style (20 keys) so switching styles doesn't
        // reflow the page.
        RectTransform previewRow = GenerateUI.Row(simpleBody, 175f);

        GameObject previewObj = new("KeyViewerPreview");
        previewObj.transform.SetParent(previewRow, false);
        RectTransform preview = previewObj.AddComponent<RectTransform>();
        preview.anchorMin = new Vector2(0.5f, 0.5f);
        preview.anchorMax = new Vector2(0.5f, 0.5f);
        preview.pivot = new Vector2(0.5f, 0.5f);

        int selectedSlot = -1;
        bool listening = false;
        var previewBoxes = new Dictionary<int, (Image fill, Image border, TextMeshProUGUI label)>();
        var statBoxes = new List<(Image fill, Image border, TextMeshProUGUI label)>();
        UIInput labelInput = null;

        void RefreshPreviewVisuals() {
            int style = Mathf.Clamp(conf.Style, 0, 3);
            foreach((int slot, (Image fill, Image border, TextMeshProUGUI label)) in previewBoxes) {
                bool selected = slot == selectedSlot;
                fill.color = conf.GetBg();
                border.color = selected ? UIColors.ObjectActive : conf.GetOutline();
                label.color = conf.GetText();
                label.text = selected && listening ? "..." : KeyViewerOverlay.LabelFor(style, slot);
            }

            // KPS/Total boxes follow the same colors, dimmed — recolored here
            // so live color edits update them like the key boxes.
            foreach((Image fill, Image border, TextMeshProUGUI label) in statBoxes) {
                Color dim = conf.GetBg();
                dim.a *= 0.5f;
                fill.color = dim;

                Color dimBorder = conf.GetOutline();
                dimBorder.a *= 0.5f;
                border.color = dimBorder;

                Color dimText = conf.GetText();
                dimText.a *= 0.6f;
                label.color = dimText;
            }
        }

        void SelectSlot(int slot) {
            selectedSlot = slot;
            listening = true;

            int style = Mathf.Clamp(conf.Style, 0, 3);
            string[] overrides = conf.LabelsForStyle(style);
            string current = slot >= 0 && slot < overrides.Length ? overrides[slot] ?? "" : "";
            labelInput?.Set(current, invoke: false);

            RefreshPreviewVisuals();
        }

        void CancelListening() {
            listening = false;
            RefreshPreviewVisuals();
        }

        void OnKeyCaptured(KeyCode key) {
            int style = Mathf.Clamp(conf.Style, 0, 3);
            int[] keys = conf.KeysForStyle(style);
            if(selectedSlot >= 0 && selectedSlot < keys.Length) {
                keys[selectedSlot] = (int)key;
                Save();
                KeyViewerOverlay.Rebuild();
            }
            listening = false;
            RefreshPreviewVisuals();
        }

        rebuildPreview = () => {
            for(int i = preview.childCount - 1; i >= 0; i--) {
                Object.Destroy(preview.GetChild(i).gameObject);
            }
            previewBoxes.Clear();
            statBoxes.Clear();
            selectedSlot = -1;
            listening = false;
            labelInput?.Set("", invoke: false);

            int style = Mathf.Clamp(conf.Style, 0, 3);
            preview.sizeDelta = KeyViewerOverlay.GridSize(style);

            List<KeyViewerOverlay.KeySlot> keySlots = [];
            List<KeyViewerOverlay.StatSlot> statSlots = [];
            KeyViewerOverlay.BuildLayout(style, keySlots, statSlots);

            foreach(KeyViewerOverlay.KeySlot slot in keySlots) {
                (Image fill, Image border) = KeyViewerOverlay.NewBoxVisual(
                    "Preview_" + slot.Slot, preview, slot.X, slot.Y, slot.W, slot.H
                );
                fill.raycastTarget = true;

                TextMeshProUGUI label = KeyViewerOverlay.NewText(
                    fill.transform, "Label", "", 18f
                );
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                int captured = slot.Slot;
                GenerateUI.AddButton(fill.gameObject, btn => {
                    if(btn == InputButton.Left) {
                        SelectSlot(captured);
                    }
                });

                previewBoxes[slot.Slot] = (fill, border, label);
            }

            // Stat boxes: shown for layout fidelity, not clickable. Colors are
            // applied by RefreshPreviewVisuals below.
            foreach(KeyViewerOverlay.StatSlot slot in statSlots) {
                (Image fill, Image border) = KeyViewerOverlay.NewBoxVisual(
                    slot.Total ? "PreviewTotal" : "PreviewKps", preview, slot.X, slot.Y, slot.W, slot.H
                );

                TextMeshProUGUI label = KeyViewerOverlay.NewText(
                    fill.transform, "Label", slot.Total ? "Total" : "KPS", 16f
                );
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                statBoxes.Add((fill, border, label));
            }

            RefreshPreviewVisuals();
        };

        KeyCaptureRunner runner = previewObj.AddComponent<KeyCaptureRunner>();
        runner.IsListening = () => listening;
        // Any focused input field (key label, slider value editors) means the
        // user is typing, not rebinding.
        runner.ShouldCancel = () => {
            if(labelInput != null && labelInput.InputField.isFocused) {
                return true;
            }
            GameObject sel = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            TMP_InputField field = sel != null ? sel.GetComponent<TMP_InputField>() : null;
            return field != null && field.isFocused;
        };
        runner.OnCaptured = OnKeyCaptured;
        runner.OnCancelled = CancelListening;

        labelInput = GenerateUI.Input(
            GenerateUI.Row(simpleBody),
            "",
            "",
            v => {
                int style = Mathf.Clamp(conf.Style, 0, 3);
                string[] overrides = conf.LabelsForStyle(style);
                if(selectedSlot >= 0 && selectedSlot < overrides.Length) {
                    overrides[selectedSlot] = v ?? "";
                    Save();
                    KeyViewerOverlay.Rebuild();
                    RefreshPreviewVisuals();
                }
            },
            "Key Label (empty = default)",
            MainCore.Spr.Get(UISprite.Text128),
            "keyviewer_keylabel"
        );
        labelInput.InputField.characterLimit = 8;
        labelInput.Rect.AddToolTip(
            "DESC_KEYVIEWER_KEYLABEL",
            "Custom caption for the selected key. Leave empty to derive it from the bound key."
        );

        GenerateUI.Toggle(
            GenerateUI.Row(simpleBody),
            def.SyncToKeyLimiter,
            conf.SyncToKeyLimiter,
            v => {
                conf.SyncToKeyLimiter = v;
                Save();
                if(v) {
                    KeyViewerOverlay.SyncKeysToKeyLimiter();
                }
                KeyViewerOverlay.RaiseSyncSettingChanged();
            },
            "Sync Keys to Key Limiter",
            "keyviewer_synclimiter"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_SYNCLIMITER",
            "Overwrites the Key Limiter's allowed keys with the keys shown here, and keeps them matched when you rebind keys or switch styles."
        );

        rebuildPreview();

        // === Rain ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(simpleBody)), "HEADING_RAIN", "Rain");

        GenerateUI.Toggle(
            GenerateUI.Row(simpleBody),
            def.RainEnabled,
            conf.RainEnabled,
            v => { conf.RainEnabled = v; Apply(); Save(); },
            "Enable Rain",
            "keyviewer_rain"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_RAIN",
            "Streaks rising from a key while it's held."
        );

        AddSlider(simpleBody, "Rain Speed", "keyviewer_rainspeed",
            def.RainSpeed, 50f, 1000f, conf.RainSpeed, "0 px/s", 10f,
            v => conf.RainSpeed = v, Save);

        AddSlider(simpleBody, "Rain Height", "keyviewer_rainheight",
            def.RainHeight, 50f, 600f, conf.RainHeight, "0 px", 5f,
            v => conf.RainHeight = v, Save);

        AddSlider(simpleBody, "Rain Fade", "keyviewer_rainfade",
            def.RainFade, 0f, 300f, conf.RainFade, "0 px", 5f,
            v => conf.RainFade = v, Save);

        UISlider rainWidth = AddSlider(simpleBody, "Rain Width (0 = key width)", "keyviewer_rainwidth",
            def.RainWidth, 0f, 100f, conf.RainWidth, "0 px", 1f,
            v => conf.RainWidth = v, Save);
        rainWidth.Rect.AddToolTip(
            "DESC_KEYVIEWER_RAINWIDTH",
            "Streak width for the front key row. 0 matches each key's width."
        );

        AddSlider(simpleBody, "Rain 2 Width (0 = key width)", "keyviewer_rain2width",
            def.Rain2Width, 0f, 100f, conf.Rain2Width, "0 px", 1f,
            v => conf.Rain2Width = v, Save);

        AddSlider(simpleBody, "Rain Offset Y", "keyviewer_rainoffsety",
            def.RainOffsetY, -100f, 100f, conf.RainOffsetY, "0 px", 1f,
            v => conf.RainOffsetY = v, Save);

        AddSlider(simpleBody, "Rain 2 Offset Y", "keyviewer_rain2offsety",
            def.Rain2OffsetY, -100f, 100f, conf.Rain2OffsetY, "0 px", 1f,
            v => conf.Rain2OffsetY = v, Save);

        AddColor(simpleBody, "Rain Color (Front Row)", "keyviewer_raincolor",
            def.GetRain(), conf.GetRain(), conf.SetRain, Apply, Save, RefreshPreviewVisuals);
        AddColor(simpleBody, "Rain 2 Color (Back Row)", "keyviewer_rain2color",
            def.GetRain2(), conf.GetRain2(), conf.SetRain2, Apply, Save, RefreshPreviewVisuals);
        AddColor(simpleBody, "Rain 3 Color (Third Row)", "keyviewer_rain3color",
            def.GetRain3(), conf.GetRain3(), conf.SetRain3, Apply, Save, RefreshPreviewVisuals);

        // === Color ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(simpleBody)), "HEADING_COLOR", "Color");

        AddColor(simpleBody, "Background", "keyviewer_bg",
            def.GetBg(), conf.GetBg(), conf.SetBg, Apply, Save, RefreshPreviewVisuals);
        AddColor(simpleBody, "Background Pressed", "keyviewer_bgpressed",
            def.GetBgPressed(), conf.GetBgPressed(), conf.SetBgPressed, Apply, Save, RefreshPreviewVisuals);
        AddColor(simpleBody, "Outline", "keyviewer_outline",
            def.GetOutline(), conf.GetOutline(), conf.SetOutline, Apply, Save, RefreshPreviewVisuals);
        AddColor(simpleBody, "Outline Pressed", "keyviewer_outlinepressed",
            def.GetOutlinePressed(), conf.GetOutlinePressed(), conf.SetOutlinePressed, Apply, Save, RefreshPreviewVisuals);
        AddColor(simpleBody, "Text", "keyviewer_text",
            def.GetText(), conf.GetText(), conf.SetText, Apply, Save, RefreshPreviewVisuals);
        AddColor(simpleBody, "Text Pressed", "keyviewer_textpressed",
            def.GetTextPressed(), conf.GetTextPressed(), conf.SetTextPressed, Apply, Save, RefreshPreviewVisuals);

        // === Actions ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(simpleBody)), "HEADING_ACTIONS", "Actions");

        GenerateUI.Button(
            GenerateUI.Row(simpleBody),
            () => KeyViewerOverlay.ResetPosition(),
            "Reset Position",
            "keyviewer_resetpos"
        ).SetSecondary();

        GenerateUI.Button(
            GenerateUI.Row(simpleBody),
            () => KeyViewerOverlay.ResetCounts(),
            "Reset Counts",
            "keyviewer_resetcounts"
        ).SetSecondary().Rect.AddToolTip(
            "DESC_KEYVIEWER_RESETCOUNTS",
            "Clears every per-key press counter and the total."
        );

        // === DM Note ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(dmNoteBody)), "HEADING_DM_NOTE", "DM Note");

        var presetStatus = GenerateUI.AddText(GenerateUI.Row(dmNoteBody, 30f));
        presetStatus.fontSize = 17f;
        presetStatus.color = new Color(1f, 1f, 1f, 0.45f);
        void RefreshPresetStatus() {
            presetStatus.text = string.IsNullOrWhiteSpace(conf.DmPresetJson)
                ? MainCore.Tr.Get("KEYVIEWER_DM_NO_PRESET", "No preset loaded")
                : string.Format(MainCore.Tr.Get("KEYVIEWER_DM_PRESET_LOADED", "Preset loaded: {0} chars"), conf.DmPresetJson.Length);
        }

        GenerateUI.Button(
            GenerateUI.Row(dmNoteBody),
            () => {
                if(KeyViewerOverlay.ImportDmNotePreset(out string error)) {
                    RefreshPresetStatus();
                } else if(!string.IsNullOrEmpty(error)) {
                    presetStatus.text = error;
                }
            },
            "Import Preset",
            "keyviewer_dm_import"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_DM_IMPORT",
            "Select a DM Note preset JSON file."
        );

        GenerateUI.Button(
            GenerateUI.Row(dmNoteBody),
            () => {
                conf.DmPresetJson = "";
                KeyViewerOverlay.Rebuild();
                Save();
                RefreshPresetStatus();
            },
            "Clear Preset",
            "keyviewer_dm_clear"
        ).SetSecondary();

        UIInput selectedTab = GenerateUI.Input(
            GenerateUI.Row(dmNoteBody),
            "4key",
            conf.DmSelectedTab ?? "4key",
            v => {
                conf.DmSelectedTab = string.IsNullOrWhiteSpace(v) ? "4key" : v;
                KeyViewerOverlay.Rebuild();
                Save();
            },
            "Selected Tab",
            MainCore.Spr.Get(UISprite.Text128),
            "keyviewer_dm_tab"
        );
        selectedTab.InputField.characterLimit = 32;

        GenerateUI.DropDown(
            GenerateUI.Row(dmNoteBody),
            def.DmOutOfLimiterMode,
            Mathf.Clamp(conf.DmOutOfLimiterMode, 0, 2),
            new[] { 0, 1, 2 },
            DmOutOfLimiterName,
            v => {
                conf.DmOutOfLimiterMode = v;
                Save();
            },
            "keyviewer_dm_limiter",
            260f,
            "Out Of Limiter"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(dmNoteBody),
            def.DmNoteEffect,
            conf.DmNoteEffect,
            v => { conf.DmNoteEffect = v; KeyViewerOverlay.Rebuild(); Save(); },
            "Note Rain",
            "keyviewer_dm_note"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(dmNoteBody),
            def.DmNoteReverse,
            conf.DmNoteReverse,
            v => { conf.DmNoteReverse = v; Apply(); Save(); },
            "Reverse Rain",
            "keyviewer_dm_reverse"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(dmNoteBody),
            def.DmShowCounter,
            conf.DmShowCounter,
            v => { conf.DmShowCounter = v; KeyViewerOverlay.Rebuild(); Save(); },
            "Show Counter",
            "keyviewer_dm_counter"
        );

        AddSlider(dmNoteBody, "Scale", "keyviewer_dm_scale",
            def.DmScale, 0.2f, 4f, conf.DmScale, "0.00 x", 0.01f,
            v => { conf.DmScale = v; Apply(); }, Save);

        AddSlider(dmNoteBody, "Offset X", "keyviewer_dm_offsetx",
            def.DmOffsetX, -2000f, 2000f, conf.DmOffsetX, "0 px", 1f,
            v => { conf.DmOffsetX = v; Apply(); }, Save);

        AddSlider(dmNoteBody, "Offset Y", "keyviewer_dm_offsety",
            def.DmOffsetY, -2000f, 2000f, conf.DmOffsetY, "0 px", 1f,
            v => { conf.DmOffsetY = v; Apply(); }, Save);

        AddSlider(dmNoteBody, "Note Speed", "keyviewer_dm_speed",
            def.DmNoteSpeed, 10f, 1000f, conf.DmNoteSpeed, "0 px/s", 1f,
            v => { conf.DmNoteSpeed = v; Apply(); }, Save);

        AddSlider(dmNoteBody, "Track Height", "keyviewer_dm_track",
            def.DmTrackHeight, 0f, 1000f, conf.DmTrackHeight, "0 px", 1f,
            v => { conf.DmTrackHeight = v; KeyViewerOverlay.Rebuild(); }, Save);

        AddSlider(dmNoteBody, "Fade (px)", "keyviewer_dm_fade",
            def.DmFadePx, 0f, 500f, conf.DmFadePx, "0 px", 1f,
            v => { conf.DmFadePx = v; Apply(); }, Save);

        GenerateUI.Toggle(
            GenerateUI.Row(dmNoteBody),
            def.DmDelayedNoteEnabled,
            conf.DmDelayedNoteEnabled,
            v => { conf.DmDelayedNoteEnabled = v; Apply(); Save(); },
            "Delayed Notes",
            "keyviewer_dm_delay_enabled"
        );

        AddSlider(dmNoteBody, "Short Note Threshold", "keyviewer_dm_short_threshold",
            def.DmShortNoteThresholdMs, 0f, 2000f, conf.DmShortNoteThresholdMs, "0 ms", 1f,
            v => { conf.DmShortNoteThresholdMs = v; Apply(); }, Save);

        AddSlider(dmNoteBody, "Short Note Min Length", "keyviewer_dm_short_min",
            def.DmShortNoteMinLengthPx, 1f, 999f, conf.DmShortNoteMinLengthPx, "0 px", 1f,
            v => { conf.DmShortNoteMinLengthPx = v; Apply(); }, Save);

        AddSlider(dmNoteBody, "Key Display Delay", "keyviewer_dm_key_delay",
            def.DmKeyDisplayDelayMs, 0f, 9999f, conf.DmKeyDisplayDelayMs, "0 ms", 1f,
            v => { conf.DmKeyDisplayDelayMs = v; Apply(); }, Save);

        RefreshPresetStatus();
        RefreshMode();
    }

    private static string StyleName(int style) => style switch {
        0 => MainCore.Tr.Get("KEYVIEWER_STYLE_10", "10 Keys"),
        1 => MainCore.Tr.Get("KEYVIEWER_STYLE_12", "12 Keys"),
        3 => MainCore.Tr.Get("KEYVIEWER_STYLE_20", "20 Keys"),
        _ => MainCore.Tr.Get("KEYVIEWER_STYLE_16", "16 Keys"),
    };

    private static string DmOutOfLimiterName(int mode) => mode switch {
        0 => MainCore.Tr.Get("KEYVIEWER_DM_LIMITER_HIDE", "Hide"),
        2 => MainCore.Tr.Get("KEYVIEWER_DM_LIMITER_FULL_PRESS", "Full Press"),
        _ => MainCore.Tr.Get("KEYVIEWER_DM_LIMITER_RAIN_ONLY", "Rain Only"),
    };

    private static UISlider AddSlider(
        Transform body, string label, string id,
        float defVal, float min, float max, float val,
        string format, float step,
        Action<float> setter, Action save
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
        s.OnChanged = v => setter(v);
        s.OnComplete = v => { setter(v); save?.Invoke(); };
        return s;
    }

    private static void AddColor(
        Transform body, string label, string id,
        Color defColor, Color current, Action<Color> setter,
        Action apply, Action save, Action refreshPreview
    ) {
        GenerateUI.ColorPicker(
            GenerateUI.Row(body),
            defColor,
            current,
            c => { setter(c); apply(); refreshPreview(); },
            c => { setter(c); apply(); refreshPreview(); save(); },
            label,
            id
        );
    }

    private static RectTransform AddModeBody(Transform parent, string name) {
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

    private static void AddModeButton(
        Transform row,
        string text,
        string key,
        Action onClick,
        out Image bg,
        out TextMeshProUGUI label
    ) {
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        if(layout == null) {
            layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(0, 250, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
        }

        GameObject obj = new("Mode_" + text.Replace(" ", ""));
        obj.transform.SetParent(row, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minHeight = 50f;
        le.preferredHeight = 50f;

        bg = obj.AddComponent<Image>();
        bg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        bg.type = Image.Type.Sliced;

        label = GenerateUI.AddText(obj.transform, true);
        GenerateUI.Localize(label, key, text);
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 22f;

        GenerateUI.AddButton(obj, btn => {
            if(btn == InputButton.Left) {
                onClick?.Invoke();
            }
        });
    }

    private static void ApplyModeButton(Image bg, TextMeshProUGUI label, bool selected) {
        if(bg != null) {
            bg.color = selected ? UIColors.ObjectActive : UIColors.ObjectBG;
        }
        if(label != null) {
            label.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.6f);
        }
    }

    // Polls for the next key press while the preview is armed. Focusing the
    // label input cancels the capture so typing doesn't rebind the key.
    private sealed class KeyCaptureRunner : MonoBehaviour {
        public Func<bool> IsListening;
        public Func<bool> ShouldCancel;
        public Action<KeyCode> OnCaptured;
        public Action OnCancelled;

        private static readonly KeyCode[] allKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        private void Update() {
            if(IsListening == null || !IsListening()) {
                return;
            }

            if(Input.GetKeyDown(KeyCode.Escape) || (ShouldCancel?.Invoke() ?? false)) {
                OnCancelled?.Invoke();
                return;
            }

            if(!Input.anyKeyDown) {
                return;
            }

            foreach(KeyCode key in allKeys) {
                if(key == KeyCode.None || (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)) {
                    continue;
                }
                if(Input.GetKeyDown(key)) {
                    OnCaptured?.Invoke(key);
                    return;
                }
            }
        }
    }
}
