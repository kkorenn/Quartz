using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;

namespace Quartz.Features.Judgement;

// Persisted config for the judgement popup hider. Default hides only the
// XPerfect X (dead-center) popup — bit 12 — matching the shipped default setup;
// every other judgement popup shows.
public sealed class JudgementPopupHiderSettings : ISettingsFile {
    public bool Enabled = true;
    public int HiddenMask = 1 << JudgementPopupHider.XPerfectPerfectBit;

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
