using Newtonsoft.Json.Linq;
using Quartz.Features.Panels;
using Quartz.IO;
using Quartz.IO.Interface;
using UnityEngine;

namespace Quartz.Features.ProgressBar;

// Persisted config for the top-of-screen Progress Bar HUD. Owns its own
// UserData/Quartz/ProgressBar.json — separate from CoreSettings, per the
// per-feature-settings pattern (see project_modstate_and_settings.md).
public sealed class ProgressBarSettings : ISettingsFile {
    public bool Enabled = true;

    public float Width = 800f;
    public float Height = 8f;
    public float OffsetX = 0f;
    public float TopOffset = 10f;

    public float Rounding = 1f;
    public float OutlineThickness = 1.75f;

    // When a run starts mid-chart (checkpoint / editor play-from-tile), start
    // the bar already filled up to that point instead of empty.
    public bool PrefillStart = false;

    public float FillR = 1f, FillG = 0f, FillB = 0f, FillA = 0.96f;
    public float BackR = 0.05f, BackG = 0.05f, BackB = 0.06f, BackA = 0.80f;
    public float OutlineColR = 1f, OutlineColG = 1f, OutlineColB = 1f, OutlineColA = 1f;

    // Optional progress-driven fill gradient (v1's ProgressBarFillColor range):
    // when enabled, the fill colour is the gradient evaluated at the current
    // progress (0 = empty, 1 = complete) instead of the flat FillColor above.
    // Disabled by default so the bar keeps its solid colour. Reuses the Panels
    // StatColor gradient model.
    public StatColor FillGradient = StatColor.DefaultFor("progress");

    // Fill colour for a given progress ratio: the gradient when enabled, else
    // the flat fill colour.
    public Color GetFillColorForProgress(float progress) =>
        FillGradient is { Enabled: true } ? FillGradient.Evaluate(progress) : GetFillColor();

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
            [nameof(PrefillStart)] = PrefillStart,
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
            [nameof(FillGradient)] = FillGradient?.Serialize(),
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
        PrefillStart = IOUtils.Read(token, nameof(PrefillStart), PrefillStart);
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
        if(token[nameof(FillGradient)] is JObject fillGradient) {
            FillGradient = StatColor.Deserialize(fillGradient);
        }
    }
}
