using Koren.Async;
using Koren.Core;
using Koren.Localization;
using Koren.Resource;
using Koren.UI.Factory;
using Koren.UI.Factory.Page;
using Koren.UI.Objects;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Random = UnityEngine.Random;
using GTweens.Tweens;
using GTweens.Builders;
using GTweens.Extensions;
using Koren.Tween;
using GTweens.Easings;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI;

public enum OriginalMenuState {
    Status,
    Settings,
    Reorganize,
    Credits,
}

public static class UICore {
    private static GameObject canvasObj;
    private static Canvas canvas;
    private static CanvasScaler canvasScaler;

    public static readonly Dictionary<int, RectTransform> Pages = [];
    public static int CurrentMenuState = (int)OriginalMenuState.Status;
    public static readonly Vector2 ReferenceResolution = new(1920, 1080);

    private static Action<TranslationFailState> _onPageSettings;
    private static Action<TranslationFailState> _onRefresh;

    public static void Initialize() {
        canvasObj = new GameObject("KorenUICanvas");
        canvasObj.transform.SetParent(MainCore.Root.transform, false);
        canvasObj.SetActive(false);

        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;

        canvasScaler = canvasObj.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        //canvasScaler.referenceResolution = new(1920, 1080);
        PanelScale = MainCore.Conf.UIScale;
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        UIColors.ApplyAccent(MainCore.Conf.GetAccentColor());
        CreatePanel();
        ResizeHandle.CreateResizeHandles(Panel);
        Tooltip.Initialize(canvasObj.transform);

        CreateExitReorganizeButton();

        MenuFactory.OnStateChanged += state => {
            bool isReorg = state == (int)OriginalMenuState.Reorganize;
            
            if(Panel != null) {
                Panel.gameObject.SetActive(!isReorg);
            }
            
            if(exitReorganizeObj != null) {
                exitReorganizeObj.SetActive(isReorg);
            }
        };

        _onPageSettings = state => {
            if(state == TranslationFailState.Success) {
                PageSettings.OnTranslatorLoadEnd();
            }
        };

        _onRefresh = state => {
            if(state == TranslationFailState.Success) {
                TextLocalization.RefreshAll();
            }
        };

        MainCore.Tr.OnLoadEnd += _onPageSettings;
        MainCore.Tr.OnLoadEnd += _onRefresh;

        TextLocalization.RefreshAll();

        if(MainCore.Conf.IsFirstRun) {
            MainCore.Conf.IsFirstRun = false;
            MainCore.ConfMgr.Save();
            MakeFirstRunHelper();
        }

        if(MainCore.Conf.ShowOnStartup) {
            Open(true);
        }
    }

    private static void CreateExitReorganizeButton() {
        exitReorganizeObj = new GameObject("ExitReorganizeButton");
        exitReorganizeObj.transform.SetParent(canvasObj.transform, false);

        var rect = exitReorganizeObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(240f, 60f);
        rect.anchoredPosition = new Vector2(0f, -40f);

        var img = exitReorganizeObj.AddComponent<Image>();
        img.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P1024);
        img.type = Image.Type.Sliced;
        img.color = UIColors.MenuHighlight;

        var btn = exitReorganizeObj.AddComponent<Button>();
        btn.onClick.AddListener(() => {
            MenuFactory.SetState((int)OriginalMenuState.Status);
        });

        GameObject textObj = new("Text");
        textObj.transform.SetParent(rect, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var label = textObj.AddComponent<TextMeshProUGUI>();
        label.text = "Exit Reorganize";
        label.font = FontManager.Current;
        label.fontSize = 24f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        
        label.gameObject.AddComponent<TextLocalization>()
            .Init("EXIT_REORGANIZE", "Exit Reorganize");

        exitReorganizeObj.SetActive(false);
    }

    private static bool firstRunHelperActivated = false;
    private static GameObject firstRunCanvasObj;
    private static Image firstRunHelperImage;
    private static TextMeshProUGUI firstRunHelperText;
    private static GTween firstRunHelperImageSequence;
    private static GTween secondRunHelperTextSequence;

    private static void MakeFirstRunHelper() {
        Task.Run(async () => {
            await Task.Delay(4000);
            MainThread.Enqueue(() => {
                firstRunHelperActivated = true;

                firstRunCanvasObj = new GameObject("FirstRunHelperCanvas");
                firstRunCanvasObj.transform.SetParent(MainCore.Root.transform, false);

                firstRunCanvasObj.AddComponent<RectTransform>();

                var canvas = firstRunCanvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32767;

                var scaler = firstRunCanvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;

                var frh = new GameObject("FirstRunHelper");
                var frhRect = frh.AddComponent<RectTransform>();
                frh.transform.SetParent(firstRunCanvasObj.transform, false);

                frhRect.anchorMin = new Vector2(0f, 0f);
                frhRect.anchorMax = new Vector2(1f, 0f);
                frhRect.pivot = new Vector2(0.5f, 0f);
                frhRect.offsetMin = new Vector2(0f, 0f);
                frhRect.offsetMax = new Vector2(0f, 4f);

                firstRunHelperImage = frh.AddComponent<Image>();
                firstRunHelperImage.raycastTarget = false;
                firstRunHelperImage.color = new Color(1f, 1f, 1f, 0f);

                var frhTextObj = new GameObject("Text");
                var frhTextRect = frhTextObj.AddComponent<RectTransform>();
                frhTextObj.transform.SetParent(frh.transform, false);

                var tmp = frhTextObj.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 22f;
                tmp.color = Color.white;
                tmp.alignment = TextAlignmentOptions.Bottom;
                tmp.text = "";
                tmp.font = FontManager.Current;

                frhTextRect.anchorMin = new Vector2(0.5f, 0.5f);
                frhTextRect.anchorMax = new Vector2(0.5f, 0.5f);
                frhTextRect.anchoredPosition = new Vector2(0f, 6f);
                frhTextRect.sizeDelta = new Vector2(1000f, 50f);
                frhTextRect.pivot = new Vector2(0.5f, 0f);

                firstRunHelperText = tmp;
                firstRunHelperImageSequence = GTweenSequenceBuilder.New()
                    .Append(firstRunHelperImage.GTAlpha(1.6f, 0.1f).SetEasing(Easing.OutSine))
                    .Append(firstRunHelperImage.GTAlpha(0.04f, 1f).SetEasing(Easing.OutSine))
                    .Build()
                    .SetLoops(-1);

                string fullText = "Press Alt + ` (BackQuote, left of 1 key)";
                secondRunHelperTextSequence = GTweenSequenceBuilder.New()
                    .Append(GTweenExtensions.Tween(
                        () => 0,
                        x => firstRunHelperText.text = fullText[..x],
                        fullText.Length,
                        1.4f
                    ))
                    .Build();

                MainCore.TC.Play(firstRunHelperImageSequence);
                MainCore.TC.Play(secondRunHelperTextSequence);
            });
        });
    }

    private static void EndFirstRunHelper() {
        firstRunHelperImageSequence?.Kill();
        secondRunHelperTextSequence?.Kill();

        firstRunHelperText.text = "";
        string endText = "Great Job!";

        var sequence = GTweenSequenceBuilder.New()
            .Append(firstRunHelperImage.GTAlpha(1.0f, 0.2f).SetEasing(Easing.OutSine))
            .Join(GTweenExtensions.Tween(
                () => 0,
                x => firstRunHelperText.text = endText[..x],
                endText.Length,
                0.8f
            ).SetEasing(Easing.Linear))
            .AppendTime(3.0f)
            .Append(firstRunHelperImage.GTAlpha(0f, 2.0f))
            .Join(firstRunHelperText.GTAlpha(0f, 2.0f))

            .AppendCallback(() => {
                if(firstRunCanvasObj != null) {
                    UnityEngine.Object.Destroy(firstRunCanvasObj);
                }
            })
            .Build();

        MainCore.TC.Play(sequence);
    }

    public static RectTransform Panel;
    public static Image CloseImage;
    public const float MENU_WIDTH = 210f;
    public static RectTransform MenuPanel;
    public static RectTransform Menu;
    public static RectTransform MenuContent;
    private static RectTransform Page;
    private static CanvasGroup menuCanvasGroup;
    private static GameObject exitReorganizeObj;

    public static float PanelScale {
        get;
        set {
            field = value;
            canvasScaler.referenceResolution =
                new Vector2(ReferenceResolution.x, ReferenceResolution.y) / field;
        }
    } = 1f;

    public static float PanelRatio {
        get => canvasScaler.matchWidthOrHeight;
        set => canvasScaler.matchWidthOrHeight = value;
    }

    private static void CreatePanel() {
        GameObject panel = new("Panel");
        panel.transform.SetParent(canvasObj.transform, false);

        {
            var image = panel.AddComponent<Image>();
            image.color = UIColors.PanelBG;
            image.type = Image.Type.Sliced;
            image.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P1024);
        }

        Panel = panel.GetComponent<RectTransform>();
        Panel.anchorMin = new(0.5f, 0.5f);
        Panel.anchorMax = new(0.5f, 0.5f);
        Panel.pivot = new(0.5f, 0.5f);
        Panel.sizeDelta = LastPanelSize = DefaultPanelSize;
        LastPanelPosition = Panel.position;

        panel.AddComponent<RectMask2D>();

        {
            var outline = panel.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new(2, -2);
        }

        {
            // Menu Panel
            GameObject menuPanel = new("MenuPanel");
            menuPanel.transform.SetParent(panel.transform, false);

            var menuPanelRect = menuPanel.AddComponent<RectTransform>();
            menuPanelRect.anchorMin = Vector2.zero;
            menuPanelRect.anchorMax = new(1, 1);
            menuPanelRect.pivot = new(0.5f, 0.5f);
            menuPanelRect.anchoredPosition = Vector2.zero;
            menuPanelRect.offsetMin = new(1, 1);
            menuPanelRect.offsetMax = new(-1, -1);
            menuPanelRect.sizeDelta = Vector2.zero;

            // Mask
            var maskImage = menuPanel.AddComponent<Image>();
            maskImage.color = Color.white;
            maskImage.type = Image.Type.Sliced;
            maskImage.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P1024);
            maskImage.raycastTarget = false;

            var mask = menuPanel.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            Page = PageFactory.CreatePages(menuPanel);

            // Menu
            GameObject menu = new("Menu");
            menu.transform.SetParent(menuPanel.transform, false);

            Menu = menu.AddComponent<RectTransform>();
            Menu.anchorMin = Vector2.zero;
            Menu.anchorMax = new(0, 1);
            Menu.pivot = new(0, 0.5f);

            Menu.sizeDelta = new(MENU_WIDTH, 0);
            Menu.anchoredPosition = new(-MENU_WIDTH, 0);

            var image = menu.AddComponent<Image>();
            image.color = UIColors.MenuBG;

            menuCanvasGroup = Menu.gameObject.AddComponent<CanvasGroup>();

            // Menu Content
            GameObject content = new("Content");
            content.transform.SetParent(Menu, false);

            MenuContent = content.AddComponent<RectTransform>();
            MenuContent.anchorMin = new(0, 1);
            MenuContent.anchorMax = new(1, 1);
            MenuContent.pivot = new(0.5f, 1);

            MenuContent.offsetMin = Vector2.zero;
            MenuContent.offsetMax = new(0, -60);

            // Layout
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            layout.spacing = 0f;
            layout.padding = new RectOffset(0, 0, 0, 0);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            MenuFactory.CreateMenu(MenuContent);

            GameObject power = new("Power");
            power.transform.SetParent(Menu, false);
            var powerRect = power.AddComponent<RectTransform>();
            powerRect.anchorMin = new Vector2(0f, 0f);
            powerRect.anchorMax = new Vector2(1f, 0f);
            powerRect.offsetMin = Vector2.zero;
            powerRect.offsetMax = Vector2.zero;
            powerRect.sizeDelta = new Vector2(0f, 60f);
            powerRect.pivot = new Vector2(0.5f, 0f);
            var powerBg = power.AddComponent<Image>();
            powerBg.color = MainCore.Conf.Active
                    ? new(0, 0, 0, 0.1f)
                    : UIColors.SoftRed;
            var btn = power.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            GTween powerSeq = null;
            btn.onClick.AddListener(() => {
                bool enable = MainCore.Conf.Active = !MainCore.Conf.Active;
                MainCore.SetModEnabled(enable);

                Color target = enable
                    ? new Color(0f, 0f, 0f, 0.1f)
                    : UIColors.SoftRed;

                powerSeq?.Kill();
                powerSeq = GTweenSequenceBuilder.New()
                    .Append(powerBg.GTColor(target, 0.32f).SetEasing(Easing.OutExpo))
                    .Build();

                MainCore.TC.Play(powerSeq);
            });
            GameObject powerIcon = new("PowerIcon");
            powerIcon.transform.SetParent(powerRect, false);
            RectTransform powerIconRect = powerIcon.AddComponent<RectTransform>();
            powerIconRect.anchorMin = new Vector2(0.5f, 0.5f);
            powerIconRect.anchorMax = new Vector2(0.5f, 0.5f);
            powerIconRect.pivot = new Vector2(0.5f, 0.5f);
            powerIconRect.sizeDelta = new Vector2(26f, 26f);
            Image powerIconImage = powerIcon.AddComponent<Image>();
            powerIconImage.sprite = MainCore.Spr.Get(UISprite.Power128);
            powerIconImage.color = new(1f, 1f, 1f, 0.6f);

            GameObject version = new("Version");
            version.transform.SetParent(Menu, false);
            var versionRect = version.AddComponent<RectTransform>();
            versionRect.anchorMin = Vector2.zero;
            versionRect.anchorMax = new(1f, 0f);
            versionRect.offsetMin = new(2f, 0f);
            versionRect.offsetMax = Vector2.zero;
            versionRect.pivot = Vector2.zero;
            var versionText = version.AddComponent<TextMeshProUGUI>();
            versionText.text = $"v{MainCore.Version}";
            versionText.font = FontManager.Current;
            versionText.fontSize = 12f;
            versionText.color = new Color(1f, 1f, 1f, 0.4f);
            versionText.characterSpacing = -3f;
            versionText.alignment = TextAlignmentOptions.BottomLeft;
        }

        // Top Bar
        GameObject topBar = new("TopBar");
        topBar.transform.SetParent(panel.transform, false);
        topBar.AddComponent<DragHandler>();

        var topImage = topBar.AddComponent<Image>();
        topImage.color = UIColors.TopBar;
        topImage.type = Image.Type.Sliced;
        topImage.sprite = MainCore.Spr.Get(UISliceSprite.CircleHalf256P1024);

        var topRect = topBar.GetComponent<RectTransform>();
        topRect.anchorMin = new(0, 1);
        topRect.anchorMax = new(1, 1);
        topRect.offsetMin = new(0, -60);
        topRect.offsetMax = Vector2.zero;
        topRect.pivot = new(0.5f, 1);
        topRect.anchoredPosition = Vector2.zero;
        topRect.sizeDelta = new(0, 60);

        {
            // Logo
            GameObject logo = new("Logo");
            logo.transform.SetParent(topBar.transform, false);

            var logoImage = logo.AddComponent<Image>();
            logoImage.sprite = MainCore.Spr.Get(UISprite.KorenLogo);
            logoImage.preserveAspect = true;

            var logoRect = logo.GetComponent<RectTransform>();
            logoRect.anchorMin = new(0, 0.5f);
            logoRect.anchorMax = new(0, 0.5f);
            logoRect.pivot = new(0, 0.5f);
            logoRect.anchoredPosition = new(14, 0);
            logoRect.sizeDelta = new(46f, 46f);

            var btn = logo.AddComponent<NonRaycastButton>();
            btn.onClick += ToggleMenu;
        }

        {
            // Root button
            GameObject close = new("Close");
            close.transform.SetParent(topBar.transform, false);

            var closeRect = close.AddComponent<RectTransform>();
            closeRect.anchorMin = new(1, 0.5f);
            closeRect.anchorMax = new(1, 0.5f);
            closeRect.pivot = new(1, 0.5f);
            closeRect.anchoredPosition = new(-16, 0);
            closeRect.sizeDelta = new(38, 38);

            // Button
            var btn = close.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => Close());

            // Background circle (hover layer)
            GameObject bg = new("Bg");
            bg.transform.SetParent(close.transform, false);

            CloseImage = bg.AddComponent<Image>();
            CloseImage.sprite = MainCore.Spr.Get(UISprite.Circle256);
            CloseImage.color = new Color(UIColors.SoftRed.r, UIColors.SoftRed.g, UIColors.SoftRed.b, 0f);

            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // X icon (always visible)
            GameObject xObj = new("X");
            xObj.transform.SetParent(close.transform, false);

            Image xImage = xObj.AddComponent<Image>();
            xImage.sprite = MainCore.Spr.Get(UISprite.X128);

            RectTransform xRect = xObj.GetComponent<RectTransform>();
            xRect.anchorMin = Vector2.zero;
            xRect.anchorMax = Vector2.one;
            xRect.offsetMin = new(4, 4);
            xRect.offsetMax = new(-4, -4);

            EventTrigger trigger = close.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry {
                eventID = EventTriggerType.PointerEnter
            };
            enter.callback.AddListener(_ => CloseImage.color = new Color(CloseImage.color.r, CloseImage.color.g, CloseImage.color.b, 1f));

            var exit = new EventTrigger.Entry {
                eventID = EventTriggerType.PointerExit
            };
            exit.callback.AddListener(_ => CloseImage.color = new Color(CloseImage.color.r, CloseImage.color.g, CloseImage.color.b, 0f));

            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }
    }

    private static float holdStartTime = 0f;
    private static bool holdingToggle = false;

    private static GTween panelTweener;
    private static GTween resetSequence;

    private static bool isOpen = false;
    public static bool IsOpen => isOpen;

    public static Vector2 LastPanelPosition;
    public static Vector2 LastPanelSize;

    public static Vector2 DefaultPanelSize => new(
        Mathf.Min(1280f / MainCore.Conf.UIScale, Screen.width / MainCore.Conf.UIScale),
        Mathf.Min(720f / MainCore.Conf.UIScale, Screen.height / MainCore.Conf.UIScale)
    );

    public static void HandleUpdate() {
        if(canvasObj == null) {
            return;
        }

        bool pressed =
            Input.GetKey(KeyCode.LeftAlt)
            && Input.GetKey(KeyCode.BackQuote);

        // key down
        if(Input.GetKey(KeyCode.LeftAlt)
            && Input.GetKeyDown(KeyCode.BackQuote)) {
            Toggle();

            holdStartTime = Time.unscaledTime;
            holdingToggle = true;
        }

        // hold reset
        if(holdingToggle && pressed) {
            if(Time.unscaledTime - holdStartTime >= 0.4f) {
                ResetScalePosition(!isOpen);
                holdingToggle = false;
            }
        }

        // key up
        if(Input.GetKeyUp(KeyCode.BackQuote)) {
            holdingToggle = false;
        }

        UIObject.TickAll();
        Tooltip.Tick();
    }

    private static Vector2 GetRandomOffscreenPosition() {
        float halfW = Screen.width * 0.5f;
        float halfH = Screen.height * 0.5f;

        int side = Random.Range(0, 4);

        return side switch {
            // Left
            0 => new(
                -halfW - Panel.sizeDelta.x,
                Random.Range(-halfH, halfH)
            ),

            // Right
            1 => new(
                halfW + Panel.sizeDelta.x,
                Random.Range(-halfH, halfH)
            ),

            // Top
            2 => new(
                Random.Range(-halfW, halfW),
                halfH + Panel.sizeDelta.y
            ),

            // Bottom
            _ => new(
                Random.Range(-halfW, halfW),
                -halfH - Panel.sizeDelta.y
            )
        };
    }

    public static void Open(bool noAnimate = false) {
        if(isOpen) {
            return;
        }

        isOpen = true;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if(panelTweener != null) {
            panelTweener.Complete();
            panelTweener.Kill();
        }

        if(resetSequence != null) {
            resetSequence.Complete();
            resetSequence.Kill();
        }

        if(noAnimate) {
            Panel.anchoredPosition = LastPanelPosition;
            Panel.sizeDelta = LastPanelSize;

            canvasObj.SetActive(true);
            return;
        }

        Vector2 startPos = GetRandomOffscreenPosition();

        Panel.anchoredPosition = startPos;
        Panel.sizeDelta = LastPanelSize;

        canvasObj.SetActive(true);

        panelTweener = Panel.GTAnchorPos(LastPanelPosition, 0.25f)
            .SetEasing(Easing.OutExpo);
        MainCore.TC.Play(panelTweener);

        if(firstRunHelperActivated) {
            firstRunHelperActivated = false;
            EndFirstRunHelper();
        }
    }

    public static void Close(bool noAnimate = false) {
        if(!isOpen) {
            return;
        }

        isOpen = false;

        LastPanelPosition = Panel.anchoredPosition;
        LastPanelSize = Panel.sizeDelta;

        CloseImage.color = new Color(
            CloseImage.color.r,
            CloseImage.color.g,
            CloseImage.color.b,
            0f
        );

        if(panelTweener != null) {
            panelTweener.Complete();
            panelTweener.Kill();
        }

        if(resetSequence != null) {
            resetSequence.Complete();
            resetSequence.Kill();
        }

        if(noAnimate) {
            canvasObj.SetActive(false);
            return;
        }

        Vector2 targetPos = GetRandomOffscreenPosition();

        panelTweener = Panel
            .GTAnchorPos(targetPos, 0.25f)
            .SetEasing(Easing.OutExpo)
            .OnComplete(() => canvasObj.SetActive(false));
        MainCore.TC.Play(panelTweener);
    }

    public static void Toggle(bool noAnimate = false) {
        if(isOpen) {
            Close(noAnimate);
        } else {
            Open(noAnimate);
        }
    }

    public static void ResetScalePosition(bool noAnimate = false) {
        Vector2 targetSize = DefaultPanelSize;

        LastPanelPosition = Vector2.zero;
        LastPanelSize = targetSize;

        panelTweener?.Kill();
        resetSequence?.Kill();

        if(noAnimate) {
            Panel.anchoredPosition = LastPanelPosition;
            Panel.sizeDelta = LastPanelSize;
            return;
        }

        resetSequence = GTweenSequenceBuilder.New()
            .Append(Panel.GTAnchorPos(LastPanelPosition, 0.26f).SetEasing(Easing.OutExpo))
            .Join(Panel.GTSizeDelta(LastPanelSize, 0.26f).SetEasing(Easing.OutExpo))
            .Build();

        MainCore.TC.Play(resetSequence);
    }

    private static bool isMenuOpen = false;
    private static GTween menuSequence;

    public static void OpenMenu() {
        menuSequence?.Kill();

        isMenuOpen = true;

        Menu.anchoredPosition = new(-MENU_WIDTH, 0);
        menuCanvasGroup.interactable = true;
        menuCanvasGroup.blocksRaycasts = true;

        menuSequence = GTweenSequenceBuilder.New()
            .Join(Menu.GTAnchorPos(Vector2.zero, 0.6f).SetEasing(Easing.OutExpo))
            .Join(menuCanvasGroup.GTFade(1f, 0.4f).SetEasing(Easing.OutSine))
            .Join(Page.GTOffsetMin(new Vector2(MENU_WIDTH, 0), 0.6f).SetEasing(Easing.OutExpo))
            .Build();
        MainCore.TC.Play(menuSequence);

        isMenuOpen = true;
    }

    public static void CloseMenu() {
        menuSequence?.Kill();

        menuCanvasGroup.interactable = false;
        menuCanvasGroup.blocksRaycasts = false;

        menuSequence = GTweenSequenceBuilder.New()
            .Join(Menu.GTAnchorPos(new Vector2(-MENU_WIDTH, 0), 0.4f).SetEasing(Easing.OutExpo))
            .Join(menuCanvasGroup.GTFade(0f, 0.3f).SetEasing(Easing.OutSine))
            .Join(Page.GTOffsetMin(new Vector2(0, 0), 0.4f).SetEasing(Easing.OutExpo))
            .Build();
        MainCore.TC.Play(menuSequence);

        isMenuOpen = false;
    }

    public static void ToggleMenu() {
        if(isMenuOpen) {
            CloseMenu();
        } else {
            OpenMenu();
        }
    }

    public static List<string> Search(string query, IEnumerable<string> source) {
        if(string.IsNullOrWhiteSpace(query)) {
            return [.. source];
        }

        string q = NormalizeString(query);

        if(string.IsNullOrEmpty(q)) {
            return [];
        }

        return [..
        source
        .Select(original => new {
            Original = original,
            Normalized = NormalizeString(original)
        })
        .Select(x => new {
            x.Original,
            Score = ScoreMatch(x.Normalized, q)
        })
        .Where(x => x.Score > 0)
        .OrderByDescending(x => x.Score)
        .Select(x => x.Original)
        ];
    }

    private static int ScoreMatch(string normalizedValue, string normalizedQuery) {
        if(normalizedValue == normalizedQuery) {
            return 100;
        }

        if(normalizedValue.StartsWith(normalizedQuery)) {
            return 80;
        }

        if(normalizedValue.Contains(normalizedQuery)) {
            return 50;
        }

        return 0;
    }

    public static string NormalizeString(string input) {
        if(string.IsNullOrEmpty(input)) {
            return string.Empty;
        }

        char[] chars = [.. input.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)];
        return new string(chars);
    }

    // Accent theming. Applies a new accent palette and recolors every already-built
    // Image in the canvas by remapping old palette colors -> new ones (white/icon
    // images aren't palette entries, so they pass through untouched).
    public static void SetAccentColor(Color accent, bool save) {
        UIColors.Palette previous = UIColors.Current;
        MainCore.Conf.SetAccentColor(accent);
        UIColors.ApplyAccent(MainCore.Conf.GetAccentColor());
        RefreshTheme(previous);

        if(save) {
            MainCore.ConfMgr.Save();
        }
    }

    private static void RefreshTheme(UIColors.Palette previous) {
        if(canvasObj != null) {
            Image[] images = canvasObj.GetComponentsInChildren<Image>(true);
            for(int i = 0; i < images.Length; i++) {
                Image img = images[i];
                if(img == null) continue;
                img.color = RemapThemeColor(img.color, previous);
            }
        }

        MenuFactory.RefreshTheme();
    }

    private static Color RemapThemeColor(Color color, UIColors.Palette previous) {
        Color next;
        if(TryRemapRgb(color, previous.ObjectActive, UIColors.ObjectActive, out next)) return next;
        if(TryRemapRgb(color, previous.ObjectActiveBright, UIColors.ObjectActiveBright, out next)) return next;
        if(TryRemapRgb(color, previous.ObjectActiveLightBright, UIColors.ObjectActiveLightBright, out next)) return next;
        if(TryRemapRgb(color, previous.ObjectButton, UIColors.ObjectButton, out next)) return next;
        if(TryRemapRgb(color, previous.ObjectBG, UIColors.ObjectBG, out next)) return next;
        if(TryRemapRgb(color, previous.MenuSelected, UIColors.MenuSelected, out next)) return next;
        if(TryRemapRgb(color, previous.MenuHighlight, UIColors.MenuHighlight, out next)) return next;
        if(TryRemapRgb(color, previous.MenuHover, UIColors.MenuHover, out next)) return next;
        if(TryRemapRgb(color, previous.MenuNormal, UIColors.MenuNormal, out next)) return next;
        if(TryRemapRgb(color, previous.MenuBG, UIColors.MenuBG, out next)) return next;
        if(TryRemapRgb(color, previous.TopBar, UIColors.TopBar, out next)) return next;
        if(TryRemapRgb(color, previous.PanelBG, UIColors.PanelBG, out next)) return next;
        return color;
    }

    private static bool TryRemapRgb(Color color, Color from, Color to, out Color result) {
        const float tolerance = 0.018f;
        if(Mathf.Abs(color.r - from.r) <= tolerance
            && Mathf.Abs(color.g - from.g) <= tolerance
            && Mathf.Abs(color.b - from.b) <= tolerance) {
            result = new Color(to.r, to.g, to.b, color.a);
            return true;
        }

        result = color;
        return false;
    }

    public static void Dispose() {
        MainCore.Tr.OnLoadEnd -= _onPageSettings;
        MainCore.Tr.OnLoadEnd -= _onRefresh;
        Tooltip.Dispose();
        UnityEngine.Object.Destroy(canvasObj);
        canvasObj = null;
    }
}