using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.Combo;

// Persisted config for the center-screen Combo overlay.
// Lives in UserData/Koren/Combo.json — separate from Status.json.
public sealed class ComboSettings : ISettingsFile {
    public bool Enabled = true;
    public bool CountAuto = true;

    public float FontSize = 56f;
    public float OffsetX = 0f;
    public float OffsetY = 0f;

    public bool ShowCaption = true;
    public string CaptionText = "Combo";
    public float CaptionOffsetY = 0f;

    public int ColorMax = 2000;
    public float ColorLowR = 1f, ColorLowG = 1f, ColorLowB = 1f, ColorLowA = 1f;
    public float ColorHighR = 1f, ColorHighG = 0.22f, ColorHighB = 0.22f, ColorHighA = 1f;

    public bool NoPopAnim = false;
    public bool FastAnim = false;

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
        float t = ColorMax <= 0 ? 0f : Mathf.Clamp01((float)combo / ColorMax);
        return Color.Lerp(GetColorLow(), GetColorHigh(), t);
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

        // Back-compat: migrate combo fields from Status.json naming.
        CountAuto = IOUtils.Read(token, "ComboCountAuto", CountAuto);
        Enabled = IOUtils.Read(token, "ShowCombo", Enabled);
    }
}
