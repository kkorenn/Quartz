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
    public bool BlockMouseWheelScrollWhilePlaying = true;
    public bool DisableMenuMusic = true;

    // Custom main-menu BPM: the menu's rabbit floor toggles the planet speed
    // between slow and fast. When enabled, those two states run at these BPMs
    // instead of the authored 1x / 2x. Off by default — leaves the menu alone.
    public bool MenuBpmEnabled = false;
    public float MenuSlowBpm = 100f;
    public float MenuHighBpm = 200f;

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
            [nameof(MenuBpmEnabled)] = MenuBpmEnabled,
            [nameof(MenuSlowBpm)] = MenuSlowBpm,
            [nameof(MenuHighBpm)] = MenuHighBpm,
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
        MenuBpmEnabled = IOUtils.Read(token, nameof(MenuBpmEnabled), MenuBpmEnabled);
        MenuSlowBpm = IOUtils.Read(token, nameof(MenuSlowBpm), MenuSlowBpm);
        MenuHighBpm = IOUtils.Read(token, nameof(MenuHighBpm), MenuHighBpm);
        HideResultXAccuracy = IOUtils.Read(token, nameof(HideResultXAccuracy), HideResultXAccuracy);
        HideResultAccuracy = IOUtils.Read(token, nameof(HideResultAccuracy), HideResultAccuracy);
        HideResultCheckpoints = IOUtils.Read(token, nameof(HideResultCheckpoints), HideResultCheckpoints);
        HideResultMaximumUsedKeys = IOUtils.Read(token, nameof(HideResultMaximumUsedKeys), HideResultMaximumUsedKeys);
    }
}
