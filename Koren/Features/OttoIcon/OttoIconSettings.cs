using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.OttoIcon;

// Persisted config for the Otto icon swap, ported from the original
// KorenResourcePack's ResourceChanger ChangeOttoIcon settings (defaults match
// v1's Settings.cs: on, red, offset (-10, 5)). Lives in
// UserData/Koren/OttoIcon.json.
public sealed class OttoIconSettings : ISettingsFile {
    public bool Enabled = true;

    public float R = 1f;
    public float G = 0f;
    public float B = 0f;
    public float A = 1f;

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

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(R)] = R,
            [nameof(G)] = G,
            [nameof(B)] = B,
            [nameof(A)] = A,
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
        OffsetX = IOUtils.Read(token, nameof(OffsetX), OffsetX);
        OffsetY = IOUtils.Read(token, nameof(OffsetY), OffsetY);
    }
}
