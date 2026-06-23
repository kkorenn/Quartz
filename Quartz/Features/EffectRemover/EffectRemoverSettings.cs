using System;
using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;

namespace Quartz.Features.EffectRemover;

// Persisted config for the Effect Remover, ported field-for-field from the
// original KorenResourcePack (defaults match v1's Settings.cs). Lives in
// UserData/Quartz/EffectRemover.json.
public sealed class EffectRemoverSettings : ISettingsFile {
    public bool On = true;

    // === Mode ===
    // "enhanced" (default, the original KRP behaviour) strips effect events out
    // of the level data on decode. "simple" instead disables the live effect
    // components at runtime (ported from PizzaLovers007's AdofaiTweaks
    // DisableEffects) — it never touches the chart, so it's editor-safe.
    public const string ModeSimple = "simple";
    public const string ModeEnhanced = "enhanced";
    public string Mode = ModeEnhanced;
    public bool IsSimple => string.Equals(Mode, ModeSimple, StringComparison.OrdinalIgnoreCase);
    public bool IsEnhanced => !IsSimple;

    // === Simple mode (AdofaiTweaks DisableEffects) ===
    public const int MoveTrackUpperBound = 100; // above this = unlimited
    public bool SimpleFilter = false;
    public bool SimpleBloom = false;
    public bool SimpleFlash = false;
    public bool SimpleHallOfMirrors = false;
    public bool SimpleScreenShake = false;
    public int SimpleMoveTrackMax = MoveTrackUpperBound + 5; // default: unlimited

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
            [nameof(Mode)] = Mode,
            [nameof(SimpleFilter)] = SimpleFilter,
            [nameof(SimpleBloom)] = SimpleBloom,
            [nameof(SimpleFlash)] = SimpleFlash,
            [nameof(SimpleHallOfMirrors)] = SimpleHallOfMirrors,
            [nameof(SimpleScreenShake)] = SimpleScreenShake,
            [nameof(SimpleMoveTrackMax)] = SimpleMoveTrackMax,
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
        Mode = IOUtils.Read(token, nameof(Mode), Mode);
        SimpleFilter = IOUtils.Read(token, nameof(SimpleFilter), SimpleFilter);
        SimpleBloom = IOUtils.Read(token, nameof(SimpleBloom), SimpleBloom);
        SimpleFlash = IOUtils.Read(token, nameof(SimpleFlash), SimpleFlash);
        SimpleHallOfMirrors = IOUtils.Read(token, nameof(SimpleHallOfMirrors), SimpleHallOfMirrors);
        SimpleScreenShake = IOUtils.Read(token, nameof(SimpleScreenShake), SimpleScreenShake);
        SimpleMoveTrackMax = IOUtils.Read(token, nameof(SimpleMoveTrackMax), SimpleMoveTrackMax);
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
