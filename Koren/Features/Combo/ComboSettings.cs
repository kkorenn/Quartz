using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.Combo;

// Persisted config for the center-screen Combo overlay.
// Lives in UserData/Koren/Combo.json — separate from Status.json.
public sealed class ComboSettings : ISettingsFile {
    public bool Enabled = true;
    public bool CountAuto = false;

    public float FontSize = 60f;
    public float OffsetX = 0f;
    public float OffsetY = 0f;

    public bool ShowCaption = true;
    public string CaptionText = "Combo";
    public float CaptionOffsetY = -20f;

    public int ColorMax = 2000;
    public float ColorLowR = 1f, ColorLowG = 1f, ColorLowB = 1f, ColorLowA = 1f;
    public float ColorHighR = 1f, ColorHighG = 0.22f, ColorHighB = 0.22f, ColorHighA = 1f;

    public bool NoPopAnim = false;
    public bool FastAnim = false;

    // === Customization (Bismuth-style extras layered on koren's foundation) ===
    public float MasterSize = 1.0f;
    public float LabelSize = 0.4f;

    public float CountThickness = 0f;

    public float LabelShadowX = 2.5f;
    public float LabelShadowY = -2.5f;
    public float LabelShadowR = 0f, LabelShadowG = 0f, LabelShadowB = 0f, LabelShadowA = 1f;

    public float CountShadowX = 0f;
    public float CountShadowY = 0f;
    public float CountShadowR = 0f, CountShadowG = 0f, CountShadowB = 0f, CountShadowA = 1f;

    public float PulsePeakScale = 1.24f;
    public float LabelPulseOffsetY = 0f;

    public bool SolidColor = false;
    public bool PerfectColorEnabled = false;
    public float PerfectR = 0.886f, PerfectG = 0.404f, PerfectB = 0.427f, PerfectA = 1f;

    public Color GetColorLow() => new(
        Mathf.Clamp01(ColorLowR),
        Mathf.Clamp01(ColorLowG),
        Mathf.Clamp01(ColorLowB),
        Mathf.Clamp01(ColorLowA)
    );

    public void SetColorLow(Color c) {
        ColorLowR = Mathf.Clamp01(c.r);
        ColorLowG = Mathf.Clamp01(c.g);
        ColorLowB = Mathf.Clamp01(c.b);
        ColorLowA = Mathf.Clamp01(c.a);
    }

    public Color GetColorHigh() => new(
        Mathf.Clamp01(ColorHighR),
        Mathf.Clamp01(ColorHighG),
        Mathf.Clamp01(ColorHighB),
        Mathf.Clamp01(ColorHighA)
    );

    public void SetColorHigh(Color c) {
        ColorHighR = Mathf.Clamp01(c.r);
        ColorHighG = Mathf.Clamp01(c.g);
        ColorHighB = Mathf.Clamp01(c.b);
        ColorHighA = Mathf.Clamp01(c.a);
    }

    public Color GetComboColor(int combo) {
        if(PerfectColorEnabled && ColorMax > 0 && combo >= ColorMax) {
            return GetPerfectColor();
        }
        if(SolidColor) {
            return GetColorLow();
        }
        float t = ColorMax <= 0 ? 0f : Mathf.Clamp01((float)combo / ColorMax);
        return Color.Lerp(GetColorLow(), GetColorHigh(), t);
    }

    public Color GetLabelShadowColor() => new(
        Mathf.Clamp01(LabelShadowR), Mathf.Clamp01(LabelShadowG),
        Mathf.Clamp01(LabelShadowB), Mathf.Clamp01(LabelShadowA));

    public void SetLabelShadowColor(Color c) {
        LabelShadowR = Mathf.Clamp01(c.r); LabelShadowG = Mathf.Clamp01(c.g);
        LabelShadowB = Mathf.Clamp01(c.b); LabelShadowA = Mathf.Clamp01(c.a);
    }

    public Color GetCountShadowColor() => new(
        Mathf.Clamp01(CountShadowR), Mathf.Clamp01(CountShadowG),
        Mathf.Clamp01(CountShadowB), Mathf.Clamp01(CountShadowA));

    public void SetCountShadowColor(Color c) {
        CountShadowR = Mathf.Clamp01(c.r); CountShadowG = Mathf.Clamp01(c.g);
        CountShadowB = Mathf.Clamp01(c.b); CountShadowA = Mathf.Clamp01(c.a);
    }

    public Color GetPerfectColor() => new(
        Mathf.Clamp01(PerfectR), Mathf.Clamp01(PerfectG),
        Mathf.Clamp01(PerfectB), Mathf.Clamp01(PerfectA));

    public void SetPerfectColor(Color c) {
        PerfectR = Mathf.Clamp01(c.r); PerfectG = Mathf.Clamp01(c.g);
        PerfectB = Mathf.Clamp01(c.b); PerfectA = Mathf.Clamp01(c.a);
    }

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(CountAuto)] = CountAuto,
            [nameof(FontSize)] = FontSize,
            [nameof(OffsetX)] = OffsetX,
            [nameof(OffsetY)] = OffsetY,
            [nameof(ShowCaption)] = ShowCaption,
            [nameof(CaptionText)] = CaptionText,
            [nameof(CaptionOffsetY)] = CaptionOffsetY,
            [nameof(ColorMax)] = ColorMax,
            [nameof(ColorLowR)] = ColorLowR,
            [nameof(ColorLowG)] = ColorLowG,
            [nameof(ColorLowB)] = ColorLowB,
            [nameof(ColorLowA)] = ColorLowA,
            [nameof(ColorHighR)] = ColorHighR,
            [nameof(ColorHighG)] = ColorHighG,
            [nameof(ColorHighB)] = ColorHighB,
            [nameof(ColorHighA)] = ColorHighA,
            [nameof(NoPopAnim)] = NoPopAnim,
            [nameof(FastAnim)] = FastAnim,
            [nameof(MasterSize)] = MasterSize,
            [nameof(LabelSize)] = LabelSize,
            [nameof(CountThickness)] = CountThickness,
            [nameof(LabelShadowX)] = LabelShadowX,
            [nameof(LabelShadowY)] = LabelShadowY,
            [nameof(LabelShadowR)] = LabelShadowR,
            [nameof(LabelShadowG)] = LabelShadowG,
            [nameof(LabelShadowB)] = LabelShadowB,
            [nameof(LabelShadowA)] = LabelShadowA,
            [nameof(CountShadowX)] = CountShadowX,
            [nameof(CountShadowY)] = CountShadowY,
            [nameof(CountShadowR)] = CountShadowR,
            [nameof(CountShadowG)] = CountShadowG,
            [nameof(CountShadowB)] = CountShadowB,
            [nameof(CountShadowA)] = CountShadowA,
            [nameof(PulsePeakScale)] = PulsePeakScale,
            [nameof(LabelPulseOffsetY)] = LabelPulseOffsetY,
            [nameof(SolidColor)] = SolidColor,
            [nameof(PerfectColorEnabled)] = PerfectColorEnabled,
            [nameof(PerfectR)] = PerfectR,
            [nameof(PerfectG)] = PerfectG,
            [nameof(PerfectB)] = PerfectB,
            [nameof(PerfectA)] = PerfectA,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        CountAuto = IOUtils.Read(token, nameof(CountAuto), CountAuto);
        FontSize = IOUtils.Read(token, nameof(FontSize), FontSize);
        OffsetX = IOUtils.Read(token, nameof(OffsetX), OffsetX);
        OffsetY = IOUtils.Read(token, nameof(OffsetY), OffsetY);
        ShowCaption = IOUtils.Read(token, nameof(ShowCaption), ShowCaption);
        CaptionText = IOUtils.Read(token, nameof(CaptionText), CaptionText);
        CaptionOffsetY = IOUtils.Read(token, nameof(CaptionOffsetY), CaptionOffsetY);
        ColorMax = IOUtils.Read(token, nameof(ColorMax), ColorMax);
        ColorLowR = IOUtils.Read(token, nameof(ColorLowR), ColorLowR);
        ColorLowG = IOUtils.Read(token, nameof(ColorLowG), ColorLowG);
        ColorLowB = IOUtils.Read(token, nameof(ColorLowB), ColorLowB);
        ColorLowA = IOUtils.Read(token, nameof(ColorLowA), ColorLowA);
        ColorHighR = IOUtils.Read(token, nameof(ColorHighR), ColorHighR);
        ColorHighG = IOUtils.Read(token, nameof(ColorHighG), ColorHighG);
        ColorHighB = IOUtils.Read(token, nameof(ColorHighB), ColorHighB);
        ColorHighA = IOUtils.Read(token, nameof(ColorHighA), ColorHighA);
        NoPopAnim = IOUtils.Read(token, nameof(NoPopAnim), NoPopAnim);
        FastAnim = IOUtils.Read(token, nameof(FastAnim), FastAnim);
        MasterSize = IOUtils.Read(token, nameof(MasterSize), MasterSize);
        LabelSize = IOUtils.Read(token, nameof(LabelSize), LabelSize);
        CountThickness = IOUtils.Read(token, nameof(CountThickness), CountThickness);
        LabelShadowX = IOUtils.Read(token, nameof(LabelShadowX), LabelShadowX);
        LabelShadowY = IOUtils.Read(token, nameof(LabelShadowY), LabelShadowY);
        LabelShadowR = IOUtils.Read(token, nameof(LabelShadowR), LabelShadowR);
        LabelShadowG = IOUtils.Read(token, nameof(LabelShadowG), LabelShadowG);
        LabelShadowB = IOUtils.Read(token, nameof(LabelShadowB), LabelShadowB);
        LabelShadowA = IOUtils.Read(token, nameof(LabelShadowA), LabelShadowA);
        CountShadowX = IOUtils.Read(token, nameof(CountShadowX), CountShadowX);
        CountShadowY = IOUtils.Read(token, nameof(CountShadowY), CountShadowY);
        CountShadowR = IOUtils.Read(token, nameof(CountShadowR), CountShadowR);
        CountShadowG = IOUtils.Read(token, nameof(CountShadowG), CountShadowG);
        CountShadowB = IOUtils.Read(token, nameof(CountShadowB), CountShadowB);
        CountShadowA = IOUtils.Read(token, nameof(CountShadowA), CountShadowA);
        PulsePeakScale = IOUtils.Read(token, nameof(PulsePeakScale), PulsePeakScale);
        LabelPulseOffsetY = IOUtils.Read(token, nameof(LabelPulseOffsetY), LabelPulseOffsetY);
        SolidColor = IOUtils.Read(token, nameof(SolidColor), SolidColor);
        PerfectColorEnabled = IOUtils.Read(token, nameof(PerfectColorEnabled), PerfectColorEnabled);
        PerfectR = IOUtils.Read(token, nameof(PerfectR), PerfectR);
        PerfectG = IOUtils.Read(token, nameof(PerfectG), PerfectG);
        PerfectB = IOUtils.Read(token, nameof(PerfectB), PerfectB);
        PerfectA = IOUtils.Read(token, nameof(PerfectA), PerfectA);

        // Back-compat: migrate combo fields from Status.json naming.
        CountAuto = IOUtils.Read(token, "ComboCountAuto", CountAuto);
        Enabled = IOUtils.Read(token, "ShowCombo", Enabled);
    }
}
