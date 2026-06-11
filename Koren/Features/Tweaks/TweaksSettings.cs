using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;

namespace Koren.Features.Tweaks;

// Persisted config for the Tweaks feature, ported field-for-field from the
// original KorenResourcePack (defaults match v1's Settings.cs). Lives in
// UserData/Koren/Tweaks.json.
public sealed class TweaksSettings : ISettingsFile {
    // === Visual tweaks (Visuals tab) ===
    public bool RemoveAllCheckpoints = true;
    public bool RemoveBallCoreParticles = true;
    public bool DisableTileHitGlow = true;
    public bool RemovePlanetGlow = true;

    // === Behavior tweaks (Tweaks tab) ===
    public bool DisableAutoPause = true;
    public bool BlockMouseWheelScrollWhilePlaying = false;
    public bool DisableMenuMusic = false;

    // === Detailed results tweaks (Tweaks tab) ===
    public bool HideResultXAccuracy = true;
    public bool HideResultAccuracy = true;
    public bool HideResultCheckpoints = true;
    public bool HideResultMaximumUsedKeys = true;

    public JToken Serialize() {
        return new JObject {
            [nameof(RemoveAllCheckpoints)] = RemoveAllCheckpoints,
            [nameof(RemoveBallCoreParticles)] = RemoveBallCoreParticles,
            [nameof(DisableTileHitGlow)] = DisableTileHitGlow,
            [nameof(RemovePlanetGlow)] = RemovePlanetGlow,
            [nameof(DisableAutoPause)] = DisableAutoPause,
            [nameof(BlockMouseWheelScrollWhilePlaying)] = BlockMouseWheelScrollWhilePlaying,
            [nameof(DisableMenuMusic)] = DisableMenuMusic,
            [nameof(HideResultXAccuracy)] = HideResultXAccuracy,
            [nameof(HideResultAccuracy)] = HideResultAccuracy,
            [nameof(HideResultCheckpoints)] = HideResultCheckpoints,
            [nameof(HideResultMaximumUsedKeys)] = HideResultMaximumUsedKeys,
        };
    }

    public void Deserialize(JToken token) {
        RemoveAllCheckpoints = IOUtils.Read(token, nameof(RemoveAllCheckpoints), RemoveAllCheckpoints);
        RemoveBallCoreParticles = IOUtils.Read(token, nameof(RemoveBallCoreParticles), RemoveBallCoreParticles);
        DisableTileHitGlow = IOUtils.Read(token, nameof(DisableTileHitGlow), DisableTileHitGlow);
        RemovePlanetGlow = IOUtils.Read(token, nameof(RemovePlanetGlow), RemovePlanetGlow);
        DisableAutoPause = IOUtils.Read(token, nameof(DisableAutoPause), DisableAutoPause);
        BlockMouseWheelScrollWhilePlaying = IOUtils.Read(token, nameof(BlockMouseWheelScrollWhilePlaying), BlockMouseWheelScrollWhilePlaying);
        DisableMenuMusic = IOUtils.Read(token, nameof(DisableMenuMusic), DisableMenuMusic);
        HideResultXAccuracy = IOUtils.Read(token, nameof(HideResultXAccuracy), HideResultXAccuracy);
        HideResultAccuracy = IOUtils.Read(token, nameof(HideResultAccuracy), HideResultAccuracy);
        HideResultCheckpoints = IOUtils.Read(token, nameof(HideResultCheckpoints), HideResultCheckpoints);
        HideResultMaximumUsedKeys = IOUtils.Read(token, nameof(HideResultMaximumUsedKeys), HideResultMaximumUsedKeys);
    }
}
