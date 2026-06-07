using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.ProgressBar;

// Persisted config for the top-of-screen Progress Bar HUD. Owns its own
// UserData/Koren/ProgressBar.json — separate from CoreSettings, per the
// per-feature-settings pattern (see project_modstate_and_settings.md).
public sealed class ProgressBarSettings : ISettingsFile {
    public bool Enabled = true;

    public float Width = 720f;
    public float Height = 9f;
    public float OffsetX = 0f;
    public float TopOffset = 10f;

    public float Rounding = 4f;
    public float OutlineThickness = 0f;

    public float FillR = 0.97f, FillG = 0.99f, FillB = 1.00f, FillA = 0.96f;
    public float BackR = 0.05f, BackG = 0.05f, BackB = 0.06f, BackA = 0.80f;
    public float OutlineColR = 0.98f, OutlineColG = 0.99f, OutlineColB = 1.00f, OutlineColA = 0.68f;

    public Color GetFillColor() => new(
        Mathf.Clamp01(FillR),
        Mathf.Clamp01(FillG),
        Mathf.Clamp01(FillB),
        Mathf.Clamp01(FillA)
    );

    public void SetFillColor(Color c) {
        FillR = Mathf.Clamp01(c.r);
        FillG = Mathf.Clamp01(c.g);
        FillB = Mathf.Clamp01(c.b);
        FillA = Mathf.Clamp01(c.a);
    }

    public Color GetBackColor() => new(
        Mathf.Clamp01(BackR),
        Mathf.Clamp01(BackG),
        Mathf.Clamp01(BackB),
        Mathf.Clamp01(BackA)
    );

    public void SetBackColor(Color c) {
        BackR = Mathf.Clamp01(c.r);
        BackG = Mathf.Clamp01(c.g);
        BackB = Mathf.Clamp01(c.b);
        BackA = Mathf.Clamp01(c.a);
    }

    public Color GetOutlineColor() => new(
        Mathf.Clamp01(OutlineColR),
        Mathf.Clamp01(OutlineColG),
        Mathf.Clamp01(OutlineColB),
        Mathf.Clamp01(OutlineColA)
    );

    public void SetOutlineColor(Color c) {
        OutlineColR = Mathf.Clamp01(c.r);
        OutlineColG = Mathf.Clamp01(c.g);
        OutlineColB = Mathf.Clamp01(c.b);
        OutlineColA = Mathf.Clamp01(c.a);
    }

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(Width)] = Width,
            [nameof(Height)] = Height,
            [nameof(OffsetX)] = OffsetX,
            [nameof(TopOffset)] = TopOffset,
            [nameof(Rounding)] = Rounding,
            [nameof(OutlineThickness)] = OutlineThickness,
            [nameof(FillR)] = FillR,
            [nameof(FillG)] = FillG,
            [nameof(FillB)] = FillB,
            [nameof(FillA)] = FillA,
            [nameof(BackR)] = BackR,
            [nameof(BackG)] = BackG,
            [nameof(BackB)] = BackB,
            [nameof(BackA)] = BackA,
            [nameof(OutlineColR)] = OutlineColR,
            [nameof(OutlineColG)] = OutlineColG,
            [nameof(OutlineColB)] = OutlineColB,
            [nameof(OutlineColA)] = OutlineColA,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        Width = IOUtils.Read(token, nameof(Width), Width);
        Height = IOUtils.Read(token, nameof(Height), Height);
        OffsetX = IOUtils.Read(token, nameof(OffsetX), OffsetX);
        TopOffset = IOUtils.Read(token, nameof(TopOffset), TopOffset);
        Rounding = IOUtils.Read(token, nameof(Rounding), Rounding);
        OutlineThickness = IOUtils.Read(token, nameof(OutlineThickness), OutlineThickness);
        FillR = IOUtils.Read(token, nameof(FillR), FillR);
        FillG = IOUtils.Read(token, nameof(FillG), FillG);
        FillB = IOUtils.Read(token, nameof(FillB), FillB);
        FillA = IOUtils.Read(token, nameof(FillA), FillA);
        BackR = IOUtils.Read(token, nameof(BackR), BackR);
        BackG = IOUtils.Read(token, nameof(BackG), BackG);
        BackB = IOUtils.Read(token, nameof(BackB), BackB);
        BackA = IOUtils.Read(token, nameof(BackA), BackA);
        OutlineColR = IOUtils.Read(token, nameof(OutlineColR), OutlineColR);
        OutlineColG = IOUtils.Read(token, nameof(OutlineColG), OutlineColG);
        OutlineColB = IOUtils.Read(token, nameof(OutlineColB), OutlineColB);
        OutlineColA = IOUtils.Read(token, nameof(OutlineColA), OutlineColA);
    }
}
