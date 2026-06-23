using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;
using UnityEngine;

namespace Quartz.Features.OttoIcon;

// Persisted config for the Otto icon swap, ported from the original
// KorenResourcePack's ResourceChanger ChangeOttoIcon settings (defaults match
// v1's Settings.cs: on, red, offset (-10, 5)). Lives in
// UserData/Quartz/OttoIcon.json.
public sealed class OttoIconSettings : ISettingsFile {
    public bool Enabled = true;

    public float R = 1f;
    public float G = 0f;
    public float B = 0f;
    public float A = 1f;

    // Optional separate tint while the level's top BPM is 300+ (the state
    // where vanilla turns Otto red). Defaults to vanilla's red.
    public bool UseHighBpmColor = false;

    public float HighBpmR = 1f;
    public float HighBpmG = 0f;
    public float HighBpmB = 0f;
    public float HighBpmA = 1f;

    public float OffsetX = -10f;
    public float OffsetY = 5f;

    public Color GetColor() => new(
        Mathf.Clamp01(R),
        Mathf.Clamp01(G),
        Mathf.Clamp01(B),
        Mathf.Clamp01(A)
    );

    public void SetColor(Color c) {
        R = Mathf.Clamp01(c.r);
        G = Mathf.Clamp01(c.g);
        B = Mathf.Clamp01(c.b);
        A = Mathf.Clamp01(c.a);
    }

    public Color GetHighBpmColor() => new(
        Mathf.Clamp01(HighBpmR),
        Mathf.Clamp01(HighBpmG),
        Mathf.Clamp01(HighBpmB),
        Mathf.Clamp01(HighBpmA)
    );

    public void SetHighBpmColor(Color c) {
        HighBpmR = Mathf.Clamp01(c.r);
        HighBpmG = Mathf.Clamp01(c.g);
        HighBpmB = Mathf.Clamp01(c.b);
        HighBpmA = Mathf.Clamp01(c.a);
    }

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(R)] = R,
            [nameof(G)] = G,
            [nameof(B)] = B,
            [nameof(A)] = A,
            [nameof(UseHighBpmColor)] = UseHighBpmColor,
            [nameof(HighBpmR)] = HighBpmR,
            [nameof(HighBpmG)] = HighBpmG,
            [nameof(HighBpmB)] = HighBpmB,
            [nameof(HighBpmA)] = HighBpmA,
            [nameof(OffsetX)] = OffsetX,
            [nameof(OffsetY)] = OffsetY,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        R = IOUtils.Read(token, nameof(R), R);
        G = IOUtils.Read(token, nameof(G), G);
        B = IOUtils.Read(token, nameof(B), B);
        A = IOUtils.Read(token, nameof(A), A);
        UseHighBpmColor = IOUtils.Read(token, nameof(UseHighBpmColor), UseHighBpmColor);
        HighBpmR = IOUtils.Read(token, nameof(HighBpmR), HighBpmR);
        HighBpmG = IOUtils.Read(token, nameof(HighBpmG), HighBpmG);
        HighBpmB = IOUtils.Read(token, nameof(HighBpmB), HighBpmB);
        HighBpmA = IOUtils.Read(token, nameof(HighBpmA), HighBpmA);
        OffsetX = IOUtils.Read(token, nameof(OffsetX), OffsetX);
        OffsetY = IOUtils.Read(token, nameof(OffsetY), OffsetY);
    }
}
