using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;
using UnityEngine;

namespace Quartz.Features.UiHider;

// One set of hide flags. Two of these exist — Playing and Recording — and a
// shortcut flips which one is live, so a streamer can keep a "clean capture"
// profile one keypress away. Same flag set as v1's UiHidingProfile.
public sealed class UiHiderProfile {
    public bool HideEverything = false;
    public bool HideJudgment = false;
    public bool HideMissIndicators = false;
    public bool HideTitle = false;
    public bool HideOtto = false;
    public bool HideTimingTarget = false;
    public bool HideNoFailIcon = false;
    public bool HideBeta = false;
    public bool HideResult = false;
    public bool HideHitErrorMeter = false;
    public bool HideLastFloorFlash = false;

    public JToken Serialize() {
        return new JObject {
            [nameof(HideEverything)] = HideEverything,
            [nameof(HideJudgment)] = HideJudgment,
            [nameof(HideMissIndicators)] = HideMissIndicators,
            [nameof(HideTitle)] = HideTitle,
            [nameof(HideOtto)] = HideOtto,
            [nameof(HideTimingTarget)] = HideTimingTarget,
            [nameof(HideNoFailIcon)] = HideNoFailIcon,
            [nameof(HideBeta)] = HideBeta,
            [nameof(HideResult)] = HideResult,
            [nameof(HideHitErrorMeter)] = HideHitErrorMeter,
            [nameof(HideLastFloorFlash)] = HideLastFloorFlash,
        };
    }

    public void Deserialize(JToken token) {
        if(token == null) {
            return;
        }
        HideEverything = IOUtils.Read(token, nameof(HideEverything), HideEverything);
        HideJudgment = IOUtils.Read(token, nameof(HideJudgment), HideJudgment);
        HideMissIndicators = IOUtils.Read(token, nameof(HideMissIndicators), HideMissIndicators);
        HideTitle = IOUtils.Read(token, nameof(HideTitle), HideTitle);
        HideOtto = IOUtils.Read(token, nameof(HideOtto), HideOtto);
        HideTimingTarget = IOUtils.Read(token, nameof(HideTimingTarget), HideTimingTarget);
        HideNoFailIcon = IOUtils.Read(token, nameof(HideNoFailIcon), HideNoFailIcon);
        HideBeta = IOUtils.Read(token, nameof(HideBeta), HideBeta);
        HideResult = IOUtils.Read(token, nameof(HideResult), HideResult);
        HideHitErrorMeter = IOUtils.Read(token, nameof(HideHitErrorMeter), HideHitErrorMeter);
        HideLastFloorFlash = IOUtils.Read(token, nameof(HideLastFloorFlash), HideLastFloorFlash);
    }
}

// Persisted config for UI Hiding, ported from the original KorenResourcePack
// (defaults match v1's Settings.cs: off, shortcut F8, no modifier). Lives in
// UserData/Quartz/UiHider.json.
public sealed class UiHiderSettings : ISettingsFile {
    public bool Enabled = false;

    // Which profile is live; the shortcut flips this at runtime.
    public bool RecordingMode = false;

    public bool UseShortcut = true;
    public int ShortcutModifier = (int)Core.Keybind.KeyModifier.None;
    public int ShortcutKey = (int)KeyCode.F8;

    public UiHiderProfile Playing = new();
    public UiHiderProfile Recording = new();

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(RecordingMode)] = RecordingMode,
            [nameof(UseShortcut)] = UseShortcut,
            [nameof(ShortcutModifier)] = ShortcutModifier,
            [nameof(ShortcutKey)] = ShortcutKey,
            [nameof(Playing)] = Playing.Serialize(),
            [nameof(Recording)] = Recording.Serialize(),
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        RecordingMode = IOUtils.Read(token, nameof(RecordingMode), RecordingMode);
        UseShortcut = IOUtils.Read(token, nameof(UseShortcut), UseShortcut);
        ShortcutModifier = IOUtils.Read(token, nameof(ShortcutModifier), ShortcutModifier);
        ShortcutKey = IOUtils.Read(token, nameof(ShortcutKey), ShortcutKey);
        Playing.Deserialize(token?[nameof(Playing)]);
        Recording.Deserialize(token?[nameof(Recording)]);
    }
}
