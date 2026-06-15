using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;

namespace Koren.Features.Judgement;

// Persisted config for the judgement popup hider. Defaults match v1's
// Settings.cs: enabled, hiding the Perfect popup — including XPerfect's X / + / -
// split of it, so toggling XPerfect on doesn't suddenly un-hide perfects.
public sealed class JudgementPopupHiderSettings : ISettingsFile {
    public bool Enabled = true;
    public int HiddenMask = (1 << (int)HitMargin.Perfect)
        | (1 << JudgementPopupHider.XPerfectPerfectBit)
        | (1 << JudgementPopupHider.PlusPerfectBit)
        | (1 << JudgementPopupHider.MinusPerfectBit);

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(HiddenMask)] = HiddenMask,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        HiddenMask = IOUtils.Read(token, nameof(HiddenMask), HiddenMask);
    }
}
