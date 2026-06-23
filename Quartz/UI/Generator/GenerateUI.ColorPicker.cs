using Quartz.Core;
using Quartz.Resource;
using Quartz.UI.Objects.Impl;
using Quartz.UI.Utility;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEngine.EventSystems.PointerEventData;

using TMPro;

namespace Quartz.UI.Generator;

// HSV + RGBA color picker builder. Grafted from the KorenResourcePack fork —
// the one widget upstream Overlayer doesn't ship. Builds the collapsed header
// (label/value/swatch) plus the expandable body (SV square, hue bar, hex input,
// R/G/B/A channel sliders) and wires pointer drag handling to the UIColorPicker.
public static partial class GenerateUI {
    public static UIColorPicker ColorPicker(
        Transform parent,
        Color defaultValue,
        Color value,
        Action<Color> onChanged,
        Action<Color> onComplete,
        string text,
        string id,
        bool showAlpha = true
    ) {
        // Body sizing depends on whether the alpha row is present, so the rounded
        // background always wraps the last channel slider (no overflow).
        float lastSliderBottom = showAlpha ? 228f : 190f;
        float bodyHeight = Mathf.Max(200f, lastSliderBottom) + 14f;
        float expandedHeight = 62f + bodyHeight;

        GameObject root = new("ColorPicker");
        root.transform.SetParent(parent, false);

        RectTransform rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = new(0.5f, 0.5f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        RectTransform header = BackGround();
        header.SetParent(root.transform, false);
        header.anchorMin = new(0f, 1f);
        header.anchorMax = new(1f, 1f);
        header.pivot = new(0.5f, 1f);
        header.offsetMin = new(0f, -50f);
        header.offsetMax = new(-250f, 0f);

        TextMeshProUGUI label = AddText(header);
        label.text = text;
        LocalizeById(label, id, text);

        TextMeshProUGUI valueText = AddText(header);
        valueText.alignment = TextAlignmentOptions.Right;
        RectTransform valueRect = valueText.rectTransform;
        valueRect.offsetMin = new(0f, 0f);
        valueRect.offsetMax = new(-72f, 0f);

        GameObject changed = AddSmallChangedCircle(header);
        Image changedImg = changed.GetComponent<Image>();

        GameObject swatch = new("Swatch");
        swatch.transform.SetParent(header, false);
        RectTransform swatchRect = swatch.AddComponent<RectTransform>();
        swatchRect.anchorMin = new(1f, 0.5f);
        swatchRect.anchorMax = new(1f, 0.5f);
        swatchRect.pivot = new(0.5f, 0.5f);
        swatchRect.anchoredPosition = new(-30f, 0f);
        swatchRect.sizeDelta = new(32f, 32f);
        Image swatchImg = swatch.AddComponent<Image>();
        swatchImg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        swatchImg.type = Image.Type.Sliced;

        GameObject body = new("PickerBody");
        body.transform.SetParent(root.transform, false);
        RectTransform bodyRect = body.AddComponent<RectTransform>();
        bodyRect.anchorMin = new(0f, 1f);
        bodyRect.anchorMax = new(1f, 1f);
        bodyRect.pivot = new(0.5f, 1f);
        bodyRect.offsetMin = new(0f, -(62f + bodyHeight));
        bodyRect.offsetMax = new(-250f, -62f);
        Image bodyBg = body.AddComponent<Image>();
        bodyBg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        bodyBg.type = Image.Type.Sliced;
        bodyBg.color = UIColors.ObjectBG;

        // Fades the body in/out during the expand animation.
        CanvasGroup bodyCg = body.AddComponent<CanvasGroup>();

        GameObject sv = new("SaturationValue");
        sv.transform.SetParent(body.transform, false);
        RectTransform svRect = sv.AddComponent<RectTransform>();
        svRect.anchorMin = new(0f, 1f);
        svRect.anchorMax = new(0f, 1f);
        svRect.pivot = new(0f, 1f);
        svRect.anchoredPosition = new(16f, -12f);
        svRect.sizeDelta = new(188f, 188f);
        RawImage svImage = sv.AddComponent<RawImage>();

        GameObject svHandle = new("Handle");
        svHandle.transform.SetParent(sv.transform, false);
        RectTransform svHandleRect = svHandle.AddComponent<RectTransform>();
        svHandleRect.anchorMin = new(0f, 1f);
        svHandleRect.anchorMax = new(0f, 1f);
        svHandleRect.pivot = new(0.5f, 0.5f);
        svHandleRect.sizeDelta = new(18f, 18f);
        Image svHandleImg = svHandle.AddComponent<Image>();
        svHandleImg.sprite = MainCore.Spr.Get(UISliceSprite.CircleOutline256P2048);
        svHandleImg.type = Image.Type.Sliced;
        svHandleImg.color = Color.white;
        svHandleImg.raycastTarget = false;

        GameObject hue = new("Hue");
        hue.transform.SetParent(body.transform, false);
        RectTransform hueRect = hue.AddComponent<RectTransform>();
        hueRect.anchorMin = new(0f, 1f);
        hueRect.anchorMax = new(0f, 1f);
        hueRect.pivot = new(0f, 1f);
        hueRect.anchoredPosition = new(216f, -12f);
        hueRect.sizeDelta = new(28f, 188f);
        RawImage hueImage = hue.AddComponent<RawImage>();

        GameObject hueHandle = new("Handle");
        hueHandle.transform.SetParent(hue.transform, false);
        RectTransform hueHandleRect = hueHandle.AddComponent<RectTransform>();
        hueHandleRect.anchorMin = new(0.5f, 1f);
        hueHandleRect.anchorMax = new(0.5f, 1f);
        hueHandleRect.pivot = new(0.5f, 0.5f);
        hueHandleRect.sizeDelta = new(36f, 5f);
        Image hueHandleImg = hueHandle.AddComponent<Image>();
        hueHandleImg.color = Color.white;
        hueHandleImg.raycastTarget = false;

        GameObject preview = new("Preview");
        preview.transform.SetParent(body.transform, false);
        RectTransform previewRect = preview.AddComponent<RectTransform>();
        previewRect.anchorMin = new(0f, 1f);
        previewRect.anchorMax = new(0f, 1f);
        previewRect.pivot = new(0f, 1f);
        previewRect.anchoredPosition = new(264f, -18f);
        previewRect.sizeDelta = new(58f, 58f);
        Image previewImg = preview.AddComponent<Image>();
        previewImg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        previewImg.type = Image.Type.Sliced;

        GameObject hexObj = new("Hex");
        hexObj.transform.SetParent(body.transform, false);
        RectTransform hexRect = hexObj.AddComponent<RectTransform>();
        hexRect.anchorMin = new(0f, 1f);
        hexRect.anchorMax = new(1f, 1f);
        hexRect.pivot = new(0f, 1f);
        hexRect.offsetMin = new(334f, -58f);
        hexRect.offsetMax = new(-18f, -18f);
        Image hexBg = hexObj.AddComponent<Image>();
        hexBg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        hexBg.type = Image.Type.Sliced;
        hexBg.color = Color.Lerp(UIColors.ObjectBG, Color.black, 0.15f);
        hexObj.AddComponent<RectMask2D>();

        TMP_InputField hexInput = hexObj.AddComponent<TMP_InputField>();

        GameObject hexTextObj = new("Text");
        hexTextObj.transform.SetParent(hexObj.transform, false);
        RectTransform hexTextRect = hexTextObj.AddComponent<RectTransform>();
        hexTextRect.anchorMin = Vector2.zero;
        hexTextRect.anchorMax = Vector2.one;
        hexTextRect.offsetMin = new(12f, 0f);
        hexTextRect.offsetMax = new(-8f, 0f);

        TextMeshProUGUI hexText = hexTextObj.AddComponent<TextMeshProUGUI>();
        hexText.font = FontManager.Current;
        hexText.fontSize = 22f;
        hexText.color = Color.white;
        hexText.alignment = TextAlignmentOptions.Left;
        hexText.verticalAlignment = VerticalAlignmentOptions.Middle;
        hexText.characterSpacing = -3f;
        hexText.textWrappingMode = TextWrappingModes.NoWrap;

        hexInput.textViewport = hexRect;
        hexInput.textComponent = hexText;

        UIColorPicker.ChannelSlider CreateChannelSlider(string channelLabel, int channel, float top) {
            GameObject row = new(channelLabel + "Slider");
            row.transform.SetParent(body.transform, false);

            RectTransform rowRect = row.AddComponent<RectTransform>();
            rowRect.anchorMin = new(0f, 1f);
            rowRect.anchorMax = new(1f, 1f);
            rowRect.pivot = new(0f, 1f);
            rowRect.offsetMin = new(264f, -top - 28f);
            rowRect.offsetMax = new(-18f, -top);

            GameObject labelObj = new("Label");
            labelObj.transform.SetParent(row.transform, false);
            RectTransform labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = new(0f, 0f);
            labelRect.anchorMax = new(0f, 1f);
            labelRect.pivot = new(0f, 0.5f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = new(24f, 0f);
            TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.font = FontManager.Current;
            labelText.fontSize = 18f;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.Left;
            labelText.verticalAlignment = VerticalAlignmentOptions.Middle;
            labelText.characterSpacing = -3f;
            labelText.text = channelLabel;

            GameObject track = new("Track");
            track.transform.SetParent(row.transform, false);
            RectTransform trackRect = track.AddComponent<RectTransform>();
            trackRect.anchorMin = new(0f, 0.5f);
            trackRect.anchorMax = new(1f, 0.5f);
            trackRect.pivot = new(0.5f, 0.5f);
            trackRect.offsetMin = new(28f, -7f);
            trackRect.offsetMax = new(-60f, 7f);
            Image trackBg = track.AddComponent<Image>();
            trackBg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
            trackBg.type = Image.Type.Sliced;
            trackBg.color = Color.Lerp(UIColors.ObjectBG, Color.white, 0.08f);

            GameObject trough = new("Trough");
            trough.transform.SetParent(track.transform, false);
            RectTransform troughRect = trough.AddComponent<RectTransform>();
            troughRect.anchorMin = Vector2.zero;
            troughRect.anchorMax = Vector2.one;
            troughRect.pivot = new(0.5f, 0.5f);
            troughRect.offsetMin = new(3f, 3f);
            troughRect.offsetMax = new(-3f, -3f);
            Image troughImg = trough.AddComponent<Image>();
            troughImg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
            troughImg.type = Image.Type.Sliced;
            troughImg.color = new Color(0f, 0f, 0f, 0.34f);
            troughImg.raycastTarget = false;

            GameObject fill = new("Fill");
            fill.transform.SetParent(track.transform, false);
            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new(0f, 1f);
            fillRect.pivot = new(0f, 0.5f);
            fillRect.offsetMin = new(3f, 3f);
            fillRect.offsetMax = new(-3f, -3f);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
            fillImg.type = Image.Type.Sliced;
            fillImg.color = channel switch {
                0 => new Color(1f, 0.22f, 0.22f, 1f),
                1 => new Color(0.35f, 1f, 0.45f, 1f),
                2 => new Color(0.35f, 0.55f, 1f, 1f),
                _ => new Color(1f, 1f, 1f, 0.55f),
            };

            GameObject valueObj = new("Value");
            valueObj.transform.SetParent(row.transform, false);
            RectTransform valueRect2 = valueObj.AddComponent<RectTransform>();
            valueRect2.anchorMin = new(1f, 0f);
            valueRect2.anchorMax = new(1f, 1f);
            valueRect2.pivot = new(1f, 0.5f);
            valueRect2.offsetMin = new(-54f, 0f);
            valueRect2.offsetMax = Vector2.zero;
            TextMeshProUGUI valueLabel = valueObj.AddComponent<TextMeshProUGUI>();
            valueLabel.font = FontManager.Current;
            valueLabel.fontSize = 18f;
            valueLabel.color = Color.white;
            valueLabel.alignment = TextAlignmentOptions.Right;
            valueLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
            valueLabel.characterSpacing = -3f;

            return new UIColorPicker.ChannelSlider(channel, trackRect, fillRect, valueLabel);
        }

        UIColorPicker.ChannelSlider redSlider = CreateChannelSlider("R", 0, 86f);
        UIColorPicker.ChannelSlider greenSlider = CreateChannelSlider("G", 1, 124f);
        UIColorPicker.ChannelSlider blueSlider = CreateChannelSlider("B", 2, 162f);
        UIColorPicker.ChannelSlider alphaSlider = showAlpha ? CreateChannelSlider("A", 3, 200f) : null;

        UIColorPicker.ChannelSlider[] sliders = showAlpha
            ? new[] { redSlider, greenSlider, blueSlider, alphaSlider }
            : new[] { redSlider, greenSlider, blueSlider };

        var picker = new UIColorPicker(
            id,
            rootRect,
            parent.GetComponent<LayoutElement>(),
            body,
            bodyCg,
            label,
            valueText,
            swatchImg,
            previewImg,
            changedImg,
            svRect,
            svImage,
            hueRect,
            hueImage,
            svHandleRect,
            hueHandleRect,
            hexInput,
            sliders,
            expandedHeight,
            defaultValue,
            value,
            onChanged,
            onComplete
        );

        AddButton(header.gameObject, btn => {
            switch(btn) {
                case InputButton.Left:
                    picker.ToggleExpanded();
                    break;

                case InputButton.Middle:
                    if(MainCore.Conf.MiddleClickToDefault) {
                        picker.Set(defaultValue);
                        picker.Commit();
                    }
                    break;
            }
        });

        void AddPickerDrag(RectTransform target, Action<Vector2> setFromPointer) {
            EventTrigger trigger = target.gameObject.AddComponent<EventTrigger>();

            UnityUtils.AddEvent(EventTriggerType.PointerDown, e => {
                PointerEventData p = e as PointerEventData;
                if(p == null || p.button != InputButton.Left) return;
                setFromPointer(p.position);
            }, trigger);

            UnityUtils.AddEvent(EventTriggerType.Drag, e => {
                PointerEventData p = e as PointerEventData;
                if(p == null || !UnityEngine.Input.GetMouseButton(0)) return;
                setFromPointer(p.position);
            }, trigger);

            UnityUtils.AddEvent(EventTriggerType.PointerUp, e => {
                PointerEventData p = e as PointerEventData;
                if(p == null || p.button != InputButton.Left) return;
                picker.Commit();
            }, trigger);
        }

        AddPickerDrag(svRect, picker.SetFromSvPointer);
        AddPickerDrag(hueRect, picker.SetFromHuePointer);

        void AddChannelDrag(UIColorPicker.ChannelSlider slider) {
            EventTrigger trigger = slider.TrackRect.gameObject.AddComponent<EventTrigger>();

            UnityUtils.AddEvent(EventTriggerType.PointerDown, e => {
                PointerEventData p = e as PointerEventData;
                if(p == null || p.button != InputButton.Left) return;
                picker.SetFromChannelPointer(slider, p.position);
            }, trigger);

            UnityUtils.AddEvent(EventTriggerType.Drag, e => {
                PointerEventData p = e as PointerEventData;
                if(p == null || !UnityEngine.Input.GetMouseButton(0)) return;
                picker.SetFromChannelPointer(slider, p.position);
            }, trigger);

            UnityUtils.AddEvent(EventTriggerType.PointerUp, e => {
                PointerEventData p = e as PointerEventData;
                if(p == null || p.button != InputButton.Left) return;
                picker.Commit();
            }, trigger);
        }

        AddChannelDrag(redSlider);
        AddChannelDrag(greenSlider);
        AddChannelDrag(blueSlider);
        if(showAlpha) {
            AddChannelDrag(alphaSlider);
        }

        return picker;
    }
}
