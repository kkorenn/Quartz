using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using GTweens.Tweens;
using Koren.Core;
using Koren.Utility.Math;
using GTweens.Extensions;

using GTweens.Builders;
using GTweens.Easings;

using TMPro;

namespace Koren.UI.Objects.Impl;

public class UISlider : UIObject {
    public float DefaultValue { get; }
    public float Min;
    public float Max;
    public float Value { get; private set; }

    public string Format {
        get;
        set {
            field = value;
            UpdateValueText();
        }
    }

    public Action<float> OnChanged;
    public Action<float> OnComplete;

    public Func<float, float> Filter;

    public RectTransform FillRect { get; }
    public Image FillImage { get; }

    public TextMeshProUGUI Label { get; }
    public TextMeshProUGUI ValueText { get; }

    public Image ChangedImage { get; }
    public Image ChangedUpImage { get; }

    // Expression-evaluator chrome (ported from Overlayer): a normally-invisible
    // outline that recolors to the eval state while typing, and a preview label
    // that overlays the editor showing "<result> = <typed expr>". EditField is
    // assigned by AddSliderValueEditor after construction (it owns the field).
    public Image OutlineImage { get; }
    public TextMeshProUGUI PreviewLabel { get; }
    public TMP_InputField EditField { get; set; }

    private GTween fillSeq;
    private GTween changeSeq;
    private GTween stateSeq;

    public UISlider(
        string id,
        RectTransform rect,
        RectTransform fillRect,
        Image fillImage,
        TextMeshProUGUI label,
        TextMeshProUGUI valueText,
        Image changedImage,
        Image changedUpImage,
        Image outlineImage,
        TextMeshProUGUI previewLabel,
        float defaultValue,
        float min,
        float max,
        float value,
        Func<float, float> filter,
        Action<float> onChanged,
        Action<float> onComplete,
        string format = "0.##"
    ) : base(id, rect) {
        FillRect = fillRect;
        FillImage = fillImage;
        FillImage.color = UIColors.ObjectActive;

        Label = label;
        ValueText = valueText;

        ChangedImage = changedImage;
        ChangedUpImage = changedUpImage;
        ChangedUpImage.color = UIColors.ObjectBG;

        OutlineImage = outlineImage;
        PreviewLabel = previewLabel;

        DefaultValue = defaultValue;
        Min = min;
        Max = max;

        OnChanged = onChanged;
        OnComplete = onComplete;

        Format = format;
        Filter = filter;

        Value = ApplyFilter(value);
        Value = Mathf.Clamp(Value, Min, Max);

        UpdateVisual(true);
    }

    public void Set(float value, bool invoke = true) {
        // Upstream 4b76865: "NaN" parses as a float; don't let it poison the
        // slider (Clamp(NaN) stays NaN and sticks).
        if(float.IsNaN(value)) {
            return;
        }

        value = ApplyFilter(value);

        Value = Mathf.Clamp(value, Min, Max);

        if(invoke) {
            OnChanged?.Invoke(Value);
        }

        UpdateVisual();
    }

    public void SetOnlyValue(float value, bool noAnimate = false) {
        if(float.IsNaN(value)) {
            return;
        }

        Value = Mathf.Clamp(ApplyFilter(value), Min, Max);
        UpdateVisual(noAnimate);
    }

    public float Normalize() => Mathf.InverseLerp(Min, Max, Value);
    public float Normalize(float value) => Mathf.InverseLerp(Min, Max, value);

    public void SetNormalized(float t, bool invoke = true)
        => Set(Mathf.Lerp(Min, Max, t), invoke);

    private float ApplyFilter(float v) => Filter?.Invoke(v) ?? v;

    private void UpdateValueText() => ValueText?.text = Value.ToString(Format);

    public void UpdateVisual(bool noAnimate = false) {
        fillSeq?.Kill();
        changeSeq?.Kill();
        UpdateValueText();

        float t = Normalize();
        // Epsilon compare (upstream 4b76865) — float equality misses values
        // that round-trip through the value editor.
        float changeAlpha = Mathf.Abs(DefaultValue - Value) > 0.001f ? 1f : 0f;

        if(noAnimate) {
            Vector2 fra = FillRect.anchorMax;
            fra.x = t;
            FillRect.anchorMax = fra;

            Color ci = ChangedImage.color;
            ci.a = changeAlpha;
            ChangedImage.color = ci;

            Color cui = ChangedUpImage.color;
            cui.a = changeAlpha;
            ChangedUpImage.color = cui;

            return;
        }

        fillSeq = GTweenSequenceBuilder.New()
            .Join(
                GTweenExtensions.Tween(
                    () => FillRect.anchorMax.x,
                    x => {
                        Vector2 anchor = FillRect.anchorMax;
                        anchor.x = x;
                        FillRect.anchorMax = anchor;
                    },
                    t,
                    0.6f
                ).SetEasing(Easing.OutExpo)
            ).Build();
        MainCore.TC.Play(fillSeq);

        changeSeq = GTweenSequenceBuilder.New()
            .Join(
                GTweenExtensions.Tween(
                    () => ChangedImage.color.a,
                    x => {
                        Color c = ChangedImage.color;
                        c.a = x;
                        ChangedImage.color = c;
                    },
                    changeAlpha,
                    0.2f
                ).SetEasing(Easing.OutSine)
            )
            .Join(
                GTweenExtensions.Tween(
                    () => ChangedUpImage.color.a,
                    x => {
                        Color c = ChangedUpImage.color;
                        c.a = x;
                        ChangedUpImage.color = c;
                    },
                    changeAlpha,
                    0.2f
                ).SetEasing(Easing.OutSine)
            ).Build();
        MainCore.TC.Play(changeSeq);
    }

    public void OnDrag(float normalizedValue) => SetNormalized(normalizedValue, true);

    // ── Expression-evaluator editing ─────────────────────────────────────────
    // Driven by the editor field's onValueChanged/onEndEdit (wired in
    // GenerateUI.AddSliderValueEditor). PreviewExpression runs live as you type
    // — shows the computed result, recolors the outline by state, and animates
    // the fill to a preview position. CommitExpression applies on Enter/blur.

    private static bool TryParseLiteral(string raw, out float value) =>
        float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || float.TryParse(raw, out value);

    public void PreviewExpression(string raw) {
        var (result, state) = Evaluator.Evaluate(raw, Value, Min, Max);

        if(state == EvalState.Error) {
            if(PreviewLabel != null) {
                PreviewLabel.text = "";
            }

            SetStateVisuals(MathVisuals.GetStateColor(state), true);
            return;
        }

        // A bare literal equal to the parsed result needs no "result = expr"
        // overlay — just preview the fill and color. Anything computed (or
        // clamped) shows the result with a relational symbol.
        bool isLiteral = TryParseLiteral(raw, out float typed) && Mathf.Abs(typed - result) < 0.0001f;
        if(isLiteral) {
            if(PreviewLabel != null) {
                PreviewLabel.text = "";
            }
        } else if(PreviewLabel != null) {
            string symbol = state switch {
                EvalState.OverRange => "<",
                EvalState.UnderRange => ">",
                _ => "="
            };

            PreviewLabel.text = $"{ApplyFilter(result).ToString(Format)} {symbol} <color=#00000000>{raw}</color>";
        }

        SetStateVisuals(MathVisuals.GetStateColor(state), true, result);
    }

    public void CommitExpression(string raw) {
        var (result, state) = Evaluator.Evaluate(raw, Value, Min, Max);

        if(state != EvalState.Error) {
            Set(result);
            OnComplete?.Invoke(Value);
        } else {
            // Unparseable — restore the readout, no change.
            UpdateVisual(true);
        }

        if(PreviewLabel != null) {
            PreviewLabel.text = "";
        }

        SetStateVisuals(UIColors.ObjectActive, false);
    }

    private void SetStateVisuals(Color targetColor, bool isCalculating, float? value = null) {
        stateSeq?.Kill();

        float targetFillAlpha = isCalculating ? (value.HasValue ? 0.3f : 0f) : 1f;

        Color startOutline = OutlineImage.color;
        Color startFill = FillImage.color;
        Color startChanged = ChangedImage.color;
        Color startCaret = EditField != null ? EditField.caretColor : Color.clear;

        stateSeq = GTweenSequenceBuilder.New()
            .Join(
                GTweenExtensions.Tween(
                    () => 0f,
                    x => {
                        OutlineImage.color = Color.Lerp(startOutline, new(targetColor.r, targetColor.g, targetColor.b, isCalculating ? targetColor.a : 0f), x);
                        FillImage.color = Color.Lerp(startFill, new(targetColor.r, targetColor.g, targetColor.b, targetFillAlpha), x);
                        ChangedImage.color = Color.Lerp(startChanged, new(targetColor.r, targetColor.g, targetColor.b, ChangedImage.color.a), x);
                        if(EditField != null) {
                            EditField.caretColor = Color.Lerp(startCaret, new(targetColor.r, targetColor.g, targetColor.b, EditField.caretColor.a), x);
                        }
                    },
                    1f,
                    0.2f
                ).SetEasing(Easing.OutSine)
            ).Build();
        MainCore.TC.Play(stateSeq);

        if(value.HasValue && isCalculating) {
            fillSeq?.Kill();
            fillSeq = GTweenSequenceBuilder.New()
                .Join(
                    GTweenExtensions.Tween(
                        () => FillRect.anchorMax.x,
                        x => {
                            Vector2 anchor = FillRect.anchorMax;
                            anchor.x = x;
                            FillRect.anchorMax = anchor;
                        },
                        Normalize(value.Value),
                        0.4f
                    ).SetEasing(Easing.OutExpo)
                ).Build();
            MainCore.TC.Play(fillSeq);
        }
    }
}