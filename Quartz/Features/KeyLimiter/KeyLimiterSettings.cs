using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;

namespace Quartz.Features.KeyLimiter;

// Persisted config for the Key Limiter, ported from the original
// KorenResourcePack (defaults match v1's Settings.cs — its default allowed
// set: Q 3 4 T O - = \ Space B H , A LShift RShift Return). Mouse buttons are
// always allowed and never stored. Lives in UserData/Quartz/KeyLimiter.json.
public sealed class KeyLimiterSettings : ISettingsFile {
    public bool Enabled = true;

    public int[] AllowedKeys = [
        113, 51, 52, 116, 111, 45, 61, 92,
        32, 98, 104, 46, 97, 304, 273, 13,
    ];

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(AllowedKeys)] = new JArray(AllowedKeys),
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);

        if(token?[nameof(AllowedKeys)] is JArray arr) {
            List<int> keys = [];
            foreach(JToken t in arr) {
                try { keys.Add((int)t); } catch { }
            }
            AllowedKeys = [.. keys];
        }
    }
}
