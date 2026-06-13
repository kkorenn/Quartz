using Koren.Core;
using Koren.Localization;
using Koren.Resource;
using Koren.UI.Transition;
using Koren.UI.Utility;
using Koren.Update;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GTweens.Tweens;
using Koren.Tween;

using GTweens.Builders;
using GTweens.Easings;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI.Factory;

public static class MenuFactory {
    public static Action<int> OnStateChanged;

    public sealed class MenuItem {
        public int state;
        public GameObject obj;
        public Image bg;
        public GTween hoverSeq;
        public TMP_Text label;
    }

    private static readonly List<MenuItem> items = [];

    // Small dot on the Settings item while an update is available, so the
    // background startup check is visible without opening the Settings page.
    private static GameObject updateBadge;
    private static bool updateHooked;

    public static void CreateMenu(Transform parent) {
        items.Clear();

        // Sized icon variants: 128px sources drawn at 28 units were ~4x
        // minified through the mip chain and visibly mushy. The panel canvas
        // multiplies px/unit by the user's UI scale, so bake for that too.
        float iconUnits = 28f * MainCore.Conf.UIScale;

        var overlay = CreateItem(parent, "Overlay", MainCore.Spr.Get(UISprite.Monitor128, iconUnits), (int)OriginalMenuState.Overlay);
        var gameplay = CreateItem(parent, "Gameplay", MainCore.Spr.Get(UISprite.Gamepad128, iconUnits), (int)OriginalMenuState.Gameplay);
        var visuals = CreateItem(parent, "Visuals", MainCore.Spr.Get(UISprite.Image128, iconUnits), (int)OriginalMenuState.Visuals);
        var tweaks = CreateItem(parent, "Tweaks", MainCore.Spr.Get(UISprite.AdjustmentsHorizontal128, iconUnits), (int)OriginalMenuState.Tweaks);
        var search = CreateItem(parent, "Search", MainCore.Spr.Get(UISprite.MagnifyingGlass128, iconUnits), (int)OriginalMenuState.Search);
        var profiles = CreateItem(parent, "Profiles", MainCore.Spr.Get(UISprite.Users128, iconUnits), (int)OriginalMenuState.Profiles);
        var settings = CreateItem(parent, "Settings", MainCore.Spr.Get(UISprite.Gear128, iconUnits), (int)OriginalMenuState.Settings);
        var credits = CreateItem(parent, "Credits", MainCore.Spr.Get(UISprite.Star128, iconUnits), (int)OriginalMenuState.Credits);

        overlay.label.gameObject.AddComponent<TextLocalization>()
            .Init("OVERLAY", "Overlay");

        gameplay.label.gameObject.AddComponent<TextLocalization>()
            .Init("GAMEPLAY", "Gameplay");

        visuals.label.gameObject.AddComponent<TextLocalization>()
            .Init("VISUALS", "Visuals");

        tweaks.label.gameObject.AddComponent<TextLocalization>()
            .Init("TWEAKS", "Tweaks");

        profiles.label.gameObject.AddComponent<TextLocalization>()
            .Init("PROFILES", "Profiles");

        settings.label.gameObject.AddComponent<TextLocalization>()
            .Init("SETTINGS", "Settings");

        search.label.gameObject.AddComponent<TextLocalization>()
            .Init("SEARCH", "Search");

        credits.label.gameObject.AddComponent<TextLocalization>()
            .Init("CREDITS", "Credits");

        // Developer tab — only present in "dev" builds.
        if(Info.IsDev) {
            var developer = CreateItem(parent, "Developer", MainCore.Spr.Get(UISprite.Wrench128, iconUnits), (int)OriginalMenuState.Developer);
            developer.label.gameObject.AddComponent<TextLocalization>()
                .Init("DEVELOPER", "Developer");
        }

        CreateUpdateBadge(settings.obj.transform);
        if(!updateHooked) {
            UpdateService.OnChanged += RefreshUpdateBadge;
            updateHooked = true;
        }
        RefreshUpdateBadge();

        ApplyState(UICore.CurrentMenuState, true);
    }

    private static void CreateUpdateBadge(Transform parent) {
        updateBadge = new GameObject("UpdateBadge");
        updateBadge.transform.SetParent(parent, false);

        RectTransform rect = updateBadge.AddComponent<RectTransform>();
        rect.anchorMin = new(1f, 0.5f);
        rect.anchorMax = new(1f, 0.5f);
        rect.pivot = new(0.5f, 0.5f);
        rect.anchoredPosition = new(-22f, 0f);
        rect.sizeDelta = new(10f, 10f);

        Image img = updateBadge.AddComponent<Image>();
        // Sized variant: the 256px circle drawn at 10 units is a ~24x
        // minification — far past what the mip chain renders cleanly.
        img.sprite = MainCore.Spr.Get(UISprite.Circle256, 10f * MainCore.Conf.UIScale);
        img.color = UIColors.SoftRed;
        img.raycastTarget = false;

        updateBadge.SetActive(false);
    }

    private static void RefreshUpdateBadge() {
        if(updateBadge == null) {
            return;
        }

        updateBadge.SetActive(UpdateService.Status == UpdateStatus.Available);
    }

    // Re-applies menu selection colors after the accent palette changes.
    public static void RefreshTheme() => ApplyState(UICore.CurrentMenuState, true);

    public static MenuItem CreateItem(Transform parent, string name, Sprite icon, int state) {
        GameObject item = new(name);
        item.transform.SetParent(parent, false);

        RectTransform rect = item.AddComponent<RectTransform>();
        rect.anchorMin = new(0, 1);
        rect.anchorMax = new(1, 1);
        rect.pivot = new(0.5f, 1);
        rect.sizeDelta = new(0, 54);

        Image bg = item.AddComponent<Image>();
        bg.color = UIColors.MenuNormal;

        GameObject iconObj = new("Icon");
        iconObj.transform.SetParent(item.transform, false);

        RectTransform iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = new(0, 0.5f);
        iconRect.anchorMax = new(0, 0.5f);
        iconRect.pivot = new(0, 0.5f);
        iconRect.anchoredPosition = new(24, 0);
        iconRect.sizeDelta = new(28, 28);

        Image iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.raycastTarget = false;

        GameObject textObj = new("Text");
        textObj.transform.SetParent(item.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new(0, 0);
        textRect.anchorMax = new(1, 1);
        textRect.offsetMin = new(70, 0);
        textRect.offsetMax = Vector2.zero;

        TMP_Text label = textObj.AddComponent<TextMeshProUGUI>();
        label.text = name;
        label.font = FontManager.Current;
        label.fontSize = 18;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Left;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.characterSpacing = -3f;

        MenuItem menuItem = new() {
            obj = item,
            bg = bg,
            state = state,
            label = label
        };

        items.Add(menuItem);

        var trigger = item.AddComponent<EventTrigger>();

        void Add(EventTriggerType type, Action cb) {
            var e = new EventTrigger.Entry {
                eventID = type
            };

            e.callback.AddListener(_ => cb());
            trigger.triggers.Add(e);
        }

        Add(EventTriggerType.PointerEnter, () => {
            if(UICore.CurrentMenuState == state) {
                return;
            }

            menuItem.hoverSeq?.Kill();
            menuItem.hoverSeq = GTweenSequenceBuilder.New()
                .Append(bg.GTColor(UIColors.MenuHover, 0.2f).SetEasing(Easing.OutSine))
                .Build();
            MainCore.TC.Play(menuItem.hoverSeq);
        });

        Add(EventTriggerType.PointerExit, () => {
            if(UICore.CurrentMenuState == state) {
                return;
            }

            menuItem.hoverSeq?.Kill();
            menuItem.hoverSeq = GTweenSequenceBuilder.New()
                .Append(bg.GTColor(UIColors.MenuNormal, 0.25f).SetEasing(Easing.OutSine))
                .Build();
            MainCore.TC.Play(menuItem.hoverSeq);
        });

        UnityUtils.AddClickEvent(trigger, _ => SetState(state));

        return menuItem;
    }

    public static void SetState(int to) {
        int from = UICore.CurrentMenuState;

        if(from == to) {
            return;
        }

        UICore.CurrentMenuState = to;

        PageSwicher.SwitchPage(from, to);
        ApplyState(to);

        OnStateChanged?.Invoke(to);
    }

    private static void ApplyState(int id, bool noAnimate = false) {
        for(int i = 0; i < items.Count; i++) {
            var it = items[i];

            it.hoverSeq?.Kill();

            bool selected = it.state == id;

            if(selected) {
                if(noAnimate) {
                    it.bg.color = UIColors.MenuSelected;
                } else {
                    it.bg.color = UIColors.MenuHighlight;

                    it.hoverSeq = it.bg.GTColor(UIColors.MenuSelected, 0.3f)
                        .SetEasing(Easing.OutSine);
                    MainCore.TC.Play(it.hoverSeq);
                }
            } else {
                it.bg.color = UIColors.MenuNormal;
            }
        }
    }
}