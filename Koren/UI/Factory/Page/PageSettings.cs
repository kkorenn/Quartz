using GTweens.Builders;
using GTweens.Easings;
using GTweens.Extensions;
using GTweens.Tweens;
using Koren.Async;
using Koren.Core;
using Koren.IO;
using Koren.Localization;
using Koren.Resource;
using Koren.Tween;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using Koren.Utility;
using Koren.Update;
using UnityEngine;
using UnityEngine.UI;
using GTweenExtensions = GTweens.Extensions.GTweenExtensions;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI.Factory.Page;

internal static class PageSettings {
    private static readonly Dictionary<TextLocalization, (GameObject LabelRow, GameObject MainRow)> objects = [];
    private static UIDropDown<string> languageDropdown;

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
        var overlayerTextTr = overlayerText.gameObject.AddComponent<TextLocalization>().Init("OVERLAYER", "Koren");

        var startupRow = GenerateUI.Row(content.transform);
        UIToggle startupToggle = GenerateUI.Toggle(
            startupRow,
            defSet.ShowOnStartup,
            MainCore.Conf.ShowOnStartup,
            toggle => {
                MainCore.Conf.ShowOnStartup = toggle;
                MainCore.ConfMgr.RequestSave();
            },
            "Show KorenResourcePack Settings at Startup",
            "show_on_startup"
        );
        var startupToggleTr = startupToggle.Label.gameObject.AddComponent<TextLocalization>().Init("SHOW_OVERLAYER_PANEL_AT_STARTUP", "Show KorenResourcePack Settings at Startup");
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
            "Recolors the whole Koren UI. Middle-click to reset."
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

        var fontRow = GenerateUI.Row(content.transform);
        var fontDropdown = GenerateUI.DropDown(
            fontRow,
            FontManager.DefaultName,
            FontManager.CurrentName,
            FontManager.GetAvailableFonts(),
            name => name,
            name => FontManager.SetFont(name, true),
            "font_dropdown"
        );
        // Render each option in the face it names (e.g. the "JetBrains Mono"
        // row draws in JetBrains Mono).
        fontDropdown.ItemFont = FontManager.GetFont;
        objects[fontTextTr] = (fontLabelRow.gameObject, fontRow.gameObject);
    }

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
