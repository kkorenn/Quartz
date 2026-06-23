using System.Globalization;
using Quartz.Core;
using Quartz.Localization;
using Quartz.Resource;
using Quartz.UI.Objects.Impl;
using Quartz.UI.Utility;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEngine.EventSystems.PointerEventData;
using GTweens.Tweens;
using GTweens.Easings;
using GTweens.Extensions;

using GTweens.Builders;
using Quartz.Tween;
using GTweenExtensions = GTweens.Extensions.GTweenExtensions;

using TMPro;

namespace Quartz.UI.Generator;

public static partial class GenerateUI {
    public static RectTransform Row(Transform parent, float height = 50f) {
        GameObject obj = new("Row");
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();

        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;

        return rect;
    }

    public static UIToggle Toggle(
        Transform parent,
        bool defaultValue,
        bool value,
        Action<bool> onChanged,
        string text,
        string id
    ) {
        RectTransform rect = BackGround();
        rect.SetParent(parent, false);

        TextMeshProUGUI tmp = AddText(rect);
        tmp.text = text;
        LocalizeById(tmp, id, text);

        GameObject change = AddSmallChangedCircle(rect);
        Image changeImg = change.GetComponent<Image>();

        GameObject toggleCircle = new("ToggleCircle");
        toggleCircle.transform.SetParent(rect, false);

        RectTransform circleRect = toggleCircle.AddComponent<RectTransform>();
        circleRect.anchorMin = new(1f, 0.5f);
        circleRect.anchorMax = new(1f, 0.5f);
        circleRect.pivot = new(0.5f, 0.5f);
        circleRect.anchoredPosition = new(-23f, 0f);
        circleRect.sizeDelta = new(26f, 26f);

        Image circleImage = toggleCircle.AddComponent<Image>();

        UIToggle toggle = new(
            id,
            rect,
            tmp,
            circleImage,
            circleRect,
            changeImg,
            defaultValue,
            value,
            onChanged
        );

        AddButton(rect.gameObject, btn => {
            switch(btn) {
                case InputButton.Left:
                    toggle.Toggle();
                    break;

                case InputButton.Middle:
                    if(
                        MainCore.Conf.MiddleClickToDefault &&
                        toggle.Value != toggle.DefaultValue
                    ) {
                        toggle.Reset();
                    }

                    break;
            }
        });

        return toggle;
    }

    public static UIButton Button(
        Transform parent,
        Action onClick,
        string text,
        string id
    ) {
        RectTransform rect = BackGround();
        rect.SetParent(parent, false);

        TextMeshProUGUI tmp = AddText(rect, true);
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        LocalizeById(tmp, id, text);

        Image bg = rect.GetComponent<Image>();
        bg.color = UIColors.ObjectButton;

        UIButton button = new(
            id,
            rect,
            tmp,
            bg,
            onClick
        );

        AddButton(rect.gameObject, btn => {
            if(btn == InputButton.Left) {
                button.Click();
            }
        }, false);

        EventTrigger trigger = rect.gameObject.GetComponent<EventTrigger>()
            ?? rect.gameObject.AddComponent<EventTrigger>();

        UnityUtils.AddEvent(EventTriggerType.PointerEnter, e => button.OnHoverEnter(), trigger);
        UnityUtils.AddEvent(EventTriggerType.PointerExit, e => button.OnHoverExit(), trigger);

        return button;
    }

    public static UISlider Slider(
        Transform parent,
        float defaultValue,
        float min,
        float max,
        float value,
        Func<float, float> filter,
        Action<float> onChanged,
        Action<float> onComplete,
        string text,
        string id
    ) {
        RectTransform rect = BackGround();
        rect.SetParent(parent, false);

        rect.gameObject.AddComponent<EventTrigger>();

        GameObject change = AddSmallChangedCircle(rect);
        Image changeImg = change.GetComponent<Image>();

        GameObject fill = new("Fill");
        fill.transform.SetParent(rect, false);

        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI label = AddText(rect);
        label.text = text;
        label.alignment = TextAlignmentOptions.Left;
        LocalizeById(label, id, text);

        TextMeshProUGUI valueText = AddText(rect);
        valueText.alignment = TextAlignmentOptions.Right;

        var valueTextRect = valueText.gameObject.GetComponent<RectTransform>();
        valueTextRect.offsetMin = Vector2.zero;
        valueTextRect.offsetMax = new(-16f, 0f);

        Image fillImg = fill.AddComponent<Image>();
        fillImg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        fillImg.type = Image.Type.Sliced;

        fill.AddComponent<Mask>().showMaskGraphic = true;

        GameObject changeUp = AddSmallChangedCircle(fillRect);
        Image changeUpImg = changeUp.GetComponent<Image>();

        // Eval-state outline — normally transparent, recolors while typing an
        // expression. Last sibling so it frames over the fill; raycast off so
        // it never eats slider drags. Matches the panel Border pattern.
        GameObject outline = new("Outline");
        outline.transform.SetParent(rect, false);

        RectTransform outlineRect = outline.AddComponent<RectTransform>();
        outlineRect.anchorMin = Vector2.zero;
        outlineRect.anchorMax = Vector2.one;
        outlineRect.offsetMin = Vector2.zero;
        outlineRect.offsetMax = Vector2.zero;

        Image outlineImg = outline.AddComponent<Image>();
        outlineImg.sprite = MainCore.Spr.Get(UISliceSprite.CircleOutline256P2048);
        outlineImg.type = Image.Type.Sliced;
        outlineImg.color = new Color(1f, 1f, 1f, 0f);
        outlineImg.raycastTarget = false;

        // Live "<result> = <typed expr>" overlay, right-aligned over the value
        // area like the readout. richText for the invisible-tail alignment
        // trick; raycast off so the edit zone underneath still receives clicks.
        TextMeshProUGUI previewLabel = AddText(rect);
        previewLabel.alignment = TextAlignmentOptions.Right;
        previewLabel.richText = true;
        previewLabel.raycastTarget = false;
        previewLabel.text = "";

        RectTransform previewRect = previewLabel.gameObject.GetComponent<RectTransform>();
        previewRect.offsetMin = Vector2.zero;
        previewRect.offsetMax = new(-16f, 0f);

        UISlider slider = new(
            id,
            rect,
            fillRect,
            fillImg,
            label,
            valueText,
            changeImg,
            changeUpImg,
            outlineImg,
            previewLabel,
            defaultValue,
            min,
            max,
            value,
            filter,
            onChanged,
            onComplete
        );

        float Apply(float v) {
            v = filter != null ? filter(v) : v;
            return Mathf.Clamp(v, min, max);
        }

        void SetFromMouse() {
            Vector2 local = rect.InverseTransformPoint(UnityEngine.Input.mousePosition);
            float width = rect.rect.width;

            float t = Mathf.Clamp01((local.x + (width * 0.5f)) / width);
            float v = Mathf.Lerp(min, max, t);

            slider.Set(Apply(v));
        }

        AddButton(rect.gameObject, e => {
            switch(e) {
                case InputButton.Left:
                    SetFromMouse();
                    slider.OnComplete?.Invoke(slider.Value);
                    break;

                case InputButton.Middle:
                    if(!MainCore.Conf.MiddleClickToDefault) {
                        break;
                    }
                    slider.Set(Apply(defaultValue));
                    slider.OnComplete?.Invoke(slider.Value);
                    break;
            }
        }, true);

        EventTrigger trigger = rect.gameObject.GetComponent<EventTrigger>()
            ?? rect.gameObject.AddComponent<EventTrigger>();

        bool isDragging = false;

        UnityUtils.AddEvent(EventTriggerType.BeginDrag, _ => {
            // Upstream 170cb0e: only the left button drags the slider.
            if(!UnityEngine.Input.GetMouseButton(0)) {
                return;
            }
            isDragging = true;
            SetFromMouse();
        }, trigger);

        UnityUtils.AddEvent(EventTriggerType.Drag, _ => {
            if(isDragging && UnityEngine.Input.GetMouseButton(0)) {
                SetFromMouse();
            } else {
                isDragging = false;
            }
        }, trigger);

        UnityUtils.AddEvent(EventTriggerType.EndDrag, _ => {
            if(isDragging) {
                isDragging = false;
                slider.OnComplete?.Invoke(slider.Value);
            }
        }, trigger);

        UnityUtils.AddEvent(EventTriggerType.PointerUp, _ => {
            if(isDragging) {
                isDragging = false;
                slider.OnComplete?.Invoke(slider.Value);
            }
        }, trigger);

        AddSliderValueEditor(slider, rect, valueText, () => Apply(defaultValue));

        slider.Set(Apply(value), false);

        return slider;
    }

    // Click-to-type for sliders: the value readout on the right is a click
    // target that swaps to an input field, so exact values don't have to be
    // hit with the slider itself. Enter/click-away applies (filtered and
    // clamped like any slider change), Esc restores.
    private static void AddSliderValueEditor(
        UISlider slider,
        RectTransform rect,
        TextMeshProUGUI valueText,
        Func<float> applyDefault
    ) {
        // Hidden input field overlaying the value area.
        GameObject editObj = new("ValueEdit");
        editObj.transform.SetParent(rect, false);

        RectTransform editRect = editObj.AddComponent<RectTransform>();
        editRect.anchorMin = new Vector2(1f, 0f);
        editRect.anchorMax = new Vector2(1f, 1f);
        editRect.pivot = new Vector2(1f, 0.5f);
        editRect.anchoredPosition = Vector2.zero;
        editRect.sizeDelta = new Vector2(140f, 0f);
        editObj.AddComponent<RectMask2D>();

        TMP_InputField editField = editObj.AddComponent<TMP_InputField>();

        TextMeshProUGUI editText = AddText(editObj.transform, true);
        editText.alignment = TextAlignmentOptions.Right;
        editText.rectTransform.offsetMax = new Vector2(-16f, 0f);
        // Field is Standard now (expressions need operators/letters), so keep
        // typed input literal — don't let a stray '<' be eaten as rich text.
        editText.richText = false;

        editField.textViewport = editRect;
        editField.textComponent = editText;
        editField.lineType = TMP_InputField.LineType.SingleLine;
        // Standard, not DecimalNumber: the field accepts math expressions
        // (6*7, max/2, pi*10), so operators and letters must be typeable.
        editField.contentType = TMP_InputField.ContentType.Standard;

        // Hand the field to the slider so it owns the eval/preview/state-color
        // pipeline driven by this field's onValueChanged/onEndEdit.
        slider.EditField = editField;

        editObj.SetActive(false);

        bool editing = false;

        void EndEdit(string raw) {
            if(!editing) {
                return;
            }
            editing = false;

            editObj.SetActive(false);
            valueText.gameObject.SetActive(true);

            // Evaluate the expression, commit if valid (else restore), and clear
            // the preview / reset the eval-state visuals.
            slider.CommitExpression(raw);
        }

        void BeginEdit() {
            if(editing) {
                return;
            }
            editing = true;

            valueText.gameObject.SetActive(false);
            editObj.SetActive(true);
            editField.SetTextWithoutNotify(
                slider.Value.ToString("0.###", CultureInfo.InvariantCulture)
            );
            editField.Select();
            editField.ActivateInputField();
        }

        // Live expression preview as the user types (BeginEdit seeds the field
        // via SetTextWithoutNotify, so this doesn't fire on open).
        editField.onValueChanged.AddListener(slider.PreviewExpression);
        editField.onEndEdit.AddListener(EndEdit);

        // Click zone over the readout. Its EventTrigger swallows the press, so
        // clicking the number edits it instead of jumping the slider.
        GameObject zone = new("ValueEditZone");
        zone.transform.SetParent(rect, false);

        RectTransform zoneRect = zone.AddComponent<RectTransform>();
        zoneRect.anchorMin = new Vector2(1f, 0f);
        zoneRect.anchorMax = new Vector2(1f, 1f);
        zoneRect.pivot = new Vector2(1f, 0.5f);
        zoneRect.anchoredPosition = Vector2.zero;
        zoneRect.sizeDelta = new Vector2(110f, 0f);

        zone.AddComponent<EmptyGraphic>().raycastTarget = true;

        EventTrigger zoneTrigger = zone.AddComponent<EventTrigger>();
        UnityUtils.AddClickEvent(zoneTrigger, e => {
            switch(e.button) {
                case PointerEventData.InputButton.Left:
                    BeginEdit();
                    break;

                case PointerEventData.InputButton.Middle:
                    if(MainCore.Conf.MiddleClickToDefault) {
                        slider.Set(applyDefault());
                        slider.OnComplete?.Invoke(slider.Value);
                    }
                    break;
            }
        });

        // Hover tint on the number as the "this is editable" affordance.
        UnityUtils.AddEvent(EventTriggerType.PointerEnter, _ => {
            if(!editing) {
                valueText.color = UIColors.ObjectActiveLightBright;
            }
        }, zoneTrigger);

        UnityUtils.AddEvent(EventTriggerType.PointerExit, _ => {
            valueText.color = Color.white;
        }, zoneTrigger);
    }

    public static UIDropDown<T> DropDown<T>(
        Transform parent,
        T defaultValue,
        T value,
        IReadOnlyList<T> values,
        Func<T, string> display,
        Action<T> onChanged,
        string id,
        float width = 0f,
        string leftLabel = null
    ) {
        const float rowHeight = 50f;
        const float listTopOffset = 62f;

        GameObject root = new("Dropdown");
        root.transform.SetParent(parent, false);

        RectTransform rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = new(0f, 0f);
        rootRect.anchorMax = new(1f, 1f);
        rootRect.pivot = new(0.5f, 0.5f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        RectTransform rect = BackGround();
        rect.SetParent(root.transform, false);
        rect.pivot = new(rect.pivot.x, 1f);
        rect.anchorMin = new(rect.anchorMin.x, 1f);
        rect.anchorMax = new(rect.anchorMax.x, 1f);
        rect.sizeDelta = new(rect.sizeDelta.x, rowHeight);

        if(width > 0f) {
            // Compact: a right-aligned fixed-width bar with a leading label,
            // instead of filling the whole row. Used for small enum pickers.
            rect.anchorMin = new(1f, 1f);
            rect.anchorMax = new(1f, 1f);
            rect.pivot = new(1f, 1f);
            rect.sizeDelta = new(width, rowHeight);
            rect.anchoredPosition = new(-250f, 0f);

            if(leftLabel != null) {
                TextMeshProUGUI lead = AddText(root.transform);
                lead.text = leftLabel;
                lead.raycastTarget = false;
                LocalizeById(lead, id, leftLabel, "LABEL");

                RectTransform leadRect = lead.rectTransform;
                leadRect.anchorMin = new(0f, 1f);
                leadRect.anchorMax = new(1f, 1f);
                leadRect.pivot = new(0.5f, 1f);
                leadRect.offsetMin = new(16f, -rowHeight);
                leadRect.offsetMax = new(-(250f + width + 16f), 0f);
                lead.textWrappingMode = TextWrappingModes.NoWrap;
                lead.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        TextMeshProUGUI tmp = AddText(rect);
        tmp.text = display(value);
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.rectTransform.offsetMax = new(-50f, 0f);

        GameObject change = AddSmallChangedCircle(rect);
        Image changeImg = change.GetComponent<Image>();

        GameObject triangle = new("Triangle");
        triangle.transform.SetParent(rect, false);

        RectTransform triangleRect = triangle.AddComponent<RectTransform>();
        triangleRect.anchorMin = new(1f, 0.5f);
        triangleRect.anchorMax = new(1f, 0.5f);
        triangleRect.pivot = new(0.5f, 0.5f);
        triangleRect.anchoredPosition = new(-23f, 0f);
        triangleRect.sizeDelta = new(26f, 26f);

        Image triangleImage = triangle.AddComponent<Image>();
        triangleImage.sprite = MainCore.Spr.Get(UISprite.Triangle128);

        GameObject list = new("List");
        list.transform.SetParent(root.transform, false);

        RectTransform listRect = list.AddComponent<RectTransform>();
        listRect.anchorMin = new(0f, 1f);
        listRect.anchorMax = new(1f, 1f);
        listRect.pivot = new(0.5f, 1f);
        listRect.offsetMin = new(0f, -listTopOffset);
        listRect.offsetMax = new(-250f, -listTopOffset);

        if(width > 0f) {
            // Align the popup under the compact bar.
            listRect.anchorMin = new(1f, 1f);
            listRect.anchorMax = new(1f, 1f);
            listRect.pivot = new(1f, 1f);
            listRect.sizeDelta = new(width, listRect.sizeDelta.y);
            listRect.anchoredPosition = new(-250f, -listTopOffset);
        }

        Image listBg = list.AddComponent<Image>();
        listBg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        listBg.type = Image.Type.Sliced;
        listBg.color = UIColors.ObjectBG;

        VerticalLayoutGroup layout = list.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 0f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = list.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CanvasGroup listCg = list.AddComponent<CanvasGroup>();
        listCg.alpha = 0f;

        list.SetActive(false);

        UIDropDown<T> dropdown = new(
            id,
            rootRect,
            tmp,
            triangleImage,
            triangleRect,
            changeImg,
            list,
            listRect,
            listCg,
            values,
            display,
            defaultValue,
            value,
            onChanged
        );
        root.AddComponent<DropdownLanguageRefresh>().Init(dropdown.RefreshLanguage);

        GTween layoutSeq = null;
        RectTransform parentRect = parent as RectTransform ?? parent.GetComponent<RectTransform>();
        LayoutElement parentLayout = parent.GetComponent<LayoutElement>();

        void RebuildParentLayouts() {
            if(rootRect != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
            }

            if(parentRect != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
            }

            Transform current = parent.parent;
            while(current != null) {
                RectTransform currentRect = current as RectTransform;
                if(
                    currentRect != null &&
                    (
                        current.GetComponent<LayoutGroup>() != null ||
                        current.GetComponent<ContentSizeFitter>() != null
                    )
                ) {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(currentRect);
                }

                current = current.parent;
            }
        }

        void UpdateHeight() {
            float spacing = layout.spacing;
            int valueCount = dropdown.Values?.Count ?? 0;

            float listHeight =
                (valueCount * rowHeight) +
                (Mathf.Max(0, valueCount - 1) * spacing);

            float targetHeight = dropdown.Expanded ? listTopOffset + listHeight : rowHeight;
            float targetAlpha = dropdown.Expanded ? 1f : 0f;

            layoutSeq?.Kill();

            if(parentLayout != null) {
                parentLayout.minHeight = rowHeight;
                parentLayout.flexibleHeight = 0f;
            }

            layoutSeq = GTweenSequenceBuilder.New()
                .Join(
                    GTweenExtensions.Tween(
                        () => parentLayout != null ? parentLayout.preferredHeight : rowHeight,
                        x => {
                            if(parentLayout != null) {
                                parentLayout.preferredHeight = Mathf.Max(rowHeight, x);
                            }

                            RebuildParentLayouts();
                        },
                        targetHeight,
                        0.14f
                    ).SetEasing(Easing.OutBack)
                )
                .Join(
                    GTweenExtensions.Tween(
                        () => listCg.alpha,
                        x => listCg.alpha = x,
                        targetAlpha,
                        0.16f
                    ).SetEasing(Easing.OutSine)
                )
                .Build();
            MainCore.TC.Play(layoutSeq);
        }

        dropdown.OnLayoutChanged = () => {
            UpdateHeight();
            RebuildParentLayouts();
        };

        // Option rows are built by the dropdown's own RebuildList() (called
        // from its constructor) — building them here too doubled every list
        // with click-dead copies.

        AddButton(rect.gameObject, btn => {
            switch(btn) {
                case InputButton.Left:
                    dropdown.ToggleExpanded();
                    UpdateHeight();
                    RebuildParentLayouts();
                    break;

                case InputButton.Middle:
                    if(
                        MainCore.Conf.MiddleClickToDefault && dropdown.DefaultValue != null &&
                        !EqualityComparer<T>.Default.Equals(
                            dropdown.Value,
                            dropdown.DefaultValue
                        )
                    ) {
                        dropdown.Reset();
                    }
                    break;
            }
        });

        UpdateHeight();
        return dropdown;
    }

    public static UIInput Input(
        Transform parent,
        string defaultValue,
        string value,
        Action<string> onChanged,
        string placeholder,
        Sprite icon,
        string id
    ) {
        RectTransform rect = BackGround();
        rect.SetParent(parent, false);

        GameObject change = AddSmallChangedCircle(rect);
        Image changeImg = change.GetComponent<Image>();

        GameObject iconObj = new("Icon");
        iconObj.transform.SetParent(rect, false);

        RectTransform circleRect = iconObj.AddComponent<RectTransform>();
        circleRect.anchorMin = new(1f, 0.5f);
        circleRect.anchorMax = new(1f, 0.5f);
        circleRect.pivot = new(0.5f, 0.5f);
        circleRect.anchoredPosition = new(-23f, 0f);
        circleRect.sizeDelta = new(26f, 26f);

        Image iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = icon;
        iconImage.color = new Color(1f, 1f, 1f, 0.2f);

        GameObject inputObj = new("Input");
        inputObj.transform.SetParent(rect, false);

        RectTransform inputRect = inputObj.AddComponent<RectTransform>();
        inputRect.anchorMin = Vector2.zero;
        inputRect.anchorMax = Vector2.one;
        inputRect.offsetMin = new(16f, 4f);
        inputRect.offsetMax = new(-12f, -4f);
        inputObj.AddComponent<RectMask2D>();

        TMP_InputField inputField = inputObj.AddComponent<TMP_InputField>();

        var text = AddText(inputObj.transform);
        text.font = FontManager.Current;
        text.text = value ?? string.Empty;
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var placeholderText = AddText(inputObj.transform);
        placeholderText.font = FontManager.Current;
        placeholderText.text = placeholder;
        placeholderText.alignment = TextAlignmentOptions.Left;
        placeholderText.textWrappingMode = TextWrappingModes.NoWrap;
        placeholderText.color = new Color(1, 1, 1, 0.2f);
        LocalizeById(placeholderText, id, placeholder);

        RectTransform placeholderRect = placeholderText.rectTransform;
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;

        inputField.textViewport = inputRect;
        inputField.textComponent = text;
        inputField.placeholder = placeholderText;

        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.richText = false;

        var input = new UIInput(
            id,
            rect,
            inputField,
            placeholderText,
            iconImage,
            changeImg,
            defaultValue,
            value,
            onChanged
        );

        AddButton(rect.gameObject, btn => {
            switch(btn) {
                case InputButton.Middle:
                    if(
                        MainCore.Conf.MiddleClickToDefault &&
                        input.Value != input.DefaultValue
                    ) {
                        input.Reset();
                    }

                    break;
            }
        });

        return input;
    }

    public enum BackGroundType {
        Main,
        Sub,
        Full
    }

    public static RectTransform BackGround() {
        GameObject obj = new("Bg");
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new(0f, 0f);
        rect.anchorMax = new(1f, 1f);
        rect.pivot = new(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = new(-250f, 0f);

        Image img = obj.AddComponent<Image>();
        img.color = UIColors.ObjectBG;
        img.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        img.type = Image.Type.Sliced;

        return rect;
    }

    public static void AddButton(GameObject obj, Action<InputButton> onClick, bool outline = true) {
        EventTrigger trigger = obj.AddComponent<EventTrigger>();

        AddClick(trigger, onClick);

        if(outline) {
            AddOutlineHover(obj, trigger);
        }
    }

    private static void AddClick(EventTrigger trigger, Action<InputButton> onClick)
        => UnityUtils.AddClickEvent(trigger, e => onClick?.Invoke(e.button));

    private static void AddOutlineHover(GameObject obj, EventTrigger trigger) {
        GTween hoverSeq = null;

        GameObject hover = new("Hover");
        hover.transform.SetParent(obj.transform, false);
        hover.transform.SetAsFirstSibling();

        RectTransform hoverRect = hover.AddComponent<RectTransform>();
        hoverRect.anchorMin = Vector2.zero;
        hoverRect.anchorMax = Vector2.one;
        hoverRect.pivot = new Vector2(0.5f, 0.5f);
        hoverRect.offsetMin = Vector2.zero;
        hoverRect.offsetMax = Vector2.zero;

        Image hoverImage = hover.AddComponent<Image>();
        hoverImage.sprite = MainCore.Spr.Get(UISliceSprite.CircleOutline256P2048);
        hoverImage.type = Image.Type.Sliced;

        Color baseColor = UIColors.ObjectActive;
        hoverImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);

        UnityUtils.AddEvent(EventTriggerType.PointerEnter, (e) => {
            hoverSeq?.Kill();
            hoverSeq = GTweenSequenceBuilder.New()
                .Append(GTweenExtensions.Tween(
                    () => hoverImage.color.a,
                    x => {
                        Color c = hoverImage.color;
                        c.a = x;
                        hoverImage.color = c;
                    },
                    1f,
                    0.1f
                ).SetEasing(Easing.OutSine))
                .Build();

            MainCore.TC.Play(hoverSeq);
        }, trigger);

        UnityUtils.AddEvent(EventTriggerType.PointerExit, (e) => {
            hoverSeq?.Kill();
            hoverSeq = GTweenSequenceBuilder.New()
                .Append(GTweenExtensions.Tween(
                    () => hoverImage.color.a,
                    x => {
                        Color c = hoverImage.color;
                        c.a = x;
                        hoverImage.color = c;
                    },
                    0f,
                    0.1f
                ).SetEasing(Easing.OutSine))
                .Build();
            MainCore.TC.Play(hoverSeq);
        }, trigger);
    }

    public static TextMeshProUGUI AddText(Transform parent, bool noPad = false) => CreateText(parent, 24f, false, noPad);

    public static TextMeshProUGUI AddTextH1(Transform parent) => CreateText(parent, 32f, true, true);

    public static TextMeshProUGUI Localize(TextMeshProUGUI text, string key, string defaultValue) {
        if(text == null) {
            return null;
        }

        text.text = defaultValue;
        text.gameObject.AddComponent<TextLocalization>().Init(key, defaultValue);
        return text;
    }

    public static TextMeshProUGUI LocalizeById(
        TextMeshProUGUI text,
        string id,
        string defaultValue,
        string suffix = null
    ) {
        string key = LocaleKeyFromId(id, suffix);
        if(string.IsNullOrEmpty(key) || string.IsNullOrEmpty(defaultValue)) {
            return text;
        }

        return Localize(text, key, defaultValue);
    }

    public static string LocaleKeyFromId(string id, string suffix = null) {
        if(string.IsNullOrWhiteSpace(id)) {
            return null;
        }

        string key = NormalizeLocaleKey(id);

        if(key.StartsWith("PANEL")) {
            int i = "PANEL".Length;
            while(i < key.Length && char.IsDigit(key[i])) {
                i++;
            }

            if(i > "PANEL".Length && i < key.Length && key[i] == '_') {
                key = "PANEL" + key[i..];
            }
        }

        if(key.StartsWith("PANEL_PICK_")) {
            key = "PANEL_STAT_" + key["PANEL_PICK_".Length..];
        }

        if(!string.IsNullOrEmpty(suffix)) {
            key += "_" + NormalizeLocaleKey(suffix);
        }

        return key;
    }

    public static string LocaleKeyFromText(string prefix, string text) {
        string key = NormalizeLocaleKey(text);
        return string.IsNullOrEmpty(prefix) ? key : NormalizeLocaleKey(prefix) + "_" + key;
    }

    private static string NormalizeLocaleKey(string value) {
        if(string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        List<char> chars = [];
        bool lastUnderscore = false;

        foreach(char raw in value.Trim().ToUpperInvariant()) {
            char c = char.IsLetterOrDigit(raw) ? raw : '_';
            if(c == '_') {
                if(lastUnderscore) {
                    continue;
                }
                lastUnderscore = true;
            } else {
                lastUnderscore = false;
            }
            chars.Add(c);
        }

        while(chars.Count > 0 && chars[^1] == '_') {
            chars.RemoveAt(chars.Count - 1);
        }

        return chars.Count == 0 ? null : new string(chars.ToArray());
    }

    private static TextMeshProUGUI CreateText(Transform parent, float size, bool bold, bool noPad) {
        GameObject obj = new("Text");
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new(0f, 0f);
        rect.anchorMax = new(1f, 1f);
        rect.offsetMin = new(noPad ? 0f : 16f, 0f);
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        // Follow the user's chosen font (FontManager.Current) so a custom font
        // survives a restart. The previous code baked SUIT in at construction
        // time, which is why most settings UI reverted to SUIT on next launch
        // even though SetFont's runtime ApplyToAll fixed the in-session view.
        tmp.font = FontManager.Current;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
        tmp.characterSpacing = -3f;

        return tmp;
    }

    public static GameObject AddSmallChangedCircle(RectTransform parent) {
        GameObject obj = new("Changed");
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(6f, -6f);
        rect.sizeDelta = new Vector2(8f, 8f);

        Image img = obj.AddComponent<Image>();
        img.sprite = MainCore.Spr.Get(UISprite.Circle256);
        Color c = UIColors.ObjectActive;
        c.a = 0f;
        img.color = c;

        return obj;
    }

    public static Transform AddToolTip(this Transform parent, string key, string def, Translator tr = null) {
        tr ??= MainCore.Tr;
        return parent.AddToolTipInternal(() => tr.Get(key, def));
    }

    public static Transform AddToolTip(this Transform parent, string tip)
        => parent.AddToolTipInternal(() => tip);

    private static Transform AddToolTipInternal(this Transform parent, System.Func<string> getText) {
        EventTrigger trigger = parent.gameObject.GetComponent<EventTrigger>()
            ?? parent.gameObject.AddComponent<EventTrigger>();

        UnityUtils.AddEvent(
            EventTriggerType.PointerEnter,
            _ => Tooltip.Show(getText()),
            trigger
        );

        UnityUtils.AddEvent(
            EventTriggerType.PointerExit,
            _ => Tooltip.Hide(),
            trigger
        );

        return parent;
    }
}

internal sealed class DropdownLanguageRefresh : MonoBehaviour {
    private Action refresh;
    private Action<TranslationFailState> onLoadEnd;
    private Action<string> onLanguageChanged;

    public void Init(Action refreshAction) {
        refresh = refreshAction;
        onLanguageChanged = _ => refresh?.Invoke();
        onLoadEnd = state => {
            if(state == TranslationFailState.Success) {
                refresh?.Invoke();
            }
        };

        MainCore.Tr.OnLanguageChanged += onLanguageChanged;
        MainCore.Tr.OnLoadEnd += onLoadEnd;
    }

    private void OnDestroy() {
        if(onLanguageChanged != null) {
            MainCore.Tr.OnLanguageChanged -= onLanguageChanged;
        }

        if(onLoadEnd != null) {
            MainCore.Tr.OnLoadEnd -= onLoadEnd;
        }
    }
}
