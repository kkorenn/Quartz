using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.Combo;

// Persisted config for the center-screen Combo overlay.
// Lives in UserData/Koren/Combo.json — separate from Status.json.
//
// The base layout (large value with a caption beneath, anchored below the
// progress bar) is the original KorenResourcePack combo. The extra knobs —
// master/caption scaling, per-element drop shadows, count thickness, solid /
// perfect-combo color, and the configurable pulse — were ported from the
// combo-progressbar-playcount branch. Every added field defaults to a value
// that reproduces the original look, so an existing Combo.json is unchanged.
public sealed class ComboSettings : ISettingsFile {
    public bool Enabled = true;
    public bool CountAuto = true;

    // When XPerfect is installed + active, count ONLY its X (dead-center)
    // perfects toward the combo, and prefix the caption with "X". A non-X
    // perfect (early/late perfect) breaks the combo. Off = every Perfect counts.
    public bool XPerfectComboEnabled = false;

    // === Sizing ===
    // FontSize is the count's base pixel size (original field). MasterSize
    // scales the whole overlay; CaptionScale is the caption size as a
    // fraction of the count size (0.35 reproduces the original hard-coded
    // ratio).
    public float FontSize = 56f;
    public float MasterSize = 1f;
    public float CaptionScale = 0.35f;

    // === Position ===
    public float OffsetX = 0f;
    public float OffsetY = 58.8050537f;

    // === Caption / Label ===
    public bool ShowCaption = true;
    public string CaptionText = "Combo";
    public float CaptionOffsetY = -40f;
    // Drop shadow (TMP underlay). Offsets are in "pixel-ish" units.
    public bool CaptionShadowEnabled = true;
    public float CaptionShadowX = 1.5f;
    public float CaptionShadowY = -1.5f;
    public float CaptionShadowSoftness = 0f;
    public float CaptionShadowR = 0f, CaptionShadowG = 0f, CaptionShadowB = 0f, CaptionShadowA = 0.5019608f;

    // === Count ===
    // TMP face dilate — thickens the value strokes. Range roughly -0.5 (thin)
    // to 0.5 (thick); 0 = native weight.
    public float CountThickness = 0f;
    public bool CountShadowEnabled = true;
    public float CountShadowX = 1.5f;
    public float CountShadowY = -1.5f;
    public float CountShadowSoftness = 0f;
    public float CountShadowR = 0f, CountShadowG = 0f, CountShadowB = 0f, CountShadowA = 0.5019608f;

    // === Color ===
    public int ColorMax = 2000;
    public float ColorLowR = 1f, ColorLowG = 1f, ColorLowB = 1f, ColorLowA = 1f;
    public float ColorHighR = 1f, ColorHighG = 0.22f, ColorHighB = 0.22f, ColorHighA = 1f;
    // SolidColor → always use the low color. PerfectColor → override with a
    // dedicated color once the count reaches ColorMax. Both default off so the
    // original low→high gradient is unchanged.
    public bool SolidColor = false;
    public bool PerfectColorEnabled = false;
    public float PerfectR = 0.886f, PerfectG = 0.404f, PerfectB = 0.427f, PerfectA = 1f;

    // === Animation ===
    // NoPopAnim disables the pulse entirely. CountPulseScale is the extra
    // scale at the pulse peak (0.24 reproduces the original 1.24x pop).
    // PulseDuration is the full pop length in seconds (0.255 ≈ the original
    // 0.075 out + 0.18 settle). LabelPulseOffsetY kicks the caption up on each
    // pop (0 = no kick, the original behavior).
    public bool NoPopAnim = false;
    public float CountPulseScale = 0.149999991f;
    public float PulseDuration = 0.099999994f;
    public float LabelPulseOffsetY = 7f;

    public Color GetColorLow() => new(
        Mathf.Clamp01(ColorLowR), Mathf.Clamp01(ColorLowG),
        Mathf.Clamp01(ColorLowB), Mathf.Clamp01(ColorLowA));

    public void SetColorLow(Color c) {
        ColorLowR = Mathf.Clamp01(c.r); ColorLowG = Mathf.Clamp01(c.g);
        ColorLowB = Mathf.Clamp01(c.b); ColorLowA = Mathf.Clamp01(c.a);
    }

    public Color GetColorHigh() => new(
        Mathf.Clamp01(ColorHighR), Mathf.Clamp01(ColorHighG),
        Mathf.Clamp01(ColorHighB), Mathf.Clamp01(ColorHighA));

    public void SetColorHigh(Color c) {
        ColorHighR = Mathf.Clamp01(c.r); ColorHighG = Mathf.Clamp01(c.g);
        ColorHighB = Mathf.Clamp01(c.b); ColorHighA = Mathf.Clamp01(c.a);
    }

    public Color GetPerfectColor() => new(
        Mathf.Clamp01(PerfectR), Mathf.Clamp01(PerfectG),
        Mathf.Clamp01(PerfectB), Mathf.Clamp01(PerfectA));

    public void SetPerfectColor(Color c) {
        PerfectR = Mathf.Clamp01(c.r); PerfectG = Mathf.Clamp01(c.g);
        PerfectB = Mathf.Clamp01(c.b); PerfectA = Mathf.Clamp01(c.a);
    }

    public Color GetCaptionShadowColor() => new(
        Mathf.Clamp01(CaptionShadowR), Mathf.Clamp01(CaptionShadowG),
        Mathf.Clamp01(CaptionShadowB), Mathf.Clamp01(CaptionShadowA));

    public void SetCaptionShadowColor(Color c) {
        CaptionShadowR = Mathf.Clamp01(c.r); CaptionShadowG = Mathf.Clamp01(c.g);
        CaptionShadowB = Mathf.Clamp01(c.b); CaptionShadowA = Mathf.Clamp01(c.a);
    }

    public Color GetCountShadowColor() => new(
        Mathf.Clamp01(CountShadowR), Mathf.Clamp01(CountShadowG),
        Mathf.Clamp01(CountShadowB), Mathf.Clamp01(CountShadowA));

    public void SetCountShadowColor(Color c) {
        CountShadowR = Mathf.Clamp01(c.r); CountShadowG = Mathf.Clamp01(c.g);
        CountShadowB = Mathf.Clamp01(c.b); CountShadowA = Mathf.Clamp01(c.a);
    }

    // Resolves the value color for a given count:
    //   PerfectColor (enabled, count >= ColorMax) → perfect color
    //   SolidColor                                 → low color
    //   otherwise                                  → low→high lerp by count/ColorMax
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

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(CountAuto)] = CountAuto,
            [nameof(XPerfectComboEnabled)] = XPerfectComboEnabled,
            [nameof(FontSize)] = FontSize,
            [nameof(MasterSize)] = MasterSize,
            [nameof(CaptionScale)] = CaptionScale,
            [nameof(OffsetX)] = OffsetX,
            [nameof(OffsetY)] = OffsetY,
            [nameof(ShowCaption)] = ShowCaption,
            [nameof(CaptionText)] = CaptionText,
            [nameof(CaptionOffsetY)] = CaptionOffsetY,
            [nameof(CaptionShadowEnabled)] = CaptionShadowEnabled,
            [nameof(CaptionShadowX)] = CaptionShadowX,
            [nameof(CaptionShadowY)] = CaptionShadowY,
            [nameof(CaptionShadowSoftness)] = CaptionShadowSoftness,
            [nameof(CaptionShadowR)] = CaptionShadowR,
            [nameof(CaptionShadowG)] = CaptionShadowG,
            [nameof(CaptionShadowB)] = CaptionShadowB,
            [nameof(CaptionShadowA)] = CaptionShadowA,
            [nameof(CountThickness)] = CountThickness,
            [nameof(CountShadowEnabled)] = CountShadowEnabled,
            [nameof(CountShadowX)] = CountShadowX,
            [nameof(CountShadowY)] = CountShadowY,
            [nameof(CountShadowSoftness)] = CountShadowSoftness,
            [nameof(CountShadowR)] = CountShadowR,
            [nameof(CountShadowG)] = CountShadowG,
            [nameof(CountShadowB)] = CountShadowB,
            [nameof(CountShadowA)] = CountShadowA,
            [nameof(ColorMax)] = ColorMax,
            [nameof(ColorLowR)] = ColorLowR,
            [nameof(ColorLowG)] = ColorLowG,
            [nameof(ColorLowB)] = ColorLowB,
            [nameof(ColorLowA)] = ColorLowA,
            [nameof(ColorHighR)] = ColorHighR,
            [nameof(ColorHighG)] = ColorHighG,
            [nameof(ColorHighB)] = ColorHighB,
            [nameof(ColorHighA)] = ColorHighA,
            [nameof(SolidColor)] = SolidColor,
            [nameof(PerfectColorEnabled)] = PerfectColorEnabled,
            [nameof(PerfectR)] = PerfectR,
            [nameof(PerfectG)] = PerfectG,
            [nameof(PerfectB)] = PerfectB,
            [nameof(PerfectA)] = PerfectA,
            [nameof(NoPopAnim)] = NoPopAnim,
            [nameof(CountPulseScale)] = CountPulseScale,
            [nameof(PulseDuration)] = PulseDuration,
            [nameof(LabelPulseOffsetY)] = LabelPulseOffsetY,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        CountAuto = IOUtils.Read(token, nameof(CountAuto), CountAuto);
        XPerfectComboEnabled = IOUtils.Read(token, nameof(XPerfectComboEnabled), XPerfectComboEnabled);
        FontSize = IOUtils.Read(token, nameof(FontSize), FontSize);
        MasterSize = IOUtils.Read(token, nameof(MasterSize), MasterSize);
        CaptionScale = IOUtils.Read(token, nameof(CaptionScale), CaptionScale);
        OffsetX = IOUtils.Read(token, nameof(OffsetX), OffsetX);
        OffsetY = IOUtils.Read(token, nameof(OffsetY), OffsetY);
        ShowCaption = IOUtils.Read(token, nameof(ShowCaption), ShowCaption);
        CaptionText = IOUtils.Read(token, nameof(CaptionText), CaptionText);
        CaptionOffsetY = IOUtils.Read(token, nameof(CaptionOffsetY), CaptionOffsetY);
        bool hasCaptionShadowEnabled = token?[nameof(CaptionShadowEnabled)] != null;
        bool hasCaptionShadowOffset =
            token?[nameof(CaptionShadowX)] != null || token?[nameof(CaptionShadowY)] != null;
        CaptionShadowEnabled = IOUtils.Read(token, nameof(CaptionShadowEnabled), CaptionShadowEnabled);
        CaptionShadowX = IOUtils.Read(token, nameof(CaptionShadowX), CaptionShadowX);
        CaptionShadowY = IOUtils.Read(token, nameof(CaptionShadowY), CaptionShadowY);
        CaptionShadowSoftness = IOUtils.Read(token, nameof(CaptionShadowSoftness), CaptionShadowSoftness);
        CaptionShadowR = IOUtils.Read(token, nameof(CaptionShadowR), CaptionShadowR);
        CaptionShadowG = IOUtils.Read(token, nameof(CaptionShadowG), CaptionShadowG);
        CaptionShadowB = IOUtils.Read(token, nameof(CaptionShadowB), CaptionShadowB);
        CaptionShadowA = IOUtils.Read(token, nameof(CaptionShadowA), CaptionShadowA);
        CountThickness = IOUtils.Read(token, nameof(CountThickness), CountThickness);
        bool hasCountShadowEnabled = token?[nameof(CountShadowEnabled)] != null;
        bool hasCountShadowOffset =
            token?[nameof(CountShadowX)] != null || token?[nameof(CountShadowY)] != null;
        CountShadowEnabled = IOUtils.Read(token, nameof(CountShadowEnabled), CountShadowEnabled);
        CountShadowX = IOUtils.Read(token, nameof(CountShadowX), CountShadowX);
        CountShadowY = IOUtils.Read(token, nameof(CountShadowY), CountShadowY);
        CountShadowSoftness = IOUtils.Read(token, nameof(CountShadowSoftness), CountShadowSoftness);
        CountShadowR = IOUtils.Read(token, nameof(CountShadowR), CountShadowR);
        CountShadowG = IOUtils.Read(token, nameof(CountShadowG), CountShadowG);
        CountShadowB = IOUtils.Read(token, nameof(CountShadowB), CountShadowB);
        CountShadowA = IOUtils.Read(token, nameof(CountShadowA), CountShadowA);
        ColorMax = IOUtils.Read(token, nameof(ColorMax), ColorMax);
        ColorLowR = IOUtils.Read(token, nameof(ColorLowR), ColorLowR);
        ColorLowG = IOUtils.Read(token, nameof(ColorLowG), ColorLowG);
        ColorLowB = IOUtils.Read(token, nameof(ColorLowB), ColorLowB);
        ColorLowA = IOUtils.Read(token, nameof(ColorLowA), ColorLowA);
        ColorHighR = IOUtils.Read(token, nameof(ColorHighR), ColorHighR);
        ColorHighG = IOUtils.Read(token, nameof(ColorHighG), ColorHighG);
        ColorHighB = IOUtils.Read(token, nameof(ColorHighB), ColorHighB);
        ColorHighA = IOUtils.Read(token, nameof(ColorHighA), ColorHighA);
        SolidColor = IOUtils.Read(token, nameof(SolidColor), SolidColor);
        PerfectColorEnabled = IOUtils.Read(token, nameof(PerfectColorEnabled), PerfectColorEnabled);
        PerfectR = IOUtils.Read(token, nameof(PerfectR), PerfectR);
        PerfectG = IOUtils.Read(token, nameof(PerfectG), PerfectG);
        PerfectB = IOUtils.Read(token, nameof(PerfectB), PerfectB);
        PerfectA = IOUtils.Read(token, nameof(PerfectA), PerfectA);
        NoPopAnim = IOUtils.Read(token, nameof(NoPopAnim), NoPopAnim);
        CountPulseScale = IOUtils.Read(token, nameof(CountPulseScale), CountPulseScale);
        PulseDuration = IOUtils.Read(token, nameof(PulseDuration), PulseDuration);
        LabelPulseOffsetY = IOUtils.Read(token, nameof(LabelPulseOffsetY), LabelPulseOffsetY);

        // Back-compat: migrate combo fields from Status.json naming.
        CountAuto = IOUtils.Read(token, "ComboCountAuto", CountAuto);
        Enabled = IOUtils.Read(token, "ShowCombo", Enabled);

        // Back-compat: older combo JSON had no enable fields. Treat those as
        // the new on-by-default shadow, and if the old offsets were both zero,
        // give them the visible default offset.
        if(!hasCaptionShadowEnabled) {
            CaptionShadowEnabled = true;
            if(hasCaptionShadowOffset &&
                Mathf.Abs(CaptionShadowX) <= 0.001f &&
                Mathf.Abs(CaptionShadowY) <= 0.001f
            ) {
                CaptionShadowX = 2f;
                CaptionShadowY = -2f;
            }
        }
        if(!hasCountShadowEnabled) {
            CountShadowEnabled = true;
            if(hasCountShadowOffset &&
                Mathf.Abs(CountShadowX) <= 0.001f &&
                Mathf.Abs(CountShadowY) <= 0.001f
            ) {
                CountShadowX = 2f;
                CountShadowY = -2f;
            }
        }
    }
}
