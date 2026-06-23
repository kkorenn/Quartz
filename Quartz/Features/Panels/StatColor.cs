using Newtonsoft.Json.Linq;
using Quartz.IO;
using UnityEngine;

namespace Quartz.Features.Panels;

// One gradient stop: position along the stat's own 0..1 ratio -> color.
public sealed class ColorPoint {
    public float Pos = 1f;
    public float R = 1f, G = 1f, B = 1f, A = 1f;

    public ColorPoint() { }

    public ColorPoint(float pos, Color color) {
        Pos = pos;
        SetColor(color);
    }

    public Color GetColor() => new(
        Mathf.Clamp01(R), Mathf.Clamp01(G), Mathf.Clamp01(B), Mathf.Clamp01(A)
    );

    public void SetColor(Color c) {
        R = Mathf.Clamp01(c.r);
        G = Mathf.Clamp01(c.g);
        B = Mathf.Clamp01(c.b);
        A = Mathf.Clamp01(c.a);
    }

    public JToken Serialize() => new JObject {
        [nameof(Pos)] = Pos,
        [nameof(R)] = R,
        [nameof(G)] = G,
        [nameof(B)] = B,
        [nameof(A)] = A,
    };

    public static ColorPoint Deserialize(JToken token) {
        ColorPoint p = new();
        p.Pos = Mathf.Clamp01(IOUtils.Read(token, nameof(Pos), p.Pos));
        p.R = IOUtils.Read(token, nameof(R), p.R);
        p.G = IOUtils.Read(token, nameof(G), p.G);
        p.B = IOUtils.Read(token, nameof(B), p.B);
        p.A = IOUtils.Read(token, nameof(A), p.A);
        return p;
    }
}

// Per-stat value coloring, ported from v1's ColorRange (Settings.ProgressColor,
// AccuracyColor, BpmColor + BpmColorMax, ...): the stat's value text is tinted
// by lerping through gradient stops evaluated at the stat's own ratio
// (progress %, accuracy, time position, bpm / MaxBpm). The optional perfect
// color overrides everything at 100% — v1's gold accuracy.
public sealed class StatColor {
    public bool Enabled;
    public List<ColorPoint> Points = [];
    public bool UsePerfect;
    public ColorPoint Perfect = new(1f, Gold);
    // v1 Settings.BpmColorMax: TBPM/CBPM/KPS map bpm/MaxBpm onto the gradient.
    public float MaxBpm = 8000f;

    private static readonly Color Gold = new(1f, 0.854902f, 0f, 1f);

    public Color Evaluate(float ratio) {
        if(float.IsNaN(ratio) || float.IsInfinity(ratio)) {
            ratio = 0f;
        }
        ratio = Mathf.Clamp01(ratio);

        if(UsePerfect && ratio >= 0.9999f && Perfect != null) {
            return Perfect.GetColor();
        }

        // Points are kept sorted by the editor / deserializer — Evaluate runs
        // every frame, so no defensive re-sort here.
        if(Points == null || Points.Count == 0) {
            return Color.white;
        }
        if(Points.Count == 1) {
            return Points[0].GetColor();
        }

        if(ratio <= Points[0].Pos) {
            return Points[0].GetColor();
        }

        int last = Points.Count - 1;
        if(ratio >= Points[last].Pos) {
            return Points[last].GetColor();
        }

        for(int i = 1; i < Points.Count; i++) {
            ColorPoint high = Points[i];
            if(ratio > high.Pos) {
                continue;
            }

            ColorPoint low = Points[i - 1];
            float span = high.Pos - low.Pos;
            float t = span <= 0.0001f ? 0f : (ratio - low.Pos) / span;
            return Color.Lerp(low.GetColor(), high.GetColor(), t);
        }

        return Points[last].GetColor();
    }

    public void SortPoints() {
        Points ??= [];
        Points.RemoveAll(p => p == null);
        Points.Sort((a, b) => a.Pos.CompareTo(b.Pos));
    }

    // v1 defaults per stat (Settings.QuartzProgressColor / QuartzAccuracyColor /
    // QuartzBpmColor / WhiteColorRange).
    public static StatColor DefaultFor(string statId) {
        StatColor c = new();
        switch(statId) {
            case "progress":
            case "best":
            case "tbpm":
            case "cbpm":
            case "kps":
            case "autokps":
                c.Points = [
                    new ColorPoint(0f, Color.white),
                    new ColorPoint(1f, Color.red),
                ];
                break;

            case "accuracy":
            case "xaccuracy":
            case "maxaccuracy":
                c.Points = [
                    new ColorPoint(0.98f, Color.red),
                    new ColorPoint(1f, Color.white),
                ];
                c.UsePerfect = true;
                break;

            default:
                c.Points = [new ColorPoint(1f, Color.white)];
                break;
        }
        return c;
    }

    // Stats whose ratio actually moves — everything else evaluates at the top
    // of the gradient, i.e. acts as a single static color.
    public static bool HasRatio(string statId) => statId is
        "progress" or "accuracy" or "xaccuracy" or "maxaccuracy"
        or "musictime" or "maptime" or "best" or "tbpm" or "cbpm" or "kps" or "autokps";

    public static bool IsBpm(string statId) => statId is "tbpm" or "cbpm" or "kps" or "autokps";

    public JToken Serialize() {
        JArray points = [];
        foreach(ColorPoint p in Points ?? []) {
            points.Add(p.Serialize());
        }

        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(Points)] = points,
            [nameof(UsePerfect)] = UsePerfect,
            [nameof(Perfect)] = Perfect?.Serialize(),
            [nameof(MaxBpm)] = MaxBpm,
        };
    }

    public static StatColor Deserialize(JToken token) {
        StatColor c = new();
        if(token == null) {
            return c;
        }

        c.Enabled = IOUtils.Read(token, nameof(Enabled), c.Enabled);
        c.UsePerfect = IOUtils.Read(token, nameof(UsePerfect), c.UsePerfect);
        c.MaxBpm = IOUtils.Read(token, nameof(MaxBpm), c.MaxBpm);

        if(token[nameof(Perfect)] is JObject perfect) {
            c.Perfect = ColorPoint.Deserialize(perfect);
        }

        if(token[nameof(Points)] is JArray arr) {
            c.Points = [];
            foreach(JToken t in arr) {
                c.Points.Add(ColorPoint.Deserialize(t));
            }
            c.SortPoints();
        }

        return c;
    }
}
