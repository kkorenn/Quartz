using Quartz.Core;
using Quartz.Features.KeyViewer;
using Quartz.Resource;
using Quartz.UI.Generator;
using Quartz.UI.Objects.Impl;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityEngine.EventSystems.PointerEventData;

using TMPro;

namespace Quartz.UI.Factory.Page;

// Key Viewer settings section for the Overlay tab — v1's "simple" key viewer.
// The Keys group is an interactive preview of the grid: click a key, press
// the new binding (Esc cancels), and edit the selected key's label below.
// Rain and foot keys come with later port slices.
internal static class PageKeyViewer {
    // Display order in the dropdown: ascending by key count (8, 10, 12, 14,
    // 16, 20). The stored Style int stays as-is (8 and 14 are 4 and 5).
    private static readonly int[] styles = [4, 0, 1, 5, 2, 3];

    // Foot styles: 0 = none, then 2/4/6/8/10/12/14/16 keys (FootStyle * 2).
    private static readonly int[] footStyles = [0, 1, 2, 3, 4, 5, 6, 7, 8];

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

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.ShowOutsideGame,
            conf.ShowOutsideGame,
            v => { conf.ShowOutsideGame = v; Save(); },
            "Show Outside Gameplay",
            "keyviewer_showoutside"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_SHOWOUTSIDE",
            "Keep the key viewer visible in menus and outside of gameplay, not just while a level is playing."
        );

        simpleBody = AddModeBody(sec.Body, "SimpleMode");
        dmNoteBody = AddModeBody(sec.Body, "DmNoteMode");

        // Declared ahead: the style dropdown rebuilds the preview, and the
        // per-key editors below refresh their values when the selection changes.
        Action rebuildPreview = null;
        // The per-key popup (rebind / label / ghost / per-key colours & fonts)
        // refreshes its controls for the selected key through this.
        Action refreshPopup = null;

        GenerateUI.DropDown(
            GenerateUI.Row(simpleBody),
            def.Style,
            Mathf.Clamp(conf.Style, 0, KeyViewerSettings.MaxStyle),
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

        GenerateUI.DropDown(
            GenerateUI.Row(simpleBody),
            false,
            conf.StatsTogether,
            new[] { false, true },
            t => MainCore.Tr.Get(
                t ? "KEYVIEWER_STATS_TOGETHER" : "KEYVIEWER_STATS_APART",
                t ? "Together" : "Apart"
            ),
            v => {
                conf.StatsTogether = v;
                KeyViewerOverlay.Rebuild();
                Save();
                rebuildPreview?.Invoke();
            },
            "keyviewer_stats_layout",
            260f,
            "KPS / Total"
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

        // === Foot Keys ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(simpleBody)), "HEADING_FOOT", "Foot Keys");

        GenerateUI.DropDown(
            GenerateUI.Row(simpleBody),
            def.FootStyle,
            Mathf.Clamp(conf.FootStyle, 0, 8),
            footStyles,
            FootStyleName,
            v => {
                conf.FootStyle = v;
                KeyViewerOverlay.Rebuild();
                Save();
                rebuildPreview?.Invoke();
            },
            "keyviewer_footstyle",
            260f,
            "Foot Keys"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_FOOTSTYLE",
            "Add a separate foot-pedal element you can drag on its own in Reorganize mode. The keys light on press but don't count."
        );

        var footHint = GenerateUI.AddText(GenerateUI.Row(simpleBody, 30f));
        GenerateUI.Localize(footHint, "KEYVIEWER_FOOT_HINT", "The foot keys are a separate element — drag them anywhere in Reorganize mode.");
        footHint.fontSize = 17f;
        footHint.color = new Color(1f, 1f, 1f, 0.45f);

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
        bool ghostListening = false;
        var previewBoxes = new Dictionary<int, (Image fill, Image border, TextMeshProUGUI label)>();
        var statBoxes = new List<(Image fill, Image border, TextMeshProUGUI label)>();
        UIInput labelInput = null;

        int Style() => Mathf.Clamp(conf.Style, 0, KeyViewerSettings.MaxStyle);
        bool SlotValid() => selectedSlot >= 0 && selectedSlot < KeyViewerSettings.SlotCount;
        int[] GhostArray() => conf.GhostKeysForStyle(Style());

        void RefreshPreviewVisuals() {
            int style = Mathf.Clamp(conf.Style, 0, KeyViewerSettings.MaxStyle);
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

        // The label-override array for a slot: foot slots (20+) live in
        // FootKeysText, main slots in the style's label array.
        string[] LabelArrayFor(int slot) =>
            slot >= KeyViewerSettings.FootSlotBase ? conf.FootKeysText : conf.LabelsForStyle(Style());

        int LabelIndexFor(int slot) =>
            slot >= KeyViewerSettings.FootSlotBase ? slot - KeyViewerSettings.FootSlotBase : slot;

        // Clicking a key opens its editor popup. It no longer auto-arms a rebind
        // — the popup's Rebind button does that explicitly.
        void SelectSlot(int slot) {
            selectedSlot = slot;
            listening = false;
            ghostListening = false;
            refreshPopup?.Invoke();
            RefreshPreviewVisuals();
        }

        void CancelCapture() {
            listening = false;
            ghostListening = false;
            refreshPopup?.Invoke();
            RefreshPreviewVisuals();
        }

        void OnKeyCaptured(KeyCode key) {
            if(SlotValid()) {
                if(selectedSlot >= KeyViewerSettings.FootSlotBase) {
                    int fi = selectedSlot - KeyViewerSettings.FootSlotBase;
                    if(fi < conf.FootKeys.Length) {
                        conf.FootKeys[fi] = (int)key;
                        Save();
                        KeyViewerOverlay.Rebuild();
                    }
                } else {
                    int[] keys = conf.KeysForStyle(Style());
                    if(selectedSlot < keys.Length) {
                        keys[selectedSlot] = (int)key;
                        Save();
                        KeyViewerOverlay.Rebuild();
                    }
                }
            }
            listening = false;
            refreshPopup?.Invoke();
            RefreshPreviewVisuals();
        }

        void OnGhostCaptured(KeyCode key) {
            int[] ghost = GhostArray();
            if(SlotValid() && selectedSlot < ghost.Length) {
                ghost[selectedSlot] = (int)key;
                Save();
                KeyViewerOverlay.Rebuild();
            }
            ghostListening = false;
            refreshPopup?.Invoke();
        }

        rebuildPreview = () => {
            for(int i = preview.childCount - 1; i >= 0; i--) {
                Object.Destroy(preview.GetChild(i).gameObject);
            }
            previewBoxes.Clear();
            statBoxes.Clear();
            selectedSlot = -1;
            listening = false;
            ghostListening = false;
            refreshPopup?.Invoke();

            int style = Mathf.Clamp(conf.Style, 0, KeyViewerSettings.MaxStyle);
            int footCount = conf.FootKeyCount();
            Vector2 gridSize = KeyViewerOverlay.GridSizeWithFoot(style, footCount);
            preview.sizeDelta = gridSize;
            // Grow the preview row so foot rows don't overlap the controls below.
            LayoutElement previewLe = previewRow.GetComponent<LayoutElement>();
            if(previewLe != null) {
                float h = Mathf.Max(175f, gridSize.y + 24f);
                previewLe.minHeight = h;
                previewLe.preferredHeight = h;
            }

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

            // Foot keys: clickable like the main keys (slots 20+), so they
            // rebind, relabel and take per-key colours through the same editors.
            // In the live overlay they're a separate draggable element; the
            // preview just stacks them under the main grid for convenience.
            if(footCount > 0) {
                List<KeyViewerOverlay.KeySlot> footSlots = [];
                Vector2 footSize = KeyViewerOverlay.BuildFootLayout(footCount, footSlots);
                float footShiftX = (gridSize.x - footSize.x) * 0.5f;
                float footShiftY = gridSize.y - footSize.y;
                foreach(KeyViewerOverlay.KeySlot slot in footSlots) {
                    (Image fill, Image border) = KeyViewerOverlay.NewBoxVisual(
                        "PreviewFoot_" + slot.Slot, preview, slot.X + footShiftX, slot.Y + footShiftY, slot.W, slot.H
                    );
                    fill.raycastTarget = true;

                    TextMeshProUGUI label = KeyViewerOverlay.NewText(
                        fill.transform, "Label", "", 13f
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

        // One capture runner serves both the Rebind and Set-Ghost-Key buttons;
        // whichever armed the listen decides where the next key press lands.
        KeyCaptureRunner runner = previewObj.AddComponent<KeyCaptureRunner>();
        runner.IsListening = () => listening || ghostListening;
        // A focused input field (the key label, slider value editors) means the
        // user is typing, not binding a key.
        runner.ShouldCancel = () => {
            if(labelInput != null && labelInput.InputField.isFocused) {
                return true;
            }
            GameObject sel = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            TMP_InputField field = sel != null ? sel.GetComponent<TMP_InputField>() : null;
            return field != null && field.isFocused;
        };
        runner.OnCaptured = key => {
            if(ghostListening) {
                OnGhostCaptured(key);
            } else {
                OnKeyCaptured(key);
            }
        };
        runner.OnCancelled = CancelCapture;

        float SnapFont(float v) => Mathf.Clamp(Mathf.Round(v / 0.05f) * 0.05f, 0.1f, 3f);

        // === Per-key popup ===
        // Appears when a key in the preview is clicked; edits only that key
        // (rebind, label, per-key colours/fonts, ghost key). Hidden otherwise.
        RectTransform popup = MakeCard(simpleBody, "KeyPopup");
        popup.gameObject.SetActive(false);

        TextMeshProUGUI popupTitle = GenerateUI.AddText(GenerateUI.Row(popup, 36f));
        popupTitle.fontSize = 24f;
        popupTitle.alignment = TextAlignmentOptions.MidlineLeft;

        GenerateUI.Button(
            GenerateUI.Row(popup),
            () => { if(SlotValid()) { listening = true; ghostListening = false; refreshPopup?.Invoke(); RefreshPreviewVisuals(); } },
            "Rebind Key", "keyviewer_pk_rebind"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_PK_REBIND",
            "Click, then press the new key for this slot. Esc cancels."
        );

        labelInput = GenerateUI.Input(
            GenerateUI.Row(popup),
            "",
            "",
            v => {
                string[] overrides = LabelArrayFor(selectedSlot);
                int idx = LabelIndexFor(selectedSlot);
                if(idx >= 0 && idx < overrides.Length) {
                    overrides[idx] = v ?? "";
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

        // Colors: a collapsible whose header dot IS the per-key-colours enable.
        // Body holds this key's colour pickers plus its ghost (secondary) key.
        var colorsSec = GenerateUI.Collapsible(
            popup, "Colors", startExpanded: false,
            v => {
                conf.PerKeyColors = v;
                if(v && !conf.PerKeyColorsInitialized) {
                    conf.SeedPerKeyColorsFromGlobal();
                    conf.PerKeyColorsInitialized = true;
                }
                Apply();
                Save();
                refreshPopup?.Invoke();
            },
            conf.PerKeyColors
        );

        UIColorPicker PerKeyColor(string label, string id, Color[] arr, Func<Color> fallback) =>
            GenerateUI.ColorPicker(
                GenerateUI.Row(colorsSec.Body),
                fallback(), fallback(),
                c => { if(SlotValid()) { arr[selectedSlot] = c; Apply(); } },
                c => { if(SlotValid()) { arr[selectedSlot] = c; Apply(); Save(); } },
                label, id
            );

        UIColorPicker pkBg = PerKeyColor("Background", "keyviewer_pk_bg", conf.PerKeyBg, conf.GetBg);
        UIColorPicker pkBgPressed = PerKeyColor("Background Pressed", "keyviewer_pk_bgpressed", conf.PerKeyBgPressed, conf.GetBgPressed);
        UIColorPicker pkOutline = PerKeyColor("Outline", "keyviewer_pk_outline", conf.PerKeyOutline, conf.GetOutline);
        UIColorPicker pkOutlinePressed = PerKeyColor("Outline Pressed", "keyviewer_pk_outlinepressed", conf.PerKeyOutlinePressed, conf.GetOutlinePressed);
        UIColorPicker pkText = PerKeyColor("Text", "keyviewer_pk_text", conf.PerKeyText, conf.GetText);
        UIColorPicker pkTextPressed = PerKeyColor("Text Pressed", "keyviewer_pk_textpressed", conf.PerKeyTextPressed, conf.GetTextPressed);
        UIColorPicker pkRain = PerKeyColor("Rain", "keyviewer_pk_rain", conf.PerKeyRain, conf.GetRain);

        // Fonts: a collapsible whose header dot IS the per-key font-size enable.
        var fontsSec = GenerateUI.Collapsible(
            popup, "Fonts", startExpanded: false,
            v => {
                conf.PerKeyFontSizes = v;
                if(v && !conf.PerKeyFontInitialized) {
                    conf.SeedPerKeyFontFromGlobal();
                    conf.PerKeyFontInitialized = true;
                }
                KeyViewerOverlay.Rebuild();
                Save();
                refreshPopup?.Invoke();
            },
            conf.PerKeyFontSizes
        );

        UISlider pkKeyFont = GenerateUI.Slider(
            GenerateUI.Row(fontsSec.Body), 1f, 0.1f, 3f, 1f, SnapFont, null, null,
            "Key Font", "keyviewer_pk_keyfont"
        );
        pkKeyFont.Format = "0.00 x";
        pkKeyFont.OnChanged = v => { if(SlotValid()) { conf.PerKeyKeyFont[selectedSlot] = v; } };
        pkKeyFont.OnComplete = v => { if(SlotValid()) { conf.PerKeyKeyFont[selectedSlot] = v; KeyViewerOverlay.Rebuild(); Save(); } };

        UISlider pkCounterFont = GenerateUI.Slider(
            GenerateUI.Row(fontsSec.Body), 1f, 0.1f, 3f, 1f, SnapFont, null, null,
            "Counter Font", "keyviewer_pk_counterfont"
        );
        pkCounterFont.Format = "0.00 x";
        pkCounterFont.OnChanged = v => { if(SlotValid()) { conf.PerKeyCounterFont[selectedSlot] = v; } };
        pkCounterFont.OnComplete = v => { if(SlotValid()) { conf.PerKeyCounterFont[selectedSlot] = v; KeyViewerOverlay.Rebuild(); Save(); } };

        // Ghost Key: an optional secondary key for this slot that emits its own
        // ghost-coloured rain streak without touching the press counters. Its
        // own section (not under Colors) since it's a binding, not a colour;
        // active whenever a ghost key is set, so the header carries no toggle.
        var ghostSec = GenerateUI.Collapsible(popup, "Ghost Key", startExpanded: false);

        TextMeshProUGUI ghostKeyLabel = GenerateUI.AddText(GenerateUI.Row(ghostSec.Body, 30f));
        ghostKeyLabel.fontSize = 17f;
        ghostKeyLabel.color = new Color(1f, 1f, 1f, 0.6f);

        GenerateUI.Button(
            GenerateUI.Row(ghostSec.Body),
            () => { if(SlotValid()) { ghostListening = true; listening = false; refreshPopup?.Invoke(); } },
            "Set Ghost Key", "keyviewer_ghostset"
        ).SetNeutral().Rect.AddToolTip(
            "DESC_KEYVIEWER_GHOSTSET",
            "Click, then press the secondary (ghost) key for this slot. Esc cancels."
        );

        GenerateUI.Button(
            GenerateUI.Row(ghostSec.Body),
            () => {
                int[] ghost = GhostArray();
                if(SlotValid() && selectedSlot < ghost.Length) {
                    ghost[selectedSlot] = 0;
                    Save();
                    KeyViewerOverlay.Rebuild();
                }
                ghostListening = false;
                refreshPopup?.Invoke();
            },
            "Clear Ghost Key", "keyviewer_ghostclear"
        ).SetDanger();

        GenerateUI.Button(
            GenerateUI.Row(popup),
            () => {
                conf.SeedPerKeyColorsFromGlobal();
                conf.SeedPerKeyFontFromGlobal();
                conf.PerKeyColorsInitialized = true;
                conf.PerKeyFontInitialized = true;
                KeyViewerOverlay.Rebuild();
                Save();
                refreshPopup?.Invoke();
            },
            "Copy Global to All Keys", "keyviewer_pk_copyglobal"
        ).SetNeutral().Rect.AddToolTip(
            "DESC_KEYVIEWER_PK_COPYGLOBAL",
            "Overwrite every key's per-key colours and font sizes with the current shared values."
        );

        GenerateUI.Button(
            GenerateUI.Row(popup),
            () => {
                selectedSlot = -1;
                listening = false;
                ghostListening = false;
                popup.gameObject.SetActive(false);
                RefreshPreviewVisuals();
            },
            "Close", "keyviewer_pk_close"
        ).SetDanger();

        refreshPopup = () => {
            bool valid = SlotValid();
            if(popup.gameObject.activeSelf != valid) {
                popup.gameObject.SetActive(valid);
            }
            if(!valid) {
                return;
            }

            int style = Style();
            if(listening) {
                popupTitle.text = MainCore.Tr.Get("KEYVIEWER_PK_LISTENING", "Press a key... (Esc cancels)");
            } else if(ghostListening) {
                popupTitle.text = MainCore.Tr.Get("KEYVIEWER_GHOST_LISTENING", "Press the ghost key... (Esc cancels)");
            } else {
                popupTitle.text = string.Format(
                    MainCore.Tr.Get("KEYVIEWER_PK_TITLE", "Editing key: {0}"),
                    KeyViewerOverlay.LabelFor(style, selectedSlot));
            }

            string[] overrides = LabelArrayFor(selectedSlot);
            int idx = LabelIndexFor(selectedSlot);
            labelInput.Set(idx >= 0 && idx < overrides.Length ? overrides[idx] ?? "" : "", invoke: false);

            int s = selectedSlot;
            pkKeyFont.Set(conf.PerKeyKeyFont[s], invoke: false);
            pkCounterFont.Set(conf.PerKeyCounterFont[s], invoke: false);
            pkBg.Set(conf.PerKeyBg[s], invoke: false);
            pkBgPressed.Set(conf.PerKeyBgPressed[s], invoke: false);
            pkOutline.Set(conf.PerKeyOutline[s], invoke: false);
            pkOutlinePressed.Set(conf.PerKeyOutlinePressed[s], invoke: false);
            pkText.Set(conf.PerKeyText[s], invoke: false);
            pkTextPressed.Set(conf.PerKeyTextPressed[s], invoke: false);
            pkRain.Set(conf.PerKeyRain[s], invoke: false);

            int[] ghost = GhostArray();
            int code = s < ghost.Length ? ghost[s] : 0;
            ghostKeyLabel.text = string.Format(
                MainCore.Tr.Get("KEYVIEWER_GHOST_CURRENT", "Ghost key: {0}"),
                code == 0
                    ? MainCore.Tr.Get("KEYVIEWER_GHOST_UNSET", "none")
                    : KeyViewerOverlay.KeyCodeShortLabel((KeyCode)code));
        };

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

        // === Fonts ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(simpleBody)), "HEADING_FONTS", "Fonts");

        UISlider keyFont = GenerateUI.Slider(
            GenerateUI.Row(simpleBody),
            def.KeyFontScale, 0.1f, 3f, conf.KeyFontScale,
            SnapFont, null, null, "Key Font Size", "keyviewer_keyfont"
        );
        keyFont.Format = "0.00 x";
        keyFont.OnComplete = v => { conf.KeyFontScale = v; KeyViewerOverlay.Rebuild(); Save(); };

        UISlider counterFont = GenerateUI.Slider(
            GenerateUI.Row(simpleBody),
            def.CounterFontScale, 0.1f, 3f, conf.CounterFontScale,
            SnapFont, null, null, "Counter Font Size", "keyviewer_counterfont"
        );
        counterFont.Format = "0.00 x";
        counterFont.OnComplete = v => { conf.CounterFontScale = v; KeyViewerOverlay.Rebuild(); Save(); };

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

        // Rain colours (front / back / third row) and the ghost-rain colour live
        // in the Colors category alongside the box colours.
        AddColor(simpleBody, "Rain Color (Front Row)", "keyviewer_raincolor",
            def.GetRain(), conf.GetRain(), conf.SetRain, Apply, Save, RefreshPreviewVisuals);
        AddColor(simpleBody, "Rain 2 Color (Back Row)", "keyviewer_rain2color",
            def.GetRain2(), conf.GetRain2(), conf.SetRain2, Apply, Save, RefreshPreviewVisuals);
        AddColor(simpleBody, "Rain 3 Color (Third Row)", "keyviewer_rain3color",
            def.GetRain3(), conf.GetRain3(), conf.SetRain3, Apply, Save, RefreshPreviewVisuals);
        AddColor(simpleBody, "Ghost Rain Color", "keyviewer_ghostcolor",
            def.GetGhostRain(), conf.GetGhostRain(), conf.SetGhostRain, Apply, Save, RefreshPreviewVisuals);

        // === Counters ===
        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(simpleBody)), "HEADING_COUNTERS", "Counters");

        GenerateUI.Toggle(
            GenerateUI.Row(simpleBody),
            def.CountFormatting,
            conf.CountFormatting,
            v => { conf.CountFormatting = v; KeyViewerOverlay.Rebuild(); Save(); },
            "Count Formatting",
            "keyviewer_countformat"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_COUNTFORMAT",
            "Show counters with a thousands separator (1,234)."
        );

        GenerateUI.Toggle(
            GenerateUI.Row(simpleBody),
            def.PerKeyKps,
            conf.PerKeyKps,
            v => { conf.PerKeyKps = v; KeyViewerOverlay.Rebuild(); Save(); },
            "Per-Key KPS",
            "keyviewer_perkeykps"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_PERKEYKPS",
            "Each key box shows that key's presses-per-second instead of its total press count."
        );

        GenerateUI.Toggle(
            GenerateUI.Row(simpleBody),
            def.HideMainKeyCount,
            conf.HideMainKeyCount,
            v => { conf.HideMainKeyCount = v; KeyViewerOverlay.Rebuild(); Save(); },
            "Hide Main Key Counts",
            "keyviewer_hidemaincount"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_HIDEMAINCOUNT",
            "Hide the per-key counters on the key boxes."
        );

        GenerateUI.Toggle(
            GenerateUI.Row(simpleBody),
            def.StreamerMode,
            conf.StreamerMode,
            v => { conf.StreamerMode = v; Apply(); Save(); },
            "Streamer Mode",
            "keyviewer_streamer"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_STREAMER",
            "Hide the KPS and Total stat boxes entirely."
        );

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

        // --- Custom CSS: an optional stylesheet layered over the preset's
        // per-key styling (colors, border, radius, font, glow, gradients). ---
        var cssStatus = GenerateUI.AddText(GenerateUI.Row(dmNoteBody, 30f));
        cssStatus.fontSize = 17f;
        cssStatus.color = new Color(1f, 1f, 1f, 0.45f);
        void RefreshCssStatus() {
            cssStatus.text = string.IsNullOrWhiteSpace(conf.DmCssText)
                ? MainCore.Tr.Get("KEYVIEWER_DM_CSS_NONE", "No custom CSS")
                : string.Format(MainCore.Tr.Get("KEYVIEWER_DM_CSS_LOADED", "Custom CSS: {0} chars"), conf.DmCssText.Length);
        }

        GenerateUI.Toggle(
            GenerateUI.Row(dmNoteBody),
            def.DmCssEnabled,
            conf.DmCssEnabled,
            v => { conf.DmCssEnabled = v; KeyViewerOverlay.Rebuild(); Save(); },
            "Custom CSS",
            "keyviewer_dm_css_enabled"
        );

        GenerateUI.Button(
            GenerateUI.Row(dmNoteBody),
            () => {
                if(KeyViewerOverlay.ImportDmNoteCss(out string error)) {
                    RefreshCssStatus();
                } else if(!string.IsNullOrEmpty(error)) {
                    cssStatus.text = error;
                }
            },
            "Import Custom CSS",
            "keyviewer_dm_css_import"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_DM_CSS_IMPORT",
            "Select a DM Note custom CSS file. Layers over the preset; restyles keys and counters."
        );

        GenerateUI.Button(
            GenerateUI.Row(dmNoteBody),
            () => {
                conf.DmCssText = "";
                conf.DmCssPath = "";
                KeyViewerOverlay.Rebuild();
                Save();
                RefreshCssStatus();
            },
            "Clear CSS",
            "keyviewer_dm_css_clear"
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
            def.DmShortNoteMinLengthPx, 1f, 9999f, conf.DmShortNoteMinLengthPx, "0 px", 1f,
            v => { conf.DmShortNoteMinLengthPx = v; Apply(); }, Save);

        AddSlider(dmNoteBody, "Key Display Delay", "keyviewer_dm_key_delay",
            def.DmKeyDisplayDelayMs, 0f, 9999f, conf.DmKeyDisplayDelayMs, "0 ms", 1f,
            v => { conf.DmKeyDisplayDelayMs = v; Apply(); }, Save);

        RefreshPresetStatus();
        RefreshCssStatus();
        RefreshMode();
    }

    private static string FootStyleName(int s) => s <= 0
        ? MainCore.Tr.Get("KEYVIEWER_FOOT_NONE", "None")
        : string.Format(MainCore.Tr.Get("KEYVIEWER_FOOT_COUNT", "{0} Keys"), s * 2);

    private static string StyleName(int style) => style switch {
        0 => MainCore.Tr.Get("KEYVIEWER_STYLE_10", "10 Keys"),
        1 => MainCore.Tr.Get("KEYVIEWER_STYLE_12", "12 Keys"),
        3 => MainCore.Tr.Get("KEYVIEWER_STYLE_20", "20 Keys"),
        4 => MainCore.Tr.Get("KEYVIEWER_STYLE_8", "8 Keys"),
        5 => MainCore.Tr.Get("KEYVIEWER_STYLE_14", "14 Keys"),
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

    // A self-sizing card with a background, used for the per-key popup. Reads as
    // a distinct panel that collapses to nothing when hidden (SetActive false on
    // the returned panel). A full-width holder with right padding makes the
    // visible panel narrower than the row — it sits slightly in from the right.
    private static RectTransform MakeCard(Transform parent, string name) {
        GameObject holder = new(name + "Holder");
        holder.transform.SetParent(parent, false);
        holder.AddComponent<RectTransform>();

        // Right padding of 250 matches the standard rows' BackGround() inset, so
        // the card's right edge lines up with the toggles/sliders below it.
        VerticalLayoutGroup holderLayout = holder.AddComponent<VerticalLayoutGroup>();
        holderLayout.padding = new RectOffset(0, 250, 0, 0);
        holderLayout.childControlWidth = true;
        holderLayout.childControlHeight = true;
        holderLayout.childForceExpandWidth = true;
        holderLayout.childForceExpandHeight = false;

        ContentSizeFitter holderFitter = holder.AddComponent<ContentSizeFitter>();
        holderFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject obj = new(name);
        obj.transform.SetParent(holder.transform, false);
        RectTransform rect = obj.AddComponent<RectTransform>();

        Image bg = obj.AddComponent<Image>();
        bg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        bg.type = Image.Type.Sliced;
        bg.color = UIColors.ObjectBG;

        VerticalLayoutGroup layout = obj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.padding = new RectOffset(16, 16, 16, 16);
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

        // Previous-frame hook-held state for the keys Unity's legacy Input is
        // blind to (Korean Hangul / Hanja, reported as Right Alt / Right
        // Control). Tracked every frame — even while idle — so arming a listen
        // never reads a stale rising edge from a key already held.
        private bool prevHookRAlt;
        private bool prevHookRCtrl;

        private void Update() {
            bool hookRAlt = Features.KeyLimiter.KeyLimiter.HookKeyHeld(KeyCode.RightAlt);
            bool hookRCtrl = Features.KeyLimiter.KeyLimiter.HookKeyHeld(KeyCode.RightControl);
            bool rAltEdge = hookRAlt && !prevHookRAlt;
            bool rCtrlEdge = hookRCtrl && !prevHookRCtrl;
            prevHookRAlt = hookRAlt;
            prevHookRCtrl = hookRCtrl;

            if(IsListening == null || !IsListening()) {
                return;
            }

            if(Input.GetKeyDown(KeyCode.Escape) || (ShouldCancel?.Invoke() ?? false)) {
                OnCancelled?.Invoke();
                return;
            }

            // Hook fallback first: Hangul/Hanja (Right Alt / Right Control) never
            // reach Unity's Input, so Input.anyKeyDown stays false and the loop
            // below would never see them. The SkyHook-fed held state is the only
            // path that does. A normal keyboard's Right Alt / Right Control still
            // lands on the Unity path below (it fires GetKeyDown), so this only
            // matters for the keys Unity genuinely can't report.
            if(rCtrlEdge) {
                OnCaptured?.Invoke(KeyCode.RightControl);
                return;
            }
            if(rAltEdge) {
                OnCaptured?.Invoke(KeyCode.RightAlt);
                return;
            }

            if(!Input.anyKeyDown) {
                return;
            }

            // Numpad Enter and Return can land on the same frame on some
            // keyboards; the loop below would bind Return first (lower value),
            // so capture the distinct numpad code when it's down.
            if(Input.GetKeyDown(KeyCode.KeypadEnter)) {
                OnCaptured?.Invoke(KeyCode.KeypadEnter);
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
