using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.Status;

// Persisted config for the Status HUD. Lives in its own JSON
// (UserData/Koren/Status.json), separate from the core CoreSettings.
public sealed class StatusSettings : ISettingsFile {
    public bool Enabled = true;

    public bool ShowProgress = true;
    public bool ShowAccuracy = false;
    public bool ShowXAccuracy = true;
    public bool ShowMaxXAccuracy = true;

    public string Prefix = "";
    public int Decimals = 2;
    public float FontSize = 22f;
    public bool BackgroundEnabled = true;

    public float TextR = 1f;
    public float TextG = 1f;
    public float TextB = 1f;
    public float TextA = 1f;

    public float PosX = 24f;
    public float PosY = -24f;

    public Color GetTextColor() => new(
        Mathf.Clamp01(TextR),
        Mathf.Clamp01(TextG),
        Mathf.Clamp01(TextB),
        Mathf.Clamp01(TextA)
    );

    public void SetTextColor(Color c) {
        TextR = Mathf.Clamp01(c.r);
        TextG = Mathf.Clamp01(c.g);
        TextB = Mathf.Clamp01(c.b);
        TextA = Mathf.Clamp01(c.a);
    }

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(ShowProgress)] = ShowProgress,
            [nameof(ShowAccuracy)] = ShowAccuracy,
            [nameof(ShowXAccuracy)] = ShowXAccuracy,
            [nameof(ShowMaxXAccuracy)] = ShowMaxXAccuracy,
            [nameof(Prefix)] = Prefix,
            [nameof(Decimals)] = Decimals,
            [nameof(FontSize)] = FontSize,
            [nameof(BackgroundEnabled)] = BackgroundEnabled,
            [nameof(TextR)] = TextR,
            [nameof(TextG)] = TextG,
            [nameof(TextB)] = TextB,
            [nameof(TextA)] = TextA,
            [nameof(PosX)] = PosX,
            [nameof(PosY)] = PosY
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        ShowProgress = IOUtils.Read(token, nameof(ShowProgress), ShowProgress);
        ShowAccuracy = IOUtils.Read(token, nameof(ShowAccuracy), ShowAccuracy);
        ShowXAccuracy = IOUtils.Read(token, nameof(ShowXAccuracy), ShowXAccuracy);
        ShowMaxXAccuracy = IOUtils.Read(token, nameof(ShowMaxXAccuracy), ShowMaxXAccuracy);
        Prefix = IOUtils.Read(token, nameof(Prefix), Prefix);
        Decimals = IOUtils.Read(token, nameof(Decimals), Decimals);
        FontSize = IOUtils.Read(token, nameof(FontSize), FontSize);
        BackgroundEnabled = IOUtils.Read(token, nameof(BackgroundEnabled), BackgroundEnabled);
        TextR = IOUtils.Read(token, nameof(TextR), TextR);
        TextG = IOUtils.Read(token, nameof(TextG), TextG);
        TextB = IOUtils.Read(token, nameof(TextB), TextB);
        TextA = IOUtils.Read(token, nameof(TextA), TextA);
        PosX = IOUtils.Read(token, nameof(PosX), PosX);
        PosY = IOUtils.Read(token, nameof(PosY), PosY);
    }
}
