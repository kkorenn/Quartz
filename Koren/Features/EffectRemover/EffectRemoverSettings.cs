using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;

namespace Koren.Features.EffectRemover;

// Persisted config for the Effect Remover, ported field-for-field from the
// original KorenResourcePack (defaults match v1's Settings.cs). Lives in
// UserData/Koren/EffectRemover.json.
public sealed class EffectRemoverSettings : ISettingsFile {
    public bool On = true;

    // Editor: while the remover is on, saving a chart would write the
    // stripped level over the original — saving is blocked unless this is
    // explicitly enabled.
    public bool EnableSave = false;

    // === Non-DLC events ===
    public bool Filters = true;
    public bool AdvancedFilters = true;
    public bool Particles = false;
    public bool Decorations = false;
    public bool Backgrounds = false;
    public bool Cameras = false;
    public bool RepeatEvents = false;
    public bool FrameRate = true;
    public bool HitSounds = true;

    // === Planet events ===
    public bool PlanetOrbit = false;
    public bool PlanetScale = false;
    public bool PlanetRadius = false;

    // === Track events ===
    public bool TrackAnimations = false;
    public bool TrackPositions = false;
    public bool TrackMoves = false;
    public bool TrackColors = false;

    // === DLC events ===
    public bool HoldSounds = true;
    public bool HideIcons = true;

    // === Misc ===
    public bool RemoveAllDecorations = true;
    // Background sub-option (shown only when Backgrounds is on): also hide the
    // default/tutorial background's tiled pattern. Its pulsing shapes are
    // always disabled whenever Backgrounds is on.
    public bool RemoveTutorialPatterns = true;
    public bool LimitTrackOpacity = true;
    public bool SetCameraZoom = false;
    public float CameraZoomScale = 250f;
    public bool ResetTrackAnimation = true;
    public bool ResetTrackColor = true;

    public JToken Serialize() {
        return new JObject {
            [nameof(On)] = On,
            [nameof(EnableSave)] = EnableSave,
            [nameof(Filters)] = Filters,
            [nameof(AdvancedFilters)] = AdvancedFilters,
            [nameof(Particles)] = Particles,
            [nameof(Decorations)] = Decorations,
            [nameof(Backgrounds)] = Backgrounds,
            [nameof(Cameras)] = Cameras,
            [nameof(RepeatEvents)] = RepeatEvents,
            [nameof(FrameRate)] = FrameRate,
            [nameof(HitSounds)] = HitSounds,
            [nameof(PlanetOrbit)] = PlanetOrbit,
            [nameof(PlanetScale)] = PlanetScale,
            [nameof(PlanetRadius)] = PlanetRadius,
            [nameof(TrackAnimations)] = TrackAnimations,
            [nameof(TrackPositions)] = TrackPositions,
            [nameof(TrackMoves)] = TrackMoves,
            [nameof(TrackColors)] = TrackColors,
            [nameof(HoldSounds)] = HoldSounds,
            [nameof(HideIcons)] = HideIcons,
            [nameof(RemoveAllDecorations)] = RemoveAllDecorations,
            [nameof(RemoveTutorialPatterns)] = RemoveTutorialPatterns,
            [nameof(LimitTrackOpacity)] = LimitTrackOpacity,
            [nameof(SetCameraZoom)] = SetCameraZoom,
            [nameof(CameraZoomScale)] = CameraZoomScale,
            [nameof(ResetTrackAnimation)] = ResetTrackAnimation,
            [nameof(ResetTrackColor)] = ResetTrackColor,
        };
    }

    public void Deserialize(JToken token) {
        On = IOUtils.Read(token, nameof(On), On);
        EnableSave = IOUtils.Read(token, nameof(EnableSave), EnableSave);
        Filters = IOUtils.Read(token, nameof(Filters), Filters);
        AdvancedFilters = IOUtils.Read(token, nameof(AdvancedFilters), AdvancedFilters);
        Particles = IOUtils.Read(token, nameof(Particles), Particles);
        Decorations = IOUtils.Read(token, nameof(Decorations), Decorations);
        Backgrounds = IOUtils.Read(token, nameof(Backgrounds), Backgrounds);
        Cameras = IOUtils.Read(token, nameof(Cameras), Cameras);
        RepeatEvents = IOUtils.Read(token, nameof(RepeatEvents), RepeatEvents);
        FrameRate = IOUtils.Read(token, nameof(FrameRate), FrameRate);
        HitSounds = IOUtils.Read(token, nameof(HitSounds), HitSounds);
        PlanetOrbit = IOUtils.Read(token, nameof(PlanetOrbit), PlanetOrbit);
        PlanetScale = IOUtils.Read(token, nameof(PlanetScale), PlanetScale);
        PlanetRadius = IOUtils.Read(token, nameof(PlanetRadius), PlanetRadius);
        TrackAnimations = IOUtils.Read(token, nameof(TrackAnimations), TrackAnimations);
        TrackPositions = IOUtils.Read(token, nameof(TrackPositions), TrackPositions);
        TrackMoves = IOUtils.Read(token, nameof(TrackMoves), TrackMoves);
        TrackColors = IOUtils.Read(token, nameof(TrackColors), TrackColors);
        HoldSounds = IOUtils.Read(token, nameof(HoldSounds), HoldSounds);
        HideIcons = IOUtils.Read(token, nameof(HideIcons), HideIcons);
        RemoveAllDecorations = IOUtils.Read(token, nameof(RemoveAllDecorations), RemoveAllDecorations);
        RemoveTutorialPatterns = IOUtils.Read(token, nameof(RemoveTutorialPatterns), RemoveTutorialPatterns);
        LimitTrackOpacity = IOUtils.Read(token, nameof(LimitTrackOpacity), LimitTrackOpacity);
        SetCameraZoom = IOUtils.Read(token, nameof(SetCameraZoom), SetCameraZoom);
        CameraZoomScale = IOUtils.Read(token, nameof(CameraZoomScale), CameraZoomScale);
        ResetTrackAnimation = IOUtils.Read(token, nameof(ResetTrackAnimation), ResetTrackAnimation);
        ResetTrackColor = IOUtils.Read(token, nameof(ResetTrackColor), ResetTrackColor);
    }
}
