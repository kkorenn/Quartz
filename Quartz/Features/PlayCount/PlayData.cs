using Newtonsoft.Json.Linq;

namespace Quartz.Features.PlayCount;

// Per-map persisted counters. Lives inside PlayCount's dictionary; the
// dictionary itself is serialized atomically to PlayCount.json on demand.
//
// Simpler than the original pack: no start-progress / multiplier bucketing,
// just a single rolling best and a lifetime attempt count. Layered features
// (best-from-this-checkpoint, multiplier-aware best) can be re-introduced
// later without breaking this shape.
public sealed class PlayData {
    public float BestProgress;
    // Where the best run started (0 = first tile) so the HUD can show
    // checkpoint bests as a "start - best" range instead of a bare number.
    public float BestStartProgress;
    public int TotalAttempts;

    public JObject Serialize() {
        return new JObject {
            [nameof(BestProgress)] = BestProgress,
            [nameof(BestStartProgress)] = BestStartProgress,
            [nameof(TotalAttempts)] = TotalAttempts,
        };
    }

    public static PlayData Deserialize(JToken token) {
        PlayData data = new();
        if(token is JObject obj) {
            data.BestProgress = obj.Value<float?>(nameof(BestProgress)) ?? 0f;
            data.BestStartProgress = obj.Value<float?>(nameof(BestStartProgress)) ?? 0f;
            data.TotalAttempts = obj.Value<int?>(nameof(TotalAttempts)) ?? 0;
        }
        return data;
    }
}
