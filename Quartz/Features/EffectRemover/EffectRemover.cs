using ADOFAI;
using ADOFAI.Editor.Actions;
using HarmonyLib;
using Quartz.Core;
using Quartz.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Quartz.Features.EffectRemover;

// Strips visual/audio effect events from a level as it's decoded, so heavy
// charts play without filters, decorations, camera movement, etc. Ported from
// the original KorenResourcePack's EffectRemover; event types are matched by
// their numeric LevelEventType values exactly as v1 did.
//
// While the remover is on, the editor's Save buttons are disabled (saving
// would write the stripped level over the original chart) unless the user
// explicitly re-enables saving.
public static partial class EffectRemover {
    public static SettingsFile<EffectRemoverSettings> ConfMgr { get; private set; }
    public static EffectRemoverSettings Conf => ConfMgr?.Data;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<EffectRemoverSettings>(
            Path.Combine(MainCore.Paths.RootPath, "EffectRemover.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    private static bool Enabled {
        get {
            EnsureConf();
            return MainCore.IsModEnabled && Conf.On;
        }
    }

    // Enhanced strips effect events from the in-memory level on decode — in the
    // editor too — so the editor holds the stripped copy. Saving would overwrite
    // the original chart with the stripped version, so it's always blocked while
    // Enhanced is active. Simple mode never touches the chart, so saving stays
    // safe there.
    public static bool EditorSaveEnabled => !EnhancedActive;

    // Enhanced mode is the chart-stripping behaviour; gates the Decode patch.
    private static bool EnhancedActive => Enabled && Conf.IsEnhanced;

    // Simple mode disables live effect components at runtime (see
    // EffectRemoverSimplePatches); gates those patches.
    internal static bool SimpleActive => Enabled && Conf.IsSimple;

    // Tag keys on a conditional-events block (event 35) whose referenced
    // decorations must survive removal — they're gameplay feedback, not
    // decoration. "없음" is the Korean editor's "None".
    private static readonly string[] ConditionalTagKeys = [
        "perfectTag",
        "hitTag",
        "earlyPerfectTag",
        "latePerfectTag",
        "barelyTag",
        "veryEarlyTag",
        "veryLateTag",
        "missTag",
        "tooEarlyTag",
        "tooLateTag",
        "lossTag",
    ];

    private static LevelEventType Event(int value) => (LevelEventType)value;

    public static void RefreshEditorSaveButtons() => SetEditorSaveButtons(scnEditor.instance, EditorSaveEnabled);

    public static void RestoreEditorSaveButtons() => SetEditorSaveButtons(scnEditor.instance, true);

    private static void SetEditorSaveButtons(scnEditor editor, bool enabled) {
        if(editor == null || SceneManager.GetActiveScene().name != "scnEditor") {
            return;
        }

        if(editor.popupUnsavedChangesSave != null) {
            editor.popupUnsavedChangesSave.interactable = enabled;
        }
        if(editor.buttonSave != null) {
            editor.buttonSave.interactable = enabled;
        }
    }

    internal static void Remove(LevelData levelData) {
        if(!Enabled || levelData == null) {
            return;
        }

        EffectRemoverSettings conf = Conf;
        List<LevelEventType> events = [];

        if(conf.Decorations) {
            RemoveDecorations(events, levelData, conf);
        }
        if(conf.Filters) {
            AddFilterEvents(events);
        }
        if(conf.AdvancedFilters) {
            events.Add(Event(25));
        }
        if(conf.Particles) {
            events.Add(Event(64));
            events.Add(Event(63));
        }
        if(conf.Backgrounds) {
            RemoveBackgrounds(events, levelData, conf);
        }
        if(conf.Cameras) {
            RemoveCameras(events, levelData, conf);
        }
        if(conf.PlanetOrbit) {
            events.Add(Event(26));
        }
        if(conf.PlanetScale) {
            events.Add(Event(56));
        }
        if(conf.PlanetRadius) {
            events.Add(Event(52));
        }
        if(conf.RepeatEvents) {
            events.Add(Event(31));
        }
        if(conf.FrameRate) {
            events.Add(Event(61));
        }
        if(conf.HitSounds) {
            events.Add(Event(42));
            events.Add(Event(23));
        }
        if(conf.HoldSounds) {
            events.Add(Event(34));
        }
        if(conf.TrackAnimations) {
            RemoveTrackAnimations(events, levelData, conf);
        }
        if(conf.TrackPositions) {
            events.Add(Event(30));
        }
        if(conf.TrackMoves) {
            events.Add(Event(18));
        }
        if(conf.TrackColors) {
            RemoveTrackColors(events, levelData, conf);
        }
        if(conf.HideIcons) {
            events.Add(Event(50));
        }
        if(conf.LimitTrackOpacity) {
            LimitTrackOpacityValues(levelData);
        }

        if(events.Count == 0) {
            return;
        }

        HashSet<LevelEventType> eventSet = [.. events];
        levelData.levelEvents.RemoveAll(data => data != null && eventSet.Contains(data.eventType));
    }

    private static void AddFilterEvents(List<LevelEventType> events) {
        events.Add(Event(22));
        events.Add(Event(24));
        events.Add(Event(27));
        events.Add(Event(28));
        events.Add(Event(32));
        events.Add(Event(36));
        events.Add(Event(37));
    }

    private static void RemoveBackgrounds(List<LevelEventType> events, LevelData levelData, EffectRemoverSettings conf) {
        events.Add(Event(13));

        levelData.backgroundSettings = new LevelEvent(0, Event(7), GCS.settingsInfo["BackgroundSettings"]);
        levelData.miscSettings["bgVideo"] = "";

        // Resetting to the default background still draws the tutorial
        // background's pulsing shapes (and tiled pattern). Always strip the
        // shapes; strip the tile too when the user asked. Keys are read back as
        // levelData.bgShapeType / bgShowDefaultBGTile in scnGame.SetBackground.
        levelData.backgroundSettings["defaultBGShapeType"] = BGShapeType.Disabled;
        if(conf.RemoveTutorialPatterns) {
            levelData.backgroundSettings["showDefaultBGTile"] = false;
        }
    }

    private static void RemoveCameras(List<LevelEventType> events, LevelData levelData, EffectRemoverSettings conf) {
        events.Add(Event(12));

        if(!conf.SetCameraZoom) {
            return;
        }

        float zoom = Mathf.Clamp(conf.CameraZoomScale, 100f, 1000f);
        conf.CameraZoomScale = zoom;

        levelData.cameraSettings = new LevelEvent(0, Event(8), GCS.settingsInfo["CameraSettings"]);
        levelData.cameraSettings["zoom"] = zoom;
    }

    private static void RemoveDecorations(List<LevelEventType> events, LevelData levelData, EffectRemoverSettings conf) {
        if(conf.RemoveAllDecorations) {
            levelData.decorations.Clear();
            levelData.decorationSettings = new LevelEvent(0, Event(11), GCS.settingsInfo["DecorationSettings"]);

            events.Add(Event(11));
            events.Add(Event(19));
            events.Add(Event(20));
            events.Add(Event(21));
            events.Add(Event(60));
            events.Add(Event(29));
            events.Add(Event(58));
            events.Add(Event(59));
            return;
        }

        // Keep decorations referenced by conditional events (judgement
        // feedback) plus anything sharing their tags; strip the rest.
        HashSet<string> conditionalEventTags = GetConditionalEventTags(levelData);
        HashSet<string> preservedDecorationTags = GetPreservedDecorationTags(levelData, conditionalEventTags);

        levelData.decorations.RemoveAll(data =>
            IsDecorationData(data) && !ShouldPreserve(data, conditionalEventTags, preservedDecorationTags));
        levelData.levelEvents.RemoveAll(data =>
            IsDecorationData(data) && !ShouldPreserve(data, conditionalEventTags, preservedDecorationTags));
    }

    private static HashSet<string> GetConditionalEventTags(LevelData levelData) {
        HashSet<string> tags = [];

        foreach(LevelEvent eventData in levelData.levelEvents) {
            if(eventData == null || eventData.eventType != Event(35)) {
                continue;
            }

            foreach(string key in ConditionalTagKeys) {
                if(!eventData.ContainsKey(key)) {
                    continue;
                }

                string tag = eventData.GetString(key);
                if(!string.IsNullOrWhiteSpace(tag) && tag != "None" && tag != "없음") {
                    tags.Add(tag);
                }
            }
        }

        return tags;
    }

    private static HashSet<string> GetPreservedDecorationTags(LevelData levelData, HashSet<string> conditionalEventTags) {
        HashSet<string> tags = [];

        foreach(LevelEvent eventData in levelData.levelEvents) {
            if(!IsDecorationData(eventData) || !HasAnyEventTag(eventData, conditionalEventTags)) {
                continue;
            }

            foreach(string tag in GetTags(eventData, "tag")) {
                tags.Add(tag);
            }
        }

        return tags;
    }

    private static bool ShouldPreserve(LevelEvent eventData, HashSet<string> conditionalEventTags, HashSet<string> preservedDecorationTags)
        => HasAnyEventTag(eventData, conditionalEventTags) || HasAnyTag(eventData, preservedDecorationTags);

    private static bool IsDecorationData(LevelEvent eventData) {
        if(eventData == null) {
            return false;
        }

        LevelEventType type = eventData.eventType;
        return type == Event(11)
            || type == Event(19)
            || type == Event(20)
            || type == Event(21)
            || type == Event(60)
            || type == Event(29)
            || type == Event(58)
            || type == Event(59);
    }

    private static bool HasAnyEventTag(LevelEvent eventData, HashSet<string> tags) {
        foreach(string eventTag in GetTags(eventData, "eventTag")) {
            if(tags.Contains(eventTag)) {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnyTag(LevelEvent eventData, HashSet<string> tags) {
        foreach(string tag in GetTags(eventData, "tag")) {
            if(tags.Contains(tag)) {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetTags(LevelEvent eventData, string key) {
        if(eventData == null || !eventData.ContainsKey(key)) {
            yield break;
        }

        string tags = eventData.GetString(key);
        if(string.IsNullOrWhiteSpace(tags)) {
            yield break;
        }

        foreach(string tag in tags.Split(' ')) {
            if(!string.IsNullOrWhiteSpace(tag)) {
                yield return tag;
            }
        }
    }

    private static void RemoveTrackAnimations(List<LevelEventType> events, LevelData levelData, EffectRemoverSettings conf) {
        events.Add(Event(16));

        if(conf.ResetTrackAnimation) {
            levelData.trackSettings["trackAppearAnimation"] = TrackAnimationType.Fade;
            levelData.trackSettings["trackDisappearAnimation"] = TrackAnimationType.Fade;
            levelData.trackSettings["beatsAhead"] = 8.0f;
            levelData.trackSettings["beatsBehind"] = 0.0f;
        }
    }

    private static void RemoveTrackColors(List<LevelEventType> events, LevelData levelData, EffectRemoverSettings conf) {
        events.Add(Event(15));
        events.Add(Event(17));

        if(conf.ResetTrackColor) {
            levelData.trackSettings["trackStyle"] = TrackStyle.Standard;
            levelData.trackSettings["trackColor"] = "debb7bff";
            levelData.trackSettings["trackColorType"] = TrackColorType.Single;
        }
    }

    private static void LimitTrackOpacityValues(LevelData levelData) {
        foreach(LevelEvent eventData in levelData.levelEvents) {
            if(eventData == null) {
                continue;
            }
            if(eventData.eventType != Event(18) && eventData.eventType != Event(30)) {
                continue;
            }
            // Cap at 100% — leave anything already at or below 100 untouched.
            if(eventData.ContainsKey("opacity") && eventData.GetFloat("opacity") > 100.0f) {
                eventData["opacity"] = 100.0f;
            }
        }
    }

    [HarmonyPatch(typeof(LevelData), "Decode")]
    private static class LevelDataDecodePatch {
        private static void Postfix(LevelData __instance) {
            // Strip on every decode while Enhanced is active — including in the
            // editor, so effects vanish live as the level loads. The stripped
            // copy lives only in memory; the editor's Save is blocked (see
            // EditorSaveEnabled) so the original chart on disk is never touched.
            if(EnhancedActive) {
                Remove(__instance);
            }
        }
    }

    // Blocks the editor's Save action while the remover is on (the in-memory
    // level is the stripped copy) unless saving is explicitly enabled.
    [HarmonyPatch(typeof(SaveLevelEditorAction), "Execute")]
    private static class SaveLevelEditorActionPatch {
        private static bool Prefix() => EditorSaveEnabled;
    }

    [HarmonyPatch(typeof(scnEditor), "LoadGameScene")]
    private static class EditorLoadGameScenePatch {
        private static void Postfix(scnEditor __instance) => SetEditorSaveButtons(__instance, EditorSaveEnabled);
    }
}
