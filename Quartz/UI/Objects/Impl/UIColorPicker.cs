using Quartz.Core;
using Quartz.Resource;
using Quartz.Tween;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using GTweens.Builders;
using GTweens.Easings;
using GTweens.Extensions;
using GTweens.Tweens;
using GTweenExtensions = GTweens.Extensions.GTweenExtensions;

using TMPro;

namespace Quartz.UI.Objects.Impl;

public sealed class UIColorPicker : UIObject {
    private const int SvTextureSize = 128;
    private const int HueTextureHeight = 256;

    public Color DefaultValue { get; }
    public Color Value { get; private set; }

    public Action<Color> OnChanged { get; }
    public Action<Color> OnComplete { get; }

    public TextMeshProUGUI Label { get; }
    public TextMeshProUGUI ValueText { get; }
    public Image SwatchImage { get; }
    public Image PreviewImage { get; }
    public Image ChangedImage { get; }

    public sealed class ChannelSlider {
        public readonly int Channel;
        public readonly RectTransform TrackRect;
        public readonly RectTransform FillRect;
        public readonly TextMeshProUGUI ValueText;

        public ChannelSlider(int channel, RectTransform trackRect, RectTransform fillRect, TextMeshProUGUI valueText) {
            Channel = channel;
            TrackRect = trackRect;
            FillRect = fillRect;
            ValueText = valueText;
        }
    }

    private readonly LayoutElement rowLayout;
    private readonly GameObject body;
    private readonly CanvasGroup bodyCanvasGroup;
    private GTween expandSeq;
    private readonly RectTransform rootRect;
    private readonly RectTransform svRect;
    private readonly RectTransform hueRect;
    private readonly RectTransform svHandleRect;
    private readonly RectTransform hueHandleRect;
    private readonly RawImage svImage;
    private readonly RawImage hueImage;
    private readonly TMP_InputField hexInput;
    private readonly ChannelSlider[] channelSliders;
    private readonly float expandedHeight;
    private bool suppressHexInput;

    private Texture2D svTexture;
    private Texture2D hueTexture;

    private float hue;
    private float saturation;
    private float brightness;
    public bool Expanded { get; private set; }

    public UIColorPicker(
        string id,
        RectTransform rect,
        LayoutElement rowLayout,
        GameObject body,
        CanvasGroup bodyCanvasGroup,
        TextMeshProUGUI label,
        TextMeshProUGUI valueText,
        Image swatchImage,
        Image previewImage,
        Image changedImage,
        RectTransform svRect,
        RawImage svImage,
        RectTransform hueRect,
        RawImage hueImage,
        RectTransform svHandleRect,
        RectTransform hueHandleRect,
        TMP_InputField hexInput,
        ChannelSlider[] channelSliders,
        float expandedHeight,
        Color defaultValue,
        Color value,
        Action<Color> onChanged,
        Action<Color> onComplete
    ) : base(id, rect) {
        rootRect = rect;
        this.rowLayout = rowLayout;
        this.body = body;
        this.bodyCanvasGroup = bodyCanvasGroup;
        Label = label;
        ValueText = valueText;
        SwatchImage = swatchImage;
        PreviewImage = previewImage;
        ChangedImage = changedImage;
        this.svRect = svRect;
        this.svImage = svImage;
        this.hueRect = hueRect;
        this.hueImage = hueImage;
        this.svHandleRect = svHandleRect;
        this.hueHandleRect = hueHandleRect;
        this.hexInput = hexInput;
        this.channelSliders = channelSliders ?? Array.Empty<ChannelSlider>();
        this.expandedHeight = expandedHeight;
        DefaultValue = Normalize(defaultValue);
        Value = Normalize(value);
        OnChanged = onChanged;
        OnComplete = onComplete;

        // The swatch and preview show the chosen colour, not a theme colour —
        // exempt them so an accent change doesn't remap them when they happen
        // to match a palette entry.
        if(SwatchImage != null) SwatchImage.gameObject.AddComponent<Quartz.UI.Utility.ThemeExempt>();
        if(PreviewImage != null) PreviewImage.gameObject.AddComponent<Quartz.UI.Utility.ThemeExempt>();

        SetupHexInput();
        Color.RGBToHSV(Value, out hue, out saturation, out brightness);
        BuildHueTexture();
        BuildSvTexture();
        SetExpanded(false, true);
        UpdateVisual(true);
    }

    public void Set(Color color, bool invoke = true) {
        Value = Normalize(color);
        Color.RGBToHSV(Value, out float h, out saturation, out brightness);
        if(saturation > 0.0001f || brightness > 0.0001f) {
            hue = h;
        }

        BuildSvTexture();
        if(invoke) {
            OnChanged?.Invoke(Value);
        }
        UpdateVisual();
    }

    public void Commit() {
        OnComplete?.Invoke(Value);
        UpdateVisual();
    }

    public void ToggleExpanded() => SetExpanded(!Expanded);

    public void SetExpanded(bool expanded, bool noAnimate = false) {
        Expanded = expanded;

        // Kept active and faded via the CanvasGroup so the open/close animates
        // both ways (alpha 0 hides the panel while it overflows the row).
        if(body != null && !body.activeSelf) {
            body.SetActive(true);
        }

        float targetHeight = expanded ? expandedHeight : 50f;
        float targetAlpha = expanded ? 1f : 0f;

        if(bodyCanvasGroup != null) {
            bodyCanvasGroup.blocksRaycasts = expanded;
            bodyCanvasGroup.interactable = expanded;
        }

        expandSeq?.Kill();

        if(noAnimate) {
            if(rowLayout != null) {
                rowLayout.preferredHeight = targetHeight;
                rowLayout.minHeight = 50f;
            }
            if(bodyCanvasGroup != null) {
                bodyCanvasGroup.alpha = targetAlpha;
            }
            if(rootRect != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
            }
            return;
        }

        GTweenSequenceBuilder builder = GTweenSequenceBuilder.New();

        if(rowLayout != null) {
            rowLayout.minHeight = 50f;
            builder.Join(GTweenExtensions.Tween(
                () => rowLayout.preferredHeight,
                x => {
                    rowLayout.preferredHeight = Mathf.Max(50f, x);
                    if(rootRect != null) {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
                    }
                },
                targetHeight,
                0.16f
            ).SetEasing(Easing.OutBack));
        }

        if(bodyCanvasGroup != null) {
            builder.Join(GTweenExtensions.Tween(
                () => bodyCanvasGroup.alpha,
                x => bodyCanvasGroup.alpha = x,
                targetAlpha,
                0.16f
            ).SetEasing(Easing.OutSine));
        }

        expandSeq = builder.Build();
        MainCore.TC.Play(expandSeq);
    }

    public void SetFromSvPointer(Vector2 screenPosition) {
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(svRect, screenPosition, null, out Vector2 local)) {
            return;
        }

        Rect rect = svRect.rect;
        saturation = Mathf.Clamp01(Mathf.InverseLerp(rect.xMin, rect.xMax, local.x));
        brightness = Mathf.Clamp01(Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));
        SetFromHsv();
    }

    public void SetFromHuePointer(Vector2 screenPosition) {
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(hueRect, screenPosition, null, out Vector2 local)) {
            return;
        }

        Rect rect = hueRect.rect;
        hue = Mathf.Clamp01(Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));
        BuildSvTexture();
        SetFromHsv();
    }

    public void SetFromChannelPointer(ChannelSlider slider, Vector2 screenPosition) {
        if(slider == null || slider.TrackRect == null) {
            return;
        }

        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(slider.TrackRect, screenPosition, null, out Vector2 local)) {
            return;
        }

        Rect rect = slider.TrackRect.rect;
        float value = Mathf.Clamp01(Mathf.InverseLerp(rect.xMin, rect.xMax, local.x));
        SetChannel(slider.Channel, value);
    }

    private void SetChannel(int channel, float component) {
        Color next = Value;
        component = Mathf.Clamp01(component);

        if(channel == 0) next.r = component;
        else if(channel == 1) next.g = component;
        else if(channel == 2) next.b = component;
        else next.a = component;

        Set(next);
    }

    private void SetFromHsv() {
        Color next = Color.HSVToRGB(hue, saturation, brightness);
        next.a = Value.a;
        Value = next;
        OnChanged?.Invoke(Value);
        UpdateVisual();
    }

    private void UpdateVisual(bool noAnimate = false) {
        string hex = ToHex(Value);
        if(ValueText != null) ValueText.text = hex;
        if(hexInput != null && !hexInput.isFocused && hexInput.text != hex) {
            suppressHexInput = true;
            hexInput.text = hex;
            suppressHexInput = false;
        }
        if(SwatchImage != null) SwatchImage.color = Value;
        if(PreviewImage != null) PreviewImage.color = Value;

        if(ChangedImage != null) {
            Color c = ChangedImage.color;
            c.a = SameColor(Value, DefaultValue) ? 0f : 1f;
            ChangedImage.color = c;
        }

        if(svHandleRect != null && svRect != null) {
            Rect r = svRect.rect;
            svHandleRect.anchoredPosition = new Vector2(
                Mathf.Lerp(r.xMin, r.xMax, saturation),
                Mathf.Lerp(r.yMin, r.yMax, brightness)
            );
        }

        if(hueHandleRect != null && hueRect != null) {
            Rect r = hueRect.rect;
            hueHandleRect.anchoredPosition = new Vector2(
                0f,
                Mathf.Lerp(r.yMin, r.yMax, hue)
            );
        }

        UpdateChannelSliders();
    }

    private void SetupHexInput() {
        if(hexInput == null) {
            return;
        }

        hexInput.lineType = TMP_InputField.LineType.SingleLine;
        hexInput.richText = false;
        hexInput.characterLimit = 9;
        hexInput.customCaretColor = true;
        hexInput.caretColor = Color.white;
        hexInput.selectionColor = UIColors.MenuHover;
        hexInput.onValueChanged.AddListener(value => {
            if(suppressHexInput) {
                return;
            }

            if(TryParseHex(value, out Color parsed)) {
                Set(parsed);
            }
        });
        hexInput.onEndEdit.AddListener(value => {
            if(TryParseHex(value, out Color parsed)) {
                Set(parsed);
                Commit();
                return;
            }

            UpdateVisual(true);
        });
    }

    private void UpdateChannelSliders() {
        for(int i = 0; i < channelSliders.Length; i++) {
            ChannelSlider slider = channelSliders[i];
            if(slider == null) continue;

            float value = slider.Channel == 0
                ? Value.r
                : slider.Channel == 1
                    ? Value.g
                    : slider.Channel == 2
                        ? Value.b
                        : Value.a;

            if(slider.FillRect != null) {
                Vector2 max = slider.FillRect.anchorMax;
                max.x = value;
                slider.FillRect.anchorMax = max;
            }

            if(slider.ValueText != null) {
                slider.ValueText.text = value.ToString("0.00");
            }
        }
    }

    private void BuildHueTexture() {
        if(hueTexture == null) {
            hueTexture = new Texture2D(1, HueTextureHeight, TextureFormat.RGBA32, false);
            hueTexture.wrapMode = TextureWrapMode.Clamp;
            hueTexture.filterMode = FilterMode.Bilinear;
            if(hueImage != null) hueImage.texture = hueTexture;
        }

        for(int y = 0; y < HueTextureHeight; y++) {
            float t = y / (HueTextureHeight - 1f);
            hueTexture.SetPixel(0, y, Color.HSVToRGB(t, 1f, 1f));
        }
        hueTexture.Apply(false);
    }

    private void BuildSvTexture() {
        if(svTexture == null) {
            svTexture = new Texture2D(SvTextureSize, SvTextureSize, TextureFormat.RGBA32, false);
            svTexture.wrapMode = TextureWrapMode.Clamp;
            svTexture.filterMode = FilterMode.Bilinear;
            if(svImage != null) svImage.texture = svTexture;
        }

        for(int y = 0; y < SvTextureSize; y++) {
            float v = y / (SvTextureSize - 1f);
            for(int x = 0; x < SvTextureSize; x++) {
                float s = x / (SvTextureSize - 1f);
                svTexture.SetPixel(x, y, Color.HSVToRGB(hue, s, v));
            }
        }
        svTexture.Apply(false);
    }

    private static bool SameColor(Color a, Color b) {
        return Mathf.Abs(a.r - b.r) < 0.001f
            && Mathf.Abs(a.g - b.g) < 0.001f
            && Mathf.Abs(a.b - b.b) < 0.001f
            && Mathf.Abs(a.a - b.a) < 0.001f;
    }

    private static Color Normalize(Color color) {
        color.r = Mathf.Clamp01(color.r);
        color.g = Mathf.Clamp01(color.g);
        color.b = Mathf.Clamp01(color.b);
        color.a = Mathf.Clamp01(color.a);
        return color;
    }

    private static string ToHex(Color color) {
        int r = Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f);
        int g = Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f);
        int b = Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);
        int a = Mathf.RoundToInt(Mathf.Clamp01(color.a) * 255f);
        string rgb = "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        return a >= 255 ? rgb : rgb + a.ToString("X2");
    }

    private static bool TryParseHex(string value, out Color color) {
        color = Color.white;
        if(string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        string hex = value.Trim().TrimStart('#');

        try {
            if(hex.Length == 3) {
                color = new Color(
                    Convert.ToInt32(hex.Substring(0, 1), 16) / 15f,
                    Convert.ToInt32(hex.Substring(1, 1), 16) / 15f,
                    Convert.ToInt32(hex.Substring(2, 1), 16) / 15f,
                    1f
                );
                return true;
            }

            if(hex.Length == 6 || hex.Length == 8) {
                color = new Color(
                    Convert.ToInt32(hex.Substring(0, 2), 16) / 255f,
                    Convert.ToInt32(hex.Substring(2, 2), 16) / 255f,
                    Convert.ToInt32(hex.Substring(4, 2), 16) / 255f,
                    hex.Length == 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) / 255f : 1f
                );
                return true;
            }
        }
        catch {
        }

        return false;
    }

    public override void Dispose() {
        base.Dispose();
        if(svTexture != null) Object.Destroy(svTexture);
        if(hueTexture != null) Object.Destroy(hueTexture);
    }
}
