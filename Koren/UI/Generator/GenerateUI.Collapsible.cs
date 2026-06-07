using Koren.Core;
using Koren.Resource;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEngine.EventSystems.PointerEventData;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI.Generator;

// Two compound widgets used to organize the Status HUD settings page:
//   • Collapsible  — a header bar that toggles a body container visible/
//                    hidden. Body has its own VerticalLayoutGroup so caller
//                    just adds child Rows to it.
//   • SideRadio    — Left | Right two-button radio with a leading label;
//                    fills one Row.
public static partial class GenerateUI {

    public sealed class CollapsibleSection {
        public RectTransform Section;
        public GameObject HeaderObj;
        public RectTransform Body;
        public bool Expanded;

        internal Image arrow;
        internal System.Action applyState;

        public void SetExpanded(bool v) {
            if(Expanded == v) {
                return;
            }
            Expanded = v;
            applyState?.Invoke();
        }
    }

    // Adds a collapsible section under `parent` (which must have a vertical
    // layout). The returned CollapsibleSection.Body is where the caller adds
    // body rows. Body is hidden when collapsed.
    public static CollapsibleSection Collapsible(
        Transform parent,
        string title,
        bool startExpanded
    ) {
        GameObject sectionObj = new("Section_" + title);
        sectionObj.transform.SetParent(parent, false);

        RectTransform sectionRect = sectionObj.AddComponent<RectTransform>();

        VerticalLayoutGroup sectionLayout = sectionObj.AddComponent<VerticalLayoutGroup>();
        sectionLayout.spacing = 6f;
        sectionLayout.childControlWidth = true;
        sectionLayout.childControlHeight = true;
        sectionLayout.childForceExpandWidth = true;
        sectionLayout.childForceExpandHeight = false;

        ContentSizeFitter sectionFitter = sectionObj.AddComponent<ContentSizeFitter>();
        sectionFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Header.
        GameObject headerObj = new("Header");
        headerObj.transform.SetParent(sectionRect, false);

        LayoutElement headerLE = headerObj.AddComponent<LayoutElement>();
        headerLE.preferredHeight = 44f;
        headerLE.minHeight = 44f;

        Image headerBg = headerObj.AddComponent<Image>();
        headerBg.color = UIColors.ObjectBG;
        headerBg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        headerBg.type = Image.Type.Sliced;
        headerBg.raycastTarget = true;

        GameObject arrowObj = new("Arrow");
        arrowObj.transform.SetParent(headerObj.transform, false);
        RectTransform arrowRect = arrowObj.AddComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(0f, 0.5f);
        arrowRect.anchorMax = new Vector2(0f, 0.5f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.anchoredPosition = new Vector2(20f, 0f);
        arrowRect.sizeDelta = new Vector2(14f, 14f);
        Image arrowImg = arrowObj.AddComponent<Image>();
        arrowImg.sprite = MainCore.Spr.Get(UISprite.Triangle128);
        arrowImg.color = Color.white;
        arrowImg.raycastTarget = false;

        GameObject labelObj = new("Label");
        labelObj.transform.SetParent(headerObj.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(42f, 0f);
        labelRect.offsetMax = new Vector2(-12f, 0f);
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.font = MainCore.Res.Get<TMP_FontAsset>(Asset.SUIT_Medium);
        label.fontSize = 20f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Left;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.text = title;
        label.characterSpacing = -3f;
        label.raycastTarget = false;

        // Body.
        GameObject bodyObj = new("Body");
        bodyObj.transform.SetParent(sectionRect, false);
        RectTransform bodyRect = bodyObj.AddComponent<RectTransform>();

        VerticalLayoutGroup bodyLayout = bodyObj.AddComponent<VerticalLayoutGroup>();
        bodyLayout.spacing = 8f;
        bodyLayout.padding = new RectOffset(20, 0, 0, 0);
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = false;

        ContentSizeFitter bodyFitter = bodyObj.AddComponent<ContentSizeFitter>();
        bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CollapsibleSection c = new() {
            Section = sectionRect,
            HeaderObj = headerObj,
            Body = bodyRect,
            Expanded = startExpanded,
            arrow = arrowImg,
        };

        c.applyState = () => {
            bodyObj.SetActive(c.Expanded);
            // Triangle128 default points up; -90° = right (collapsed),
            // 180° = down (expanded).
            arrowObj.transform.localRotation =
                Quaternion.Euler(0f, 0f, c.Expanded ? 180f : -90f);
        };
        c.applyState();

        AddButton(headerObj, btn => {
            if(btn == InputButton.Left) {
                c.SetExpanded(!c.Expanded);
            }
        }, outline: false);

        return c;
    }

    // Left | Right two-button radio with a leading label. Fills one Row.
    // initialLeft = current state (true = Left selected). onChanged is fired
    // with the new value (true = Left, false = Right) when the user picks.
    public static void SideRadio(
        Transform parent,
        string label,
        bool initialLeft,
        Action<bool> onChanged
    ) {
        GameObject bgObj = new("SideRadioBg");
        bgObj.transform.SetParent(parent, false);
        RectTransform bg = bgObj.AddComponent<RectTransform>();
        bg.anchorMin = Vector2.zero;
        bg.anchorMax = Vector2.one;
        bg.offsetMin = Vector2.zero;
        bg.offsetMax = Vector2.zero;

        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = UIColors.ObjectBG;
        bgImg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        bgImg.type = Image.Type.Sliced;

        TextMeshProUGUI labelTmp = AddText(bg);
        labelTmp.text = label;

        Image leftImg = CreateMiniButton(bg, "Left", -134f, out GameObject leftBtnObj);
        Image rightImg = CreateMiniButton(bg, "Right", -34f, out GameObject rightBtnObj);

        bool current = initialLeft;

        void ApplySelection() {
            leftImg.color = current ? UIColors.ObjectActive : UIColors.ObjectButton;
            rightImg.color = !current ? UIColors.ObjectActive : UIColors.ObjectButton;
        }
        ApplySelection();

        EventTrigger leftTrigger = leftBtnObj.AddComponent<EventTrigger>();
        UnityUtils.AddEvent(EventTriggerType.PointerClick, _ => {
            if(!current) {
                current = true;
                ApplySelection();
                onChanged?.Invoke(true);
            }
        }, leftTrigger);

        EventTrigger rightTrigger = rightBtnObj.AddComponent<EventTrigger>();
        UnityUtils.AddEvent(EventTriggerType.PointerClick, _ => {
            if(current) {
                current = false;
                ApplySelection();
                onChanged?.Invoke(false);
            }
        }, rightTrigger);
    }

    private static Image CreateMiniButton(Transform parent, string text, float rightX, out GameObject obj) {
        obj = new GameObject("MiniBtn_" + text);
        obj.transform.SetParent(parent, false);

        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(rightX, 0f);
        rt.sizeDelta = new Vector2(90f, 32f);

        Image img = obj.AddComponent<Image>();
        img.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        img.type = Image.Type.Sliced;
        img.color = UIColors.ObjectButton;
        img.raycastTarget = true;

        GameObject textObj = new("Text");
        textObj.transform.SetParent(rt, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.font = MainCore.Res.Get<TMP_FontAsset>(Asset.SUIT_Regular);
        tmp.fontSize = 16f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = text;
        tmp.raycastTarget = false;

        return img;
    }
}
