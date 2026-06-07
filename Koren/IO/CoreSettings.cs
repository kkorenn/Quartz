using Newtonsoft.Json.Linq;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.IO;

public sealed class CoreSettings : ISettingsFile {
    public bool Active = true;
    public string Language = "en-US";
    public bool IsFirstRun = true;
    public bool ShowOnStartup = false;
    public bool Tooltip = true;
    public bool MiddleClickToDefault = true;
    public float UIScale = 1.0f;
    public string FontName = "";
    public float ScrollSpeed = 80f;

    // UI accent color (drives the whole theme via UIColors.ApplyAccent).
    // Default ff9999 (soft red).
    public float AccentR = 1.0f;
    public float AccentG = 0.6f;
    public float AccentB = 0.6f;

    public Color GetAccentColor() => new(
        Mathf.Clamp01(AccentR),
        Mathf.Clamp01(AccentG),
        Mathf.Clamp01(AccentB),
        1f
    );

    public void SetAccentColor(Color color) {
        AccentR = Mathf.Clamp01(color.r);
        AccentG = Mathf.Clamp01(color.g);
        AccentB = Mathf.Clamp01(color.b);
    }

    public JToken Serialize() {
        return new JObject {
            [nameof(Active)] = Active,
            [nameof(Language)] = Language,
            [nameof(IsFirstRun)] = IsFirstRun,
            [nameof(ShowOnStartup)] = ShowOnStartup,
            [nameof(Tooltip)] = Tooltip,
            [nameof(MiddleClickToDefault)] = MiddleClickToDefault,
            [nameof(UIScale)] = UIScale,
            [nameof(FontName)] = FontName,
            [nameof(ScrollSpeed)] = ScrollSpeed,
            [nameof(AccentR)] = AccentR,
            [nameof(AccentG)] = AccentG,
            [nameof(AccentB)] = AccentB
        };
    }

    public void Deserialize(JToken token) {
        Active = IOUtils.Read(token, nameof(Active), Active);
        Language = IOUtils.Read(token, nameof(Language), Language);
        IsFirstRun = IOUtils.Read(token, nameof(IsFirstRun), IsFirstRun);
        ShowOnStartup = IOUtils.Read(token, nameof(ShowOnStartup), ShowOnStartup);
        Tooltip = IOUtils.Read(token, nameof(Tooltip), Tooltip);
        MiddleClickToDefault = IOUtils.Read(token, nameof(MiddleClickToDefault), MiddleClickToDefault);
        UIScale = IOUtils.Read(token, nameof(UIScale), UIScale);
        FontName = IOUtils.Read(token, nameof(FontName), FontName);
        ScrollSpeed = IOUtils.Read(token, nameof(ScrollSpeed), ScrollSpeed);
        AccentR = IOUtils.Read(token, nameof(AccentR), AccentR);
        AccentG = IOUtils.Read(token, nameof(AccentG), AccentG);
        AccentB = IOUtils.Read(token, nameof(AccentB), AccentB);
    }
}
