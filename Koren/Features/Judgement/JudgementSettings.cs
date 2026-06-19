using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.Judgement;

// Persisted config for the judgement counts overlay (top-center row of
// per-judgement hit counts). Ranges match the original KorenResourcePack:
// OffsetY -100..200, Size 0.3..3, Spacing -20..80. OffsetX is new in v2 —
// the overlay is draggable in Reorganize mode like the other HUD elements.
public sealed class JudgementSettings : ISettingsFile {
    public bool Enabled = true;

    // When the XPerfect mod is active, split the Perfect slot into
    // +Perfect / X / -Perfect. Off collapses it back to a single combined
    // Perfect count even while XPerfect is installed.
    public bool ShowXPerfect = true;

    public float OffsetX = 0f;
    public float OffsetY = -5f;
    public float Size = 0.9f;
    public float Spacing = 5f;

    public bool TextShadowEnabled = true;
    public float TextShadowX = 1.5f;
    public float TextShadowY = -1.5f;
    public float TextShadowSoftness = 0f;
    public float TextShadowR = 0f, TextShadowG = 0f, TextShadowB = 0f, TextShadowA = 0.5019608f;

    public Color GetTextShadowColor() => new(
        Mathf.Clamp01(TextShadowR),
        Mathf.Clamp01(TextShadowG),
        Mathf.Clamp01(TextShadowB),
        Mathf.Clamp01(TextShadowA)
    );

    public void SetTextShadowColor(Color c) {
        TextShadowR = Mathf.Clamp01(c.r);
        TextShadowG = Mathf.Clamp01(c.g);
        TextShadowB = Mathf.Clamp01(c.b);
        TextShadowA = Mathf.Clamp01(c.a);
    }

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(ShowXPerfect)] = ShowXPerfect,
            [nameof(OffsetX)] = OffsetX,
            [nameof(OffsetY)] = OffsetY,
            [nameof(Size)] = Size,
            [nameof(Spacing)] = Spacing,
            [nameof(TextShadowEnabled)] = TextShadowEnabled,
            [nameof(TextShadowX)] = TextShadowX,
            [nameof(TextShadowY)] = TextShadowY,
            [nameof(TextShadowSoftness)] = TextShadowSoftness,
            [nameof(TextShadowR)] = TextShadowR,
            [nameof(TextShadowG)] = TextShadowG,
            [nameof(TextShadowB)] = TextShadowB,
            [nameof(TextShadowA)] = TextShadowA,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        ShowXPerfect = IOUtils.Read(token, nameof(ShowXPerfect), ShowXPerfect);
        OffsetX = IOUtils.Read(token, nameof(OffsetX), OffsetX);
        OffsetY = IOUtils.Read(token, nameof(OffsetY), OffsetY);
        Size = IOUtils.Read(token, nameof(Size), Size);
        Spacing = IOUtils.Read(token, nameof(Spacing), Spacing);
        TextShadowEnabled = IOUtils.Read(token, nameof(TextShadowEnabled), TextShadowEnabled);
        TextShadowX = IOUtils.Read(token, nameof(TextShadowX), TextShadowX);
        TextShadowY = IOUtils.Read(token, nameof(TextShadowY), TextShadowY);
        TextShadowSoftness = IOUtils.Read(token, nameof(TextShadowSoftness), TextShadowSoftness);
        TextShadowR = IOUtils.Read(token, nameof(TextShadowR), TextShadowR);
        TextShadowG = IOUtils.Read(token, nameof(TextShadowG), TextShadowG);
        TextShadowB = IOUtils.Read(token, nameof(TextShadowB), TextShadowB);
        TextShadowA = IOUtils.Read(token, nameof(TextShadowA), TextShadowA);
    }
}
