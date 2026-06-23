using GTweens.Builders;
using GTweens.Easings;
using GTweens.Extensions;
using GTweens.Tweens;
using Quartz.Async;
using Quartz.Core;
using Quartz.Features.GameOverlayFont;
using Quartz.IO;
using Quartz.Localization;
using Quartz.Resource;
using Quartz.Tween;
using Quartz.UI.Generator;
using Quartz.UI.Objects.Impl;
using Quartz.UI.Utility;
using Quartz.Utility;
using Quartz.Update;
using UnityEngine;
using UnityEngine.UI;
using UnityFileDialog;
using GTweenExtensions = GTweens.Extensions.GTweenExtensions;

using TMPro;

namespace Quartz.UI.Factory.Page;

internal static class PageSettings {
    private static readonly Dictionary<TextLocalization, (GameObject LabelRow, GameObject MainRow)> objects = [];
    private static UIDropDown<string> languageDropdown;

    // Font picker + custom-font management.
    private static UIDropDown<string> fontDropdown;
    private static GameObject fontManageRow;
    private static UIInput fontRenameInput;
    private static UIButton fontDeleteBtn;
    private static Func<Color> fontDeleteRestColor;
    private static bool fontDeleteArmed;
    private static GameObject fontStatusRow;
    private static TextMeshProUGUI fontStatusText;
    private static UIDropDown<string> gameFontDropdown;
    private static GameObject gameFontPickerRow;
    private static UIDropDown<string> settingsFontDropdown;
    private static string pendingFontName = "";

    // Update UI, refreshed from UpdateService.OnChanged.
    private static UIButton updateCheckButton;
    private static TextMeshProUGUI updateStatusText;
    private static GameObject updateActionRow;
    private static GameObject updateButtonRow;
    private static TextMeshProUGUI updateVersionText;
    private static UIButton updateNotesButton;
    private static UIButton updateSkipButton;
    private static UIButton updateInstallButton;
    private static UIButton updateUndoButton;
    private static GameObject updateProgressRow;
    private static RectTransform updateProgressFill;
    private static TextMeshProUGUI updateProgressLabel;
    private static bool updateHooked;

    // For jumping to the Updates section from the update toast.
    private static UIScrollController scrollController;
    private static RectTransform pageContent;
    private static RectTransform updatesAnchor;

    public static void Create(RectTransform parent) {
        // The page can be built more than once per session (profile switches
        // rebuild the whole UI); drop rows from the previous build.
        objects.Clear();
        FontManager.OnFontCatalogChanged -= RefreshFontDropdowns;
        FontManager.OnFontCatalogChanged += RefreshFontDropdowns;

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

        scrollController = pad.AddComponent<UIScrollController>();
        scrollController.SetContent(contentRect, viewportRect);
        pageContent = contentRect;

        CoreSettings defSet = new();

        var inputRow = GenerateUI.Row(content.transform);
        var findInput =
        GenerateUI.Input(
            inputRow,
            null,
            null,
            value => {
                bool isBlank = string.IsNullOrWhiteSpace(value);
                Dictionary<GameObject, bool> labelActivationMap = [];

                foreach(var pair in objects.Where(pair => pair.Value.LabelRow != null)) {
                    labelActivationMap[pair.Value.LabelRow] = isBlank;
                }

                string normalizedQuery = StringUtils.Normalize(value);

                if(MainCore.Conf.Language == "ko-KR") {
                    normalizedQuery = StringUtils.NormalizeToHangulChosung(normalizedQuery);
                }

                foreach(var (labelLoc, valueTuple) in objects) {
                    var (labelRow, mainRow) = valueTuple;

                    if(labelRow == null || mainRow == null) {
                        continue;
                    }

                    string normalizedTarget = labelLoc != null ? StringUtils.Normalize(labelLoc.Value) : string.Empty;
                    if(MainCore.Conf.Language == "ko-KR" && !string.IsNullOrEmpty(normalizedTarget)) {
                        normalizedTarget = StringUtils.NormalizeToHangulChosung(normalizedTarget);
                    }

                    bool isMainMatch = isBlank
                        || (
                            !string.IsNullOrEmpty(normalizedTarget)
                            && normalizedTarget.Contains(normalizedQuery)
                        );

                    mainRow.SetActive(isMainMatch);

                    if(isMainMatch) {
                        labelActivationMap[labelRow] = true;
                    }
                }

                foreach(var kvp in labelActivationMap) {
                    kvp.Key.SetActive(kvp.Value);
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            },
            "Find",
            MainCore.Spr.Get(UISprite.MagnifyingGlass128),
            "search_find"
        );
        findInput.Placeholder.gameObject.AddComponent<TextLocalization>().Init("FIND", "Find");
        findInput.InputField.characterLimit = 22;

        var langLabelRow = GenerateUI.Row(content.transform);
        var langText = GenerateUI.AddTextH1(langLabelRow);
        var langTextTr = langText.gameObject.AddComponent<TextLocalization>().Init("LANGUAGE", "Language");

        string[] langs = [.. MainCore.Tr.GetLanguages().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
        var langRow = GenerateUI.Row(content.transform);
        languageDropdown = GenerateUI.DropDown(
            langRow,
            null,
            MainCore.Tr.Language,
            langs,
            lang => {
                if(lang == Translator.FALLBACK_LANGUAGE) {
                    return "DEFAULT";
                }

                string native = MainCore.Tr.GetForLanguage(
                    "0NATIVELANG",
                    lang,
                    lang
                );

                return $"{native} ({lang})";
            },
            value => {
                MainCore.Tr.Language = value;
                MainCore.Conf.Language = value;
                MainCore.ConfMgr.RequestSave();
                TextLocalization.RefreshAll();
            },
            "language_dropdown"
        );

        UIButton langBtn = GenerateUI.Button(
            langRow,
            () => { },
            "Reload",
            "language_reload"
        );
        langBtn.OnClick = async () => {
            languageDropdown.SetExpanded(false);
            languageDropdown.SetBlocked(true);
            langBtn.SetBlocked(true);
            langBtn.Label.text = "...";
            _ = Task.Run(async () => {
                await MainCore.Tr.Load(MainCore.Paths.LangPath);
                MainThread.Enqueue(() => {
                    languageDropdown.SetBlocked(false);
                    langBtn.SetBlocked(false);
                    TextLocalization.RefreshAll();
                    RefreshUpdates();
                });
            });
        };
        {
            var br = langBtn.Rect;
            br.pivot = new(1f, 1f);
            br.anchorMin = new(1f, 1f);
            br.anchorMax = new(1f, 1f);
            br.sizeDelta = new(114f, 50f);
            br.offsetMax = Vector2.zero;
        }
        langBtn.Label.gameObject.AddComponent<TextLocalization>().Init("RELOAD", "Reload");

        objects[langTextTr] = (langLabelRow.gameObject, langRow.gameObject);

        var overlayerText = GenerateUI.AddTextH1(GenerateUI.Row(content.transform));
        var overlayerTextTr = overlayerText.gameObject.AddComponent<TextLocalization>().Init("OVERLAYER", "Quartz");

        var startupRow = GenerateUI.Row(content.transform);
        UIToggle startupToggle = GenerateUI.Toggle(
            startupRow,
            defSet.ShowOnStartup,
            MainCore.Conf.ShowOnStartup,
            toggle => {
                MainCore.Conf.ShowOnStartup = toggle;
                MainCore.ConfMgr.RequestSave();
            },
            "Show Quartz Settings at Startup",
            "show_on_startup"
        );
        var startupToggleTr = startupToggle.Label.gameObject.AddComponent<TextLocalization>().Init("SHOW_OVERLAYER_PANEL_AT_STARTUP", "Show Quartz Settings at Startup");
        objects[startupToggleTr] = (overlayerText.gameObject, startupRow.gameObject);

        var keybindRow = GenerateUI.Row(content.transform);
        var keybindLabel = GenerateUI.KeyBind(
            keybindRow,
            (Keybind.KeyModifier)MainCore.Conf.ToggleModifier,
            (KeyCode)MainCore.Conf.ToggleKey,
            (mod, key) => {
                MainCore.Conf.ToggleModifier = (int)mod;
                MainCore.Conf.ToggleKey = (int)key;
                MainCore.ConfMgr.RequestSave();
            },
            "Toggle Menu Keybind",
            "toggle_keybind"
        );
        var keybindTr = keybindLabel.gameObject.AddComponent<TextLocalization>().Init("TOGGLE_KEYBIND", "Toggle Menu Keybind");
        objects[keybindTr] = (overlayerText.gameObject, keybindRow.gameObject);

        var tooltipRow = GenerateUI.Row(content.transform);
        UIToggle tooltipToggle = GenerateUI.Toggle(
            tooltipRow,
            defSet.Tooltip,
            MainCore.Conf.Tooltip,
            toggle => {
                Tooltip.Hide();
                MainCore.Conf.Tooltip = toggle;
                MainCore.ConfMgr.RequestSave();
            },
            "Show Tooltip",
            "show_tooltip"
        );
        tooltipToggle.Rect.AddToolTip(
            "DESC_SHOW_TOOLTIP",
            "This is a Tooltip!"
        );
        var tooltipToggleTr = tooltipToggle.Label.gameObject.AddComponent<TextLocalization>().Init("SHOW_TOOLTIP", "Show Tooltip");
        objects[tooltipToggleTr] = (overlayerText.gameObject, tooltipRow.gameObject);

        var middleClickRow = GenerateUI.Row(content.transform);
        UIToggle middleClickToggle = GenerateUI.Toggle(
            middleClickRow,
            defSet.MiddleClickToDefault,
            MainCore.Conf.MiddleClickToDefault,
            toggle => {
                MainCore.Conf.MiddleClickToDefault = toggle;
                MainCore.ConfMgr.RequestSave();
            },
            "Middle-click to set as default",
            "middle_click_default"
        );
        middleClickToggle.Rect.AddToolTip(
            "DESC_MIDDLE_CLICK_TO_SET_AS_DEFAULT",
            "Setting that restores an item to its default value when you middle-click on it.\nYou can identify it by a small dot at the top-left of the item"
        );
        var middleClickToggleTr = middleClickToggle.Label.gameObject.AddComponent<TextLocalization>().Init("MIDDLE_CLICK_TO_SET_AS_DEFAULT", "Middle-click to set as default");
        objects[middleClickToggleTr] = (overlayerText.gameObject, middleClickRow.gameObject);

        static float uiScaleFilter(float v) {
            v = Mathf.Round(v * 100f) / 100f;
            return Mathf.Clamp(v, 0.8f, 1.6f);
        }
        var uiScaleRow = GenerateUI.Row(content.transform);
        UISlider uiScale = GenerateUI.Slider(
            uiScaleRow,
            1f,
            0.8f,
            1.6f,
            MainCore.Conf.UIScale,
            uiScaleFilter,
            null,
            null,
            "UI Scale",
            "ui_scale"
        );
        uiScale.Format = "0.00x";
        uiScale.OnChanged = value => MainCore.Conf.UIScale = value;
        GTween scaleSeq = null;
        uiScale.OnComplete = value => {
            MainCore.Conf.UIScale = value;
            MainCore.ConfMgr.RequestSave();

            scaleSeq?.Kill();

            float scaleStart = UICore.PanelScale;
            Vector2 targetSize = UICore.DefaultPanelSize;
            UICore.LastPanelSize = targetSize;

            scaleSeq = GTweenSequenceBuilder.New()
                .Append(
                    GTweenExtensions.Tween(
                        () => scaleStart,
                        x => UICore.PanelScale = x,
                        value,
                        0.4f
                    ).SetEasing(Easing.OutExpo)
                )
                .Join(
                    UICore.Panel.GTSizeDelta(targetSize, 0.4f)
                        .SetEasing(Easing.OutExpo)
                )
                .Build();

            MainCore.TC.Play(scaleSeq);
        };
        var uiScaleTr = uiScale.Label.gameObject.AddComponent<TextLocalization>().Init("UI_SCALE", "UI Scale");

        objects[uiScaleTr] = (overlayerText.gameObject, uiScaleRow.gameObject);

        var scrollRow = GenerateUI.Row(content.transform);
        UISlider scrollSpeed = GenerateUI.Slider(
            scrollRow,
            80f,
            20f,
            300f,
            MainCore.Conf.ScrollSpeed,
            Mathf.Round,
            v => MainCore.Conf.ScrollSpeed = v,
            v => { MainCore.Conf.ScrollSpeed = v; MainCore.ConfMgr.RequestSave(); },
            "Scroll Speed",
            "scroll_speed"
        );
        scrollSpeed.Format = "0 px";
        var scrollTr = scrollSpeed.Label.gameObject.AddComponent<TextLocalization>().Init("SCROLL_SPEED", "Scroll Speed");
        objects[scrollTr] = (overlayerText.gameObject, scrollRow.gameObject);

        var opacityRow = GenerateUI.Row(content.transform);
        UISlider opacity = GenerateUI.Slider(
            opacityRow,
            100f,
            20f,
            100f,
            MainCore.Conf.PanelOpacity * 100f,
            Mathf.Round,
            v => UICore.SetPanelOpacity(v / 100f, false),
            v => UICore.SetPanelOpacity(v / 100f, true),
            "Window Opacity",
            "window_opacity"
        );
        // Literal % (a bare "0%" is .NET's percent specifier and multiplies by 100).
        opacity.Format = "0'%'";
        opacity.Rect.AddToolTip(
            "DESC_WINDOW_OPACITY",
            "Transparency of the settings window."
        );
        var opacityTr = opacity.Label.gameObject.AddComponent<TextLocalization>().Init("WINDOW_OPACITY", "Window Opacity");
        objects[opacityTr] = (overlayerText.gameObject, opacityRow.gameObject);

        var accentRow = GenerateUI.Row(content.transform);
        UIColorPicker accentPicker = GenerateUI.ColorPicker(
            accentRow,
            new Color(1f, 0.6f, 0.6f, 1f),
            MainCore.Conf.GetAccentColor(),
            c => UICore.SetAccentColor(c, false),
            c => UICore.SetAccentColor(c, true),
            "Accent Color",
            "accent_color",
            false
        );
        accentPicker.Rect.AddToolTip(
            "DESC_ACCENT_COLOR",
            "Recolors the whole Quartz UI. Middle-click to reset."
        );
        var accentTr = accentPicker.Label.gameObject.AddComponent<TextLocalization>().Init("ACCENT_COLOR", "Accent Color");
        objects[accentTr] = (overlayerText.gameObject, accentRow.gameObject);

        var updatesLabelRow = GenerateUI.Row(content.transform);
        var updatesText = GenerateUI.AddTextH1(updatesLabelRow);
        var updatesTextTr = updatesText.gameObject.AddComponent<TextLocalization>().Init("UPDATES", "Updates");
        updatesAnchor = updatesLabelRow;

        ReleaseChannel[] channels = [
            ReleaseChannel.Stable,
            ReleaseChannel.ReleaseCandidate,
            ReleaseChannel.Beta,
            ReleaseChannel.Alpha,
        ];
        var channelRow = GenerateUI.Row(content.transform);
        UIDropDown<ReleaseChannel> channelDropdown = GenerateUI.DropDown(
            channelRow,
            ReleaseChannel.Stable,
            MainCore.Conf.GetUpdateChannel(),
            channels,
            ch => ch switch {
                ReleaseChannel.Stable => MainCore.Tr.Get("UPDATE_CHANNEL_STABLE", "Stable"),
                ReleaseChannel.ReleaseCandidate => MainCore.Tr.Get("UPDATE_CHANNEL_RC", "Release Candidate"),
                ReleaseChannel.Beta => MainCore.Tr.Get("UPDATE_CHANNEL_BETA", "Beta"),
                ReleaseChannel.Alpha => MainCore.Tr.Get("UPDATE_CHANNEL_ALPHA", "Alpha"),
                _ => ch.ToString(),
            },
            ch => {
                MainCore.Conf.UpdateChannel = (int)ch;
                MainCore.ConfMgr.RequestSave();
            },
            "update_channel"
        );
        channelDropdown.Rect.AddToolTip(
            "DESC_UPDATE_CHANNEL",
            "Which builds to receive when updating. Alpha includes every build; each step up is more stable, with Stable being only final releases."
        );
        objects[updatesTextTr] = (updatesLabelRow.gameObject, channelRow.gameObject);

        var updateCheckRow = GenerateUI.Row(content.transform);
        updateCheckButton = GenerateUI.Button(
            updateCheckRow,
            () => UpdateService.Check(),
            "Check for Updates",
            "update_check"
        );
        updateCheckButton.Label.gameObject.AddComponent<TextLocalization>().Init("CHECK_FOR_UPDATES", "Check for Updates");

        var updateStatusRow = GenerateUI.Row(content.transform);
        // noPad so the text lines up with the progress bar track below.
        updateStatusText = GenerateUI.AddText(updateStatusRow, noPad: true);
        updateStatusText.text = "";

        {
            // Download progress: a rounded track with an accent fill whose
            // width follows UpdateService.Progress, plus a percent readout.
            // Visible only while Installing (and progress is known); drives
            // both real downloads and the dev-simulated one.
            var progressRect = GenerateUI.Row(content.transform, 32f);
            updateProgressRow = progressRect.gameObject;

            GameObject track = new("ProgressTrack");
            track.transform.SetParent(progressRect, false);

            // Same horizontal span as the BackGround() widgets above (full
            // width minus the 250-unit right gutter) so the edges line up;
            // the percent readout sits in that gutter.
            RectTransform trackRect = track.AddComponent<RectTransform>();
            trackRect.anchorMin = new(0f, 0.5f);
            trackRect.anchorMax = new(1f, 0.5f);
            trackRect.pivot = new(0f, 0.5f);
            trackRect.offsetMin = new(0f, -7f);
            trackRect.offsetMax = new(-250f, 7f);

            Image trackImg = track.AddComponent<Image>();
            trackImg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
            trackImg.type = Image.Type.Sliced;
            trackImg.color = UIColors.ObjectBG;
            trackImg.raycastTarget = false;

            GameObject fill = new("ProgressFill");
            fill.transform.SetParent(track.transform, false);

            updateProgressFill = fill.AddComponent<RectTransform>();
            updateProgressFill.anchorMin = Vector2.zero;
            updateProgressFill.anchorMax = new(0f, 1f);
            updateProgressFill.offsetMin = Vector2.zero;
            updateProgressFill.offsetMax = Vector2.zero;

            Image fillImg = fill.AddComponent<Image>();
            fillImg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
            fillImg.type = Image.Type.Sliced;
            fillImg.color = UIColors.ObjectActive;
            fillImg.raycastTarget = false;

            GameObject pctObj = new("ProgressPercent");
            pctObj.transform.SetParent(progressRect, false);

            RectTransform pctRect = pctObj.AddComponent<RectTransform>();
            pctRect.anchorMin = new(1f, 0f);
            pctRect.anchorMax = new(1f, 1f);
            pctRect.pivot = new(0f, 0.5f);
            pctRect.anchoredPosition = new(-238f, 0f);
            pctRect.sizeDelta = new(90f, 0f);

            updateProgressLabel = pctObj.AddComponent<TextMeshProUGUI>();
            updateProgressLabel.font = FontManager.Current;
            updateProgressLabel.fontSize = 18f;
            updateProgressLabel.color = Color.white;
            updateProgressLabel.alignment = TextAlignmentOptions.Left;
            updateProgressLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
            updateProgressLabel.raycastTarget = false;

            updateProgressRow.SetActive(false);
        }

        // Version text on its own row; action buttons on the row below so
        // long tags get the full width instead of squeezing the buttons.
        var updateActionRect = GenerateUI.Row(content.transform);
        updateActionRow = updateActionRect.gameObject;

        HorizontalLayoutGroup actionLayout = updateActionRow.AddComponent<HorizontalLayoutGroup>();
        actionLayout.padding = new RectOffset(16, 12, 0, 0);
        actionLayout.childControlWidth = true;
        actionLayout.childControlHeight = true;
        actionLayout.childForceExpandWidth = false;
        actionLayout.childForceExpandHeight = true;
        actionLayout.childAlignment = TextAnchor.MiddleLeft;

        updateVersionText = GenerateUI.AddText(updateActionRect, true);
        updateVersionText.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement versionLe = updateVersionText.gameObject.AddComponent<LayoutElement>();
        versionLe.flexibleWidth = 1f;

        var updateButtonRect = GenerateUI.Row(content.transform);
        updateButtonRow = updateButtonRect.gameObject;

        HorizontalLayoutGroup buttonLayout = updateButtonRow.AddComponent<HorizontalLayoutGroup>();
        buttonLayout.spacing = 12f;
        buttonLayout.padding = new RectOffset(16, 12, 0, 0);
        buttonLayout.childControlWidth = true;
        buttonLayout.childControlHeight = true;
        buttonLayout.childForceExpandWidth = false;
        buttonLayout.childForceExpandHeight = true;
        buttonLayout.childAlignment = TextAnchor.MiddleLeft;

        static void FixWidth(UIButton button, float width) {
            LayoutElement le = button.Rect.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.flexibleWidth = 0f;
        }

        updateNotesButton = GenerateUI.Button(
            updateButtonRect,
            () => {
                string url = UpdateService.Available?.Url;
                if(!string.IsNullOrEmpty(url)) {
                    Application.OpenURL(url);
                }
            },
            "Notes",
            "update_notes"
        ).SetSecondary();
        FixWidth(updateNotesButton, 100f);
        updateNotesButton.Label.gameObject.AddComponent<TextLocalization>().Init("UPDATE_NOTES", "Notes");
        updateNotesButton.Rect.AddToolTip(
            "DESC_UPDATE_NOTES",
            "Opens this release's notes on GitHub."
        );

        updateSkipButton = GenerateUI.Button(
            updateButtonRect,
            () => UpdateService.Skip(UpdateService.Available),
            "Skip",
            "update_skip"
        ).SetSecondary();
        FixWidth(updateSkipButton, 100f);
        updateSkipButton.Label.gameObject.AddComponent<TextLocalization>().Init("UPDATE_SKIP", "Skip");
        updateSkipButton.Rect.AddToolTip(
            "DESC_UPDATE_SKIP",
            "Hides this version. You'll still be offered the next release."
        );

        updateInstallButton = GenerateUI.Button(
            updateButtonRect,
            () => UpdateService.Install(UpdateService.Available),
            "Install",
            "update_install"
        );
        FixWidth(updateInstallButton, 130f);
        updateInstallButton.Label.gameObject.AddComponent<TextLocalization>().Init("UPDATE_INSTALL", "Install");

        updateUndoButton = GenerateUI.Button(
            updateButtonRect,
            () => UpdateService.UndoSkip(),
            "Undo",
            "update_undo"
        ).SetSecondary();
        FixWidth(updateUndoButton, 100f);
        updateUndoButton.Label.gameObject.AddComponent<TextLocalization>().Init("UPDATE_UNDO", "Undo");

        if(!updateHooked) {
            UpdateService.OnChanged += RefreshUpdates;
            // The status line is plain text (not TextLocalization), so it has
            // to be re-rendered when the language changes.
            MainCore.Tr.OnLanguageChanged += _ => RefreshUpdates();
            updateHooked = true;
        }
        RefreshUpdates();

        var fontLabelRow = GenerateUI.Row(content.transform);
        var fontText = GenerateUI.AddTextH1(fontLabelRow);
        var fontTextTr = fontText.gameObject.AddComponent<TextLocalization>().Init("FONT", "Font");

        // Dropdown + the custom-font manage controls live in one container so
        // the settings search shows/hides them together.
        GameObject fontGroup = new("FontGroup");
        fontGroup.transform.SetParent(content.transform, false);
        fontGroup.AddComponent<RectTransform>();
        var fontGroupLayout = fontGroup.AddComponent<VerticalLayoutGroup>();
        fontGroupLayout.spacing = 8f;
        fontGroupLayout.childControlWidth = true;
        fontGroupLayout.childControlHeight = true;
        fontGroupLayout.childForceExpandWidth = true;
        fontGroupLayout.childForceExpandHeight = false;

        var fontRow = GenerateUI.Row(fontGroup.transform);
        fontDropdown = GenerateUI.DropDown(
            fontRow,
            FontManager.DefaultName,
            FontManager.CurrentName,
            BuildFontValues(),
            DisplayFont,
            OnFontSelected,
            "font_dropdown"
        );
        // Render each option in the face it names (e.g. the "JetBrains Mono"
        // row draws in JetBrains Mono).
        fontDropdown.ItemFont = FontManager.GetFont;

        // Rename / delete row — shown only when a custom font is selected.
        var manageRow = GenerateUI.Row(fontGroup.transform);
        fontManageRow = manageRow.gameObject;

        fontRenameInput = GenerateUI.Input(
            manageRow,
            null,
            FontManager.CurrentName,
            v => pendingFontName = v,
            "Font Name",
            MainCore.Spr.Get(UISprite.Text128),
            "font_rename"
        );
        fontRenameInput.Placeholder.gameObject.AddComponent<TextLocalization>().Init("FONT_NAME", "Font Name");
        fontRenameInput.InputField.characterLimit = 40;

        fontDeleteBtn = GenerateUI.Button(
            manageRow,
            () => DeleteCurrentFont(),
            "Delete",
            "font_delete"
        ).SetSecondary();
        fontDeleteRestColor = fontDeleteBtn.RestColor;
        {
            var br = fontDeleteBtn.Rect;
            br.pivot = new(1f, 1f);
            br.anchorMin = new(1f, 1f);
            br.anchorMax = new(1f, 1f);
            br.sizeDelta = new(104f, 50f);
            // anchoredPosition, not offsetMax: BackGround() leaves a non-zero
            // anchoredPosition (-125) from its stretch inset, and the offsetMax
            // setter folds that into sizeDelta, blowing up the button width.
            br.anchoredPosition = Vector2.zero;
        }
        fontDeleteBtn.Label.gameObject.AddComponent<TextLocalization>().Init("FONT_DELETE", "Delete");

        UIButton fontRenameBtn = GenerateUI.Button(
            manageRow,
            RenameCurrentFont,
            "Rename",
            "font_rename_btn"
        );
        {
            var br = fontRenameBtn.Rect;
            br.pivot = new(1f, 1f);
            br.anchorMin = new(1f, 1f);
            br.anchorMax = new(1f, 1f);
            br.sizeDelta = new(104f, 50f);
            // 104 wide + 8 gap = 112 left of the Delete button. anchoredPosition
            // for the same reason as Delete above.
            br.anchoredPosition = new(-112f, 0f);
        }
        fontRenameBtn.Label.gameObject.AddComponent<TextLocalization>().Init("FONT_RENAME", "Rename");

        var fontStatusRowRect = GenerateUI.Row(fontGroup.transform, 28f);
        fontStatusRow = fontStatusRowRect.gameObject;
        fontStatusText = GenerateUI.AddText(fontStatusRowRect, noPad: true);
        fontStatusText.fontSize = 16f;
        fontStatusText.color = new Color(1f, 1f, 1f, 0.5f);
        fontStatusText.text = "";
        fontStatusRow.SetActive(false);

        RefreshFontManageRow();

        objects[fontTextTr] = (fontLabelRow.gameObject, fontGroup);

        // Applies the chosen font to ADOFAI's own in-game overlay (the level
        // title/artist HUD), handled by the GameOverlayFont feature.
        var gameFontRow = GenerateUI.Row(content.transform);
        UIToggle gameFontToggle = GenerateUI.Toggle(
            gameFontRow,
            defSet.ApplyFontToGameOverlay,
            MainCore.Conf.ApplyFontToGameOverlay,
            toggle => {
                MainCore.Conf.ApplyFontToGameOverlay = toggle;
                MainCore.ConfMgr.RequestSave();
                GameOverlayFont.Refresh();
                RefreshGameFontRow();
            },
            "Use font in the in-game overlay",
            "use_font_in_game_overlay"
        );
        gameFontToggle.Rect.AddToolTip(
            "DESC_USE_FONT_IN_GAME_OVERLAY",
            "Apply the selected font to A Dance of Fire and Ice's own in-game overlay (the level title and artist shown during play), not just this mod's UI. The default SUIT font keeps the game's own font."
        );
        var gameFontToggleTr = gameFontToggle.Label.gameObject.AddComponent<TextLocalization>().Init("USE_FONT_IN_GAME_OVERLAY", "Use font in the in-game overlay");
        objects[gameFontToggleTr] = (fontLabelRow.gameObject, gameFontRow.gameObject);

        // In-game overlay font picker — visible only while the toggle above is on.
        // Lets the game's own text use a different font than the mod UI; the first
        // option ("Same as overlay font") follows the overlay font live.
        var gameFontPickRow = GenerateUI.Row(content.transform);
        gameFontPickerRow = gameFontPickRow.gameObject;
        gameFontDropdown = GenerateUI.DropDown(
            gameFontPickRow,
            FontManager.SameAsOverlay,
            CurrentGameFontValue(),
            BuildGameFontValues(),
            DisplayGameFont,
            OnGameFontSelected,
            "game_font_dropdown"
        );
        // Render each option in the face it names (the "Same as overlay" row uses
        // the default font).
        gameFontDropdown.ItemFont = FontManager.GetFont;
        RefreshGameFontRow();

        // Settings-window font: lets this mod's own settings window use a
        // different face than the gameplay overlays. "Same as overlay font"
        // (the default) follows the Font picker above, so setups that never
        // touch this are unchanged.
        var settingsFontLabelRow = GenerateUI.Row(content.transform);
        var settingsFontText = GenerateUI.AddTextH1(settingsFontLabelRow);
        var settingsFontTextTr = settingsFontText.gameObject.AddComponent<TextLocalization>().Init("SETTINGS_FONT", "Settings Window Font");

        var settingsFontRow = GenerateUI.Row(content.transform);
        settingsFontDropdown = GenerateUI.DropDown(
            settingsFontRow,
            FontManager.SameAsOverlay,
            CurrentSettingsFontValue(),
            BuildGameFontValues(),
            DisplayGameFont,
            OnSettingsFontSelected,
            "settings_font_dropdown"
        );
        settingsFontDropdown.ItemFont = FontManager.GetFont;
        settingsFontDropdown.Rect.AddToolTip(
            "DESC_SETTINGS_FONT",
            "Font for this mod's own settings window. \"Same as overlay font\" follows the Font picker above."
        );
        objects[settingsFontTextTr] = (settingsFontLabelRow.gameObject, settingsFontRow.gameObject);
    }

    private static string CurrentSettingsFontValue() {
        string name = MainCore.Conf.SettingsFontName;
        return string.IsNullOrEmpty(name) ? FontManager.SameAsOverlay : name;
    }

    private static void OnSettingsFontSelected(string name) {
        MainCore.Conf.SettingsFontName = name == FontManager.SameAsOverlay ? "" : name;
        MainCore.ConfMgr.RequestSave();
        // Re-font only the settings window; the overlays keep the overlay font.
        FontManager.ApplyMenuFont();
    }

    private static IReadOnlyList<string> BuildGameFontValues() {
        var list = new List<string> { FontManager.SameAsOverlay };
        list.AddRange(FontManager.GetAvailableFonts());
        return list;
    }

    private static string CurrentGameFontValue() {
        string name = MainCore.Conf.GameOverlayFontName;
        return string.IsNullOrEmpty(name) ? FontManager.SameAsOverlay : name;
    }

    private static string DisplayGameFont(string name) =>
        name == FontManager.SameAsOverlay
            ? Tr("FONT_SAME_AS_OVERLAY", "Same as overlay font")
            : name == FontManager.DefaultName
                ? Tr("FONT_DEFAULT", "Default (Cookie Run Bold)")
                : name;

    private static void OnGameFontSelected(string name) {
        MainCore.Conf.GameOverlayFontName = name == FontManager.SameAsOverlay ? "" : name;
        MainCore.ConfMgr.RequestSave();
        // Re-font the game's own text (captured TMP labels + a re-sweep); live
        // twins follow GameOverlayFontAsset on their own each frame.
        GameOverlayFont.ApplyFontChange();
    }

    // The in-game font picker only applies while the feature is on, so hide it
    // when the toggle is off.
    private static void RefreshGameFontRow() {
        if(gameFontPickerRow != null) {
            gameFontPickerRow.SetActive(MainCore.Conf.ApplyFontToGameOverlay);
        }
    }

    // Rebuild every open font picker after an import, rename, or delete. This
    // also updates selections whose persisted override was renamed or reset.
    private static void RefreshFontDropdowns() {
        if(fontDropdown != null) {
            fontDropdown.SetValues(BuildFontValues());
            fontDropdown.Set(FontManager.CurrentName, false);
        }

        if(gameFontDropdown != null) {
            gameFontDropdown.SetValues(BuildGameFontValues());
            gameFontDropdown.Set(CurrentGameFontValue(), false);
        }

        if(settingsFontDropdown != null) {
            settingsFontDropdown.SetValues(BuildGameFontValues());
            settingsFontDropdown.Set(CurrentSettingsFontValue(), false);
        }
    }

    private static IReadOnlyList<string> BuildFontValues() {
        var list = new List<string>(FontManager.GetAvailableFonts()) { FontManager.AddSentinel };
        return list;
    }

    private static string DisplayFont(string name) =>
        name == FontManager.AddSentinel
            ? Tr("FONT_ADD", "＋  Add custom font…")
            : name == FontManager.DefaultName
                ? Tr("FONT_DEFAULT", "Default (Cookie Run Bold)")
                : name;

    private static void OnFontSelected(string name) {
        if(name == FontManager.AddSentinel) {
            // Action row, not a real font — restore the live selection and pick.
            fontDropdown.Set(FontManager.CurrentName, false);
            AddCustomFont();
            return;
        }

        SetFontStatus(null);
        FontManager.SetFont(name, true);
        RefreshFontManageRow();
    }

    private static void AddCustomFont() {
        string path;
        try {
            path = FileBrowser.PickFile(
                null,
                "Font",
                ["ttf", "otf", "ttc"],
                Tr("FONT_PICK_TITLE", "Select a font file")
            );
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(PageSettings)}] font PickFile failed: {e}");
            return;
        }

        if(string.IsNullOrEmpty(path)) {
            return;
        }

        string name = FontManager.ImportFont(path);
        if(name == null) {
            SetFontStatus(Tr("FONT_IMPORT_FAILED", "Couldn't import that file."));
            return;
        }

        fontDropdown.SetValues(BuildFontValues());
        fontDropdown.Set(name, true); // selects + applies via OnFontSelected
        SetFontStatus(string.Format(Tr("FONT_ADDED", "Added '{0}'."), name));
    }

    private static void RenameCurrentFont() {
        string cur = FontManager.CurrentName;
        if(!FontManager.IsCustomFont(cur)) {
            return;
        }

        if(FontManager.RenameFont(cur, pendingFontName, out string error)) {
            fontDropdown.SetValues(BuildFontValues());
            fontDropdown.Set(FontManager.CurrentName, false);
            RefreshFontManageRow();
            SetFontStatus(string.Format(Tr("FONT_RENAMED", "Renamed to '{0}'."), FontManager.CurrentName));
        } else {
            SetFontStatus(error);
        }
    }

    private static void DeleteCurrentFont() {
        string cur = FontManager.CurrentName;
        if(!FontManager.IsCustomFont(cur)) {
            return;
        }

        // Two-step delete: first click arms the button (red "Sure?").
        if(!fontDeleteArmed) {
            fontDeleteArmed = true;
            fontDeleteBtn.Label.text = Tr("FONT_DELETE_CONFIRM", "Sure?");
            fontDeleteBtn.RestColor = static () => UIColors.SoftRed;
            fontDeleteBtn.Background.color = UIColors.SoftRed;
            return;
        }

        if(FontManager.DeleteFont(cur)) {
            fontDropdown.SetValues(BuildFontValues());
            fontDropdown.Set(FontManager.CurrentName, false);
            RefreshFontManageRow();
            SetFontStatus(string.Format(Tr("FONT_DELETED", "Deleted '{0}'."), cur));
        }
    }

    // Shows the rename/delete row for custom fonts only, refills the rename
    // field, and disarms the delete button.
    private static void RefreshFontManageRow() {
        if(fontManageRow == null) {
            return;
        }

        bool custom = FontManager.IsCustomFont(FontManager.CurrentName);
        fontManageRow.SetActive(custom);

        fontDeleteArmed = false;
        if(fontDeleteBtn != null) {
            fontDeleteBtn.Label.text = Tr("FONT_DELETE", "Delete");
            if(fontDeleteRestColor != null) {
                fontDeleteBtn.RestColor = fontDeleteRestColor;
                fontDeleteBtn.Background.color = fontDeleteRestColor();
            }
        }

        if(custom) {
            pendingFontName = FontManager.CurrentName;
            fontRenameInput?.Set(FontManager.CurrentName, false);
        }

        if(pageContent != null) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);
        }
    }

    private static void SetFontStatus(string message) {
        if(fontStatusText == null || fontStatusRow == null) {
            return;
        }

        fontStatusText.text = message ?? "";
        fontStatusRow.SetActive(!string.IsNullOrEmpty(message));

        if(pageContent != null) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);
        }
    }

    private static string Tr(string key, string def) => MainCore.Tr.Get(key, def);

    // Scrolls the page so the Updates section heading sits at the top of the
    // viewport. Used by the update toast after switching to this page.
    internal static void ScrollToUpdates() {
        if(scrollController == null || updatesAnchor == null || pageContent == null) {
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);

        // Layout children keep the default centre pivot, so anchoredPosition.y
        // is -(distance from content top + half the row height).
        float top = -updatesAnchor.anchoredPosition.y - (updatesAnchor.rect.height * 0.5f);
        scrollController.ScrollTo(top - 6f);
    }

    // Pulls the latest UpdateService state into the update row. Runs on the
    // main thread (UpdateService raises OnChanged via MainThread).
    internal static void RefreshUpdates() {
        if(updateStatusText == null || updateActionRow == null || updateButtonRow == null) {
            return;
        }

        UpdateStatus status = UpdateService.Status;
        UpdateInfo info = UpdateService.Available;
        bool available = status == UpdateStatus.Available && info != null;
        bool skipped = status == UpdateStatus.Skipped;

        static string Tr(string key, string def) => MainCore.Tr.Get(key, def);

        updateStatusText.text = status switch {
            UpdateStatus.Checking => Tr("UPDATE_CHECKING", "Checking for updates…"),
            UpdateStatus.UpToDate => Tr("UPDATE_UP_TO_DATE", "You're up to date."),
            UpdateStatus.Available => Tr("UPDATE_AVAILABLE", "Update available:"),
            UpdateStatus.Installing => Tr("UPDATE_DOWNLOADING", "Downloading update…"),
            UpdateStatus.Installed => string.IsNullOrEmpty(UpdateService.Message)
                ? Tr("UPDATE_INSTALLED", "Update installed — restart the game to apply.")
                : UpdateService.Message,
            UpdateStatus.Skipped => string.Format(
                Tr("UPDATE_SKIPPED", "Skipped {0} — it won't be offered again."),
                UpdateService.SkippedTag
            ),
            UpdateStatus.Failed => UpdateService.Failure switch {
                UpdateFailure.Network => Tr("UPDATE_FAILED_NETWORK", "Couldn't reach GitHub — check your connection."),
                UpdateFailure.NotFound => Tr("UPDATE_FAILED_NOT_FOUND", "Update check failed — release feed not found."),
                UpdateFailure.RateLimited => Tr("UPDATE_FAILED_RATE_LIMIT", "GitHub rate limit reached — try again later."),
                UpdateFailure.InstallError => Tr("UPDATE_FAILED_INSTALL", "Install failed."),
                _ => Tr("UPDATE_FAILED_CHECK", "Update check failed."),
            },
            _ => "",
        };

        // Progress bar mirrors the download. Hidden when the size is unknown
        // (Progress < 0, no Content-Length) — the status text still shows.
        float progress = UpdateService.Progress;
        bool showProgress = status == UpdateStatus.Installing && progress >= 0f;
        updateProgressRow.SetActive(showProgress);

        if(showProgress) {
            float p = Mathf.Clamp01(progress);
            // Floor wide enough that the pill's rounded ends don't collapse.
            updateProgressFill.anchorMax = new(Mathf.Max(p, 0.03f), 1f);
            updateProgressLabel.text = $"{Mathf.RoundToInt(p * 100f)}%";
        }

        updateActionRow.SetActive(available);
        updateButtonRow.SetActive(available || skipped);
        updateNotesButton?.Rect.gameObject.SetActive(available);
        updateSkipButton?.Rect.gameObject.SetActive(available);
        updateInstallButton?.Rect.gameObject.SetActive(available);
        updateUndoButton?.Rect.gameObject.SetActive(skipped);

        if(available) {
            // SUIT has the arrow glyph, but a user-supplied font might not.
            string arrow = HasGlyph('→') ? "→" : ">";
            string simulated = UpdateService.DevSimulate
                ? $" {Tr("UPDATE_SIMULATED", "(simulated)")}"
                : "";
            updateVersionText.text = $"v{Info.DisplayVersion}  {arrow}  {info.Tag}{simulated}";
        } else {
            updateVersionText.text = "";
        }

        if(updateCheckButton != null) {
            updateCheckButton.SetBlocked(status is UpdateStatus.Checking or UpdateStatus.Installing);
        }
    }

    private static bool HasGlyph(char c) {
        try {
            return FontManager.Current != null && FontManager.Current.HasCharacter(c, true, true);
        } catch {
            return false;
        }
    }

    internal static void OnTranslatorLoadEnd() {
        string[] langs = [.. MainCore.Tr.GetLanguages().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];

        languageDropdown.SetValues(langs);
        languageDropdown.Set(
            string.IsNullOrWhiteSpace(MainCore.Conf.Language)
                ? Translator.FALLBACK_LANGUAGE
                : MainCore.Conf.Language,
            false
        );
    }
}
