using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;

namespace Koren.Features.ChatterBlocker;

// Persisted config for the Keyboard Chatter Blocker (defaults match v1's
// Settings.cs: on, 35 ms threshold). Lives in UserData/Koren/ChatterBlocker.json.
public sealed class ChatterBlockerSettings : ISettingsFile {
    public bool Enabled = true;
    public float ThresholdMs = 35f;

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(ThresholdMs)] = ThresholdMs,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        ThresholdMs = IOUtils.Read(token, nameof(ThresholdMs), ThresholdMs);
    }
}
