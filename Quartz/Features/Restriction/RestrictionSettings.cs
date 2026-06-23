using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;

namespace Quartz.Features.Restriction;

// Persisted config for Judgement Restriction + Death Limit, ported from the
// original KorenResourcePack (defaults match v1's Settings.cs). The break
// messages are new in v2: they're what the fail screen shows when the
// restriction kills the run (v1 hardcoded these strings). Lives in
// UserData/Quartz/Restriction.json.
public sealed class RestrictionSettings : ISettingsFile {
    // === Judgement Restriction ===
    // Modes keep v1's numeric values: 0 = minimum accuracy, 1 = Pure Perfect
    // only, 3 = custom allowed-judgement mask, 4 = no Too Early. (v1's mode 2
    // was "XPure Perfect", tied to the XPerfect mod — not ported.)
    public bool JRestrictEnabled = false;
    public int JRestrictMode = 1;
    public float JRestrictAccuracy = 96.6741943f;
    public int JRestrictAllowedMask = 0;
    // {judgement} is replaced at fail time with the judgement that broke the
    // restriction (see Restriction.FormatJrMessage).
    public string JRestrictMessage = "Broke the judgement restriction!!";

    // === Death Limit ===
    public bool DeathLimitEnabled = false;
    public bool MaxDeathsOn = true;
    public int MaxDeaths = 10;
    public bool MaxMissesOn = false;
    public int MaxMisses = 3;
    public bool MaxOverloadsOn = false;
    public int MaxOverloads = 3;
    public string DeathLimitMessage = "Exceeded death limit!!";

    public JToken Serialize() {
        return new JObject {
            [nameof(JRestrictEnabled)] = JRestrictEnabled,
            [nameof(JRestrictMode)] = JRestrictMode,
            [nameof(JRestrictAccuracy)] = JRestrictAccuracy,
            [nameof(JRestrictAllowedMask)] = JRestrictAllowedMask,
            [nameof(JRestrictMessage)] = JRestrictMessage,
            [nameof(DeathLimitEnabled)] = DeathLimitEnabled,
            [nameof(MaxDeathsOn)] = MaxDeathsOn,
            [nameof(MaxDeaths)] = MaxDeaths,
            [nameof(MaxMissesOn)] = MaxMissesOn,
            [nameof(MaxMisses)] = MaxMisses,
            [nameof(MaxOverloadsOn)] = MaxOverloadsOn,
            [nameof(MaxOverloads)] = MaxOverloads,
            [nameof(DeathLimitMessage)] = DeathLimitMessage,
        };
    }

    public void Deserialize(JToken token) {
        JRestrictEnabled = IOUtils.Read(token, nameof(JRestrictEnabled), JRestrictEnabled);
        JRestrictMode = IOUtils.Read(token, nameof(JRestrictMode), JRestrictMode);
        JRestrictAccuracy = IOUtils.Read(token, nameof(JRestrictAccuracy), JRestrictAccuracy);
        JRestrictAllowedMask = IOUtils.Read(token, nameof(JRestrictAllowedMask), JRestrictAllowedMask);
        JRestrictMessage = IOUtils.Read(token, nameof(JRestrictMessage), JRestrictMessage);
        DeathLimitEnabled = IOUtils.Read(token, nameof(DeathLimitEnabled), DeathLimitEnabled);
        MaxDeathsOn = IOUtils.Read(token, nameof(MaxDeathsOn), MaxDeathsOn);
        MaxDeaths = IOUtils.Read(token, nameof(MaxDeaths), MaxDeaths);
        MaxMissesOn = IOUtils.Read(token, nameof(MaxMissesOn), MaxMissesOn);
        MaxMisses = IOUtils.Read(token, nameof(MaxMisses), MaxMisses);
        MaxOverloadsOn = IOUtils.Read(token, nameof(MaxOverloadsOn), MaxOverloadsOn);
        MaxOverloads = IOUtils.Read(token, nameof(MaxOverloads), MaxOverloads);
        DeathLimitMessage = IOUtils.Read(token, nameof(DeathLimitMessage), DeathLimitMessage);
    }
}
