using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;

namespace Koren.Features.AutoDeafen;

// Persisted config for Auto Deafen, ported from the original
// KorenResourcePack (defaults match v1's Settings.cs). The access token is
// the user's own Discord OAuth token for their own Discord application —
// stored locally like v1 did. Lives in UserData/Koren/AutoDeafen.json.
public sealed class AutoDeafenSettings : ISettingsFile {
    public bool Enabled = false;
    public float DeafenAtPercent = 5f;
    public bool OnlyFromStart = true;

    public string DiscordClientId = "";
    public string DiscordAccessToken = "";

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(DeafenAtPercent)] = DeafenAtPercent,
            [nameof(OnlyFromStart)] = OnlyFromStart,
            [nameof(DiscordClientId)] = DiscordClientId,
            [nameof(DiscordAccessToken)] = DiscordAccessToken,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        DeafenAtPercent = IOUtils.Read(token, nameof(DeafenAtPercent), DeafenAtPercent);
        OnlyFromStart = IOUtils.Read(token, nameof(OnlyFromStart), OnlyFromStart);
        DiscordClientId = IOUtils.Read(token, nameof(DiscordClientId), DiscordClientId);
        DiscordAccessToken = IOUtils.Read(token, nameof(DiscordAccessToken), DiscordAccessToken);
    }
}
