using Newtonsoft.Json.Linq;
using Koren.Core;
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
    public float UIScale = 0.85f;
    public string FontName = "";

    // Apply FontName to A Dance of Fire and Ice's own in-game overlay (the
    // scrHUDText title/artist HUD shown during play), not just the mod's UI.
    // "Default (SUIT)" has no standalone font file, so it leaves the game's
    // own localized font in place.
    public bool ApplyFontToGameOverlay = true;

    // Font for the in-game overlay when ApplyFontToGameOverlay is on. Empty means
    // "follow the mod's overlay font (FontName)"; otherwise a font display name
    // from the same picker, so the in-game text can use a different face.
    public string GameOverlayFontName = "";

    public float ScrollSpeed = 80f;

    // Settings-window opacity (0..1). Default fully opaque (shown as 100%).
    public float PanelOpacity = 1.0f;

    // User-resized settings-window size, in panel-local (reference) units. 0 =
    // unset → use the screen-derived default. Restored (clamped to screen/min)
    // on next launch so a resize persists across sessions.
    public float PanelWidth = 0f;
    public float PanelHeight = 0f;

    public Dictionary<string, bool> CollapsibleStates = [];

    // Menu toggle keybind, stored as ints (Keybind.KeyModifier and KeyCode).
    // Default Alt + K (shown as Option + K on macOS).
    public int ToggleModifier = (int)Keybind.KeyModifier.Alt;
    public int ToggleKey = (int)KeyCode.K;

    // Release channel to accept updates from. Lower (less stable) channels
    // include all higher ones: Alpha gets every build, Stable only final
    // releases. Defaults to Alpha for the current alpha test phase.
    public int UpdateChannel = (int)ReleaseChannel.Alpha;

    // Version tag the user chose to skip (e.g. "v2.0.0-alpha-2"). That build
    // won't be offered again; a newer one still will.
    public string SkippedVersion = "";

    public ReleaseChannel GetUpdateChannel() => (ReleaseChannel)UpdateChannel;

    // Whether a build published on `remote` should be offered to this user —
    // the chosen channel accepts itself and everything more stable.
    public bool AcceptsChannel(ReleaseChannel remote) => remote >= GetUpdateChannel();

    // UI accent color (drives the whole theme via UIColors.ApplyAccent).
    // Default ff9999 (soft red).
    public float AccentR = 1.0f;
    public float AccentG = 0.5995077f;
    public float AccentB = 0.5995077f;

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

    public bool GetCollapsibleExpanded(string key)
        => CollapsibleStates.TryGetValue(key, out bool expanded) && expanded;

    public void SetCollapsibleExpanded(string key, bool expanded)
        => CollapsibleStates[key] = expanded;

    public JToken Serialize() {
        JObject collapsibleStates = [];
        foreach(var kvp in CollapsibleStates) {
            collapsibleStates[kvp.Key] = kvp.Value;
        }

        return new JObject {
            [nameof(Active)] = Active,
            [nameof(Language)] = Language,
            [nameof(IsFirstRun)] = IsFirstRun,
            [nameof(ShowOnStartup)] = ShowOnStartup,
            [nameof(Tooltip)] = Tooltip,
            [nameof(MiddleClickToDefault)] = MiddleClickToDefault,
            [nameof(UIScale)] = UIScale,
            [nameof(FontName)] = FontName,
            [nameof(ApplyFontToGameOverlay)] = ApplyFontToGameOverlay,
            [nameof(GameOverlayFontName)] = GameOverlayFontName,
            [nameof(ScrollSpeed)] = ScrollSpeed,
            [nameof(PanelOpacity)] = PanelOpacity,
            [nameof(PanelWidth)] = PanelWidth,
            [nameof(PanelHeight)] = PanelHeight,
            [nameof(ToggleModifier)] = ToggleModifier,
            [nameof(ToggleKey)] = ToggleKey,
            [nameof(UpdateChannel)] = UpdateChannel,
            [nameof(SkippedVersion)] = SkippedVersion,
            [nameof(CollapsibleStates)] = collapsibleStates,
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
        ApplyFontToGameOverlay = IOUtils.Read(token, nameof(ApplyFontToGameOverlay), ApplyFontToGameOverlay);
        GameOverlayFontName = IOUtils.Read(token, nameof(GameOverlayFontName), GameOverlayFontName);
        ScrollSpeed = IOUtils.Read(token, nameof(ScrollSpeed), ScrollSpeed);
        PanelOpacity = IOUtils.Read(token, nameof(PanelOpacity), PanelOpacity);
        PanelWidth = IOUtils.Read(token, nameof(PanelWidth), PanelWidth);
        PanelHeight = IOUtils.Read(token, nameof(PanelHeight), PanelHeight);
        ToggleModifier = IOUtils.Read(token, nameof(ToggleModifier), ToggleModifier);
        ToggleKey = IOUtils.Read(token, nameof(ToggleKey), ToggleKey);
        UpdateChannel = IOUtils.Read(token, nameof(UpdateChannel), UpdateChannel);
        SkippedVersion = IOUtils.Read(token, nameof(SkippedVersion), SkippedVersion);
        CollapsibleStates.Clear();
        if(token[nameof(CollapsibleStates)] is JObject collapsibleStates) {
            foreach(var prop in collapsibleStates.Properties()) {
                try {
                    CollapsibleStates[prop.Name] = prop.Value.Value<bool>();
                } catch {
                }
            }
        }
        AccentR = IOUtils.Read(token, nameof(AccentR), AccentR);
        AccentG = IOUtils.Read(token, nameof(AccentG), AccentG);
        AccentB = IOUtils.Read(token, nameof(AccentB), AccentB);
    }
}
