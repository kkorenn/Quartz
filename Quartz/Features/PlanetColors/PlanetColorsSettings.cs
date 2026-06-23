using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;
using UnityEngine;

namespace Quartz.Features.PlanetColors;

// Persisted config for the Planet Colors feature, ported from the original
// KorenResourcePack's ResourceChanger ball-color settings (defaults match
// v1's Settings.cs: red planets at 50% ball / 20% tail opacity). Slots are
// 0=Planet 1 (red), 1=Planet 2 (blue), 2=Planet 3 (third planet in 3+ planet
// charts). Lives in UserData/Quartz/PlanetColors.json.
public sealed class PlanetColorsSettings : ISettingsFile {
    public const int Slots = 3;

    public bool Enabled = true;

    // Off: tails reuse the ball color (with their own opacity). On: tails get
    // their own RGB per planet.
    public bool SeparateTailColor = false;

    public float[] BallR = [1f, 1f, 1f];
    public float[] BallG = [0f, 0f, 0f];
    public float[] BallB = [0f, 0f, 0f];
    public float[] BallOpacity = [0.5f, 0.5f, 0.5f];

    public float[] TailR = [1f, 1f, 1f];
    public float[] TailG = [0f, 0f, 0f];
    public float[] TailB = [0f, 0f, 0f];
    public float[] TailOpacity = [0f, 0f, 0f];

    // Ring recolor (v1 ResourceChanger ring color). Off: the ring is forced
    // transparent while planet colors are active (the vanilla ring clashes with
    // custom ball colors). On: the ring is painted this single colour instead.
    public bool EnableRingRecolor = false;
    public float RingR = 1f, RingG = 1f, RingB = 1f, RingA = 1f;

    public Color GetRingColor() => new(
        Mathf.Clamp01(RingR), Mathf.Clamp01(RingG), Mathf.Clamp01(RingB), Mathf.Clamp01(RingA)
    );

    public void SetRingRgb(Color c) {
        RingR = Mathf.Clamp01(c.r);
        RingG = Mathf.Clamp01(c.g);
        RingB = Mathf.Clamp01(c.b);
    }

    public Color GetBallColor(int slot) {
        slot = Mathf.Clamp(slot, 0, Slots - 1);
        return new Color(
            Mathf.Clamp01(BallR[slot]),
            Mathf.Clamp01(BallG[slot]),
            Mathf.Clamp01(BallB[slot]),
            Mathf.Clamp01(BallOpacity[slot])
        );
    }

    public void SetBallRgb(int slot, Color c) {
        slot = Mathf.Clamp(slot, 0, Slots - 1);
        BallR[slot] = Mathf.Clamp01(c.r);
        BallG[slot] = Mathf.Clamp01(c.g);
        BallB[slot] = Mathf.Clamp01(c.b);
    }

    public Color GetTailColor(int slot) {
        slot = Mathf.Clamp(slot, 0, Slots - 1);
        return SeparateTailColor
            ? new Color(
                Mathf.Clamp01(TailR[slot]),
                Mathf.Clamp01(TailG[slot]),
                Mathf.Clamp01(TailB[slot]),
                Mathf.Clamp01(TailOpacity[slot])
            )
            : new Color(
                Mathf.Clamp01(BallR[slot]),
                Mathf.Clamp01(BallG[slot]),
                Mathf.Clamp01(BallB[slot]),
                Mathf.Clamp01(TailOpacity[slot])
            );
    }

    public void SetTailRgb(int slot, Color c) {
        slot = Mathf.Clamp(slot, 0, Slots - 1);
        TailR[slot] = Mathf.Clamp01(c.r);
        TailG[slot] = Mathf.Clamp01(c.g);
        TailB[slot] = Mathf.Clamp01(c.b);
    }

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(SeparateTailColor)] = SeparateTailColor,
            [nameof(BallR)] = new JArray(BallR),
            [nameof(BallG)] = new JArray(BallG),
            [nameof(BallB)] = new JArray(BallB),
            [nameof(BallOpacity)] = new JArray(BallOpacity),
            [nameof(TailR)] = new JArray(TailR),
            [nameof(TailG)] = new JArray(TailG),
            [nameof(TailB)] = new JArray(TailB),
            [nameof(TailOpacity)] = new JArray(TailOpacity),
            [nameof(EnableRingRecolor)] = EnableRingRecolor,
            [nameof(RingR)] = RingR,
            [nameof(RingG)] = RingG,
            [nameof(RingB)] = RingB,
            [nameof(RingA)] = RingA,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        SeparateTailColor = IOUtils.Read(token, nameof(SeparateTailColor), SeparateTailColor);
        ReadFloats(token, nameof(BallR), BallR);
        ReadFloats(token, nameof(BallG), BallG);
        ReadFloats(token, nameof(BallB), BallB);
        ReadFloats(token, nameof(BallOpacity), BallOpacity);
        ReadFloats(token, nameof(TailR), TailR);
        ReadFloats(token, nameof(TailG), TailG);
        ReadFloats(token, nameof(TailB), TailB);
        ReadFloats(token, nameof(TailOpacity), TailOpacity);
        EnableRingRecolor = IOUtils.Read(token, nameof(EnableRingRecolor), EnableRingRecolor);
        RingR = Mathf.Clamp01(IOUtils.Read(token, nameof(RingR), RingR));
        RingG = Mathf.Clamp01(IOUtils.Read(token, nameof(RingG), RingG));
        RingB = Mathf.Clamp01(IOUtils.Read(token, nameof(RingB), RingB));
        RingA = Mathf.Clamp01(IOUtils.Read(token, nameof(RingA), RingA));
    }

    private static void ReadFloats(JToken token, string name, float[] target) {
        if(token?[name] is not JArray arr) {
            return;
        }

        for(int i = 0; i < target.Length && i < arr.Count; i++) {
            try { target[i] = Mathf.Clamp01((float)arr[i]); } catch { }
        }
    }
}
