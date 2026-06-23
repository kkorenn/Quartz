using System.Reflection;
using Quartz.Core;
using Quartz.Features.Status;
using Quartz.IO;
using UnityEngine;

namespace Quartz.Features.Tweaks;

// Small gameplay/visual tweaks ported from the original KorenResourcePack's
// Tweaks feature:
//   - Remove All Checkpoints (icon + behavior, so runs aren't checkpointed)
//   - Remove Ball Core Particles (planet core/spark particles killed)
//   - Disable Tile Hit Glow
//   - Remove Planet Glow
//   - Disable Auto Pause (auto-play no longer pauses on focus loss)
//   - Block Mouse Wheel Scroll While Playing
//   - Hide selected Detailed Results rows
//
// v1's judgement-popup hiding lives in JudgementPopupHider; its planet-ring
// handling belonged to the ResourceChanger, which v2 doesn't port; v1's
// stationary-tail opacity fade was dropped (the Planet Colors feature owns
// tail appearance now).
//
// Everything mutated remembers its original state by instance id so toggling
// a tweak off (or disabling the mod) restores what the level set up. Scene
// object lookups are cached and invalidated from the patches when the scene
// changes or the relevant objects respawn.
public static partial class Tweaks {
    public static SettingsFile<TweaksSettings> ConfMgr { get; private set; }
    public static TweaksSettings Conf => ConfMgr?.Data;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<TweaksSettings>(
            Path.Combine(MainCore.Paths.RootPath, "Tweaks.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    private static bool Enabled {
        get {
            EnsureConf();
            return MainCore.IsModEnabled;
        }
    }

    private static bool ShouldRemoveCheckpoints => Enabled && Conf.RemoveAllCheckpoints;
    private static bool ShouldRemoveBallCoreParticles => Enabled && Conf.RemoveBallCoreParticles;
    private static bool ShouldDisableTileHitGlow => Enabled && Conf.DisableTileHitGlow;
    private static bool ShouldRemovePlanetGlow => Enabled && Conf.RemovePlanetGlow;
    private static bool ShouldDisableAutoPause => Enabled && Conf.DisableAutoPause;
    private static bool ShouldHideAnyDetailedResultLabel => Enabled && (
        Conf.HideResultXAccuracy ||
        Conf.HideResultAccuracy ||
        Conf.HideResultCheckpoints ||
        Conf.HideResultMaximumUsedKeys
    );

    private static bool ShouldBlockMouseWheelScroll =>
        Enabled && Conf.BlockMouseWheelScrollWhilePlaying && GameStats.InGame;

    private static bool ShouldDisableMenuMusic => Enabled && Conf.DisableMenuMusic;

    internal static string FilterDetailedResults(string result) {
        if(!ShouldHideAnyDetailedResultLabel || string.IsNullOrEmpty(result)) {
            return result;
        }

        List<string> labels = [];
        AddResultLabel(labels, Conf.HideResultXAccuracy, "xAccuracy");
        AddResultLabel(labels, Conf.HideResultAccuracy, "accuracy");
        AddResultLabel(labels, Conf.HideResultCheckpoints, "checkpoints");
        // In practice mode the checkpoints slot is labelled "practiceAttempts".
        AddResultLabel(labels, Conf.HideResultCheckpoints, "practiceAttempts");
        AddResultLabel(labels, Conf.HideResultMaximumUsedKeys, "maximumUsedKeys");
        if(labels.Count == 0) {
            return result;
        }

        string[] rows = result.Split('\n');
        List<string> kept = new(rows.Length);
        for(int i = 0; i < rows.Length; i++) {
            string filtered = FilterDetailedResultRow(rows[i], labels);
            if(filtered != null) {
                kept.Add(filtered);
            }
        }
        return string.Join("\n", kept.ToArray());
    }

    private static void AddResultLabel(List<string> labels, bool enabled, string key) {
        if(!enabled) {
            return;
        }

        try {
            string label = RDString.Get("status.results." + key);
            if(!string.IsNullOrEmpty(label)) {
                labels.Add(label + ": ");
            }
        } catch {
        }
    }

    // A single result row can pack several stats into columns joined by a
    // 5-space gap (e.g. "Accuracy: 99%     Checkpoints: 3"). Drop only the
    // columns whose label matches so hiding one stat can't take its row-mate
    // with it. Returns null when no column survives (row removed entirely).
    private static string FilterDetailedResultRow(string row, List<string> labels) {
        if(string.IsNullOrEmpty(row)) {
            return row;
        }

        string[] cells = row.Split(new[] { "     " }, StringSplitOptions.None);
        List<string> kept = new(cells.Length);
        for(int i = 0; i < cells.Length; i++) {
            if(!CellMatchesLabel(cells[i], labels)) {
                kept.Add(cells[i]);
            }
        }

        if(kept.Count == 0) {
            return null;
        }
        return string.Join("     ", kept.ToArray());
    }

    private static bool CellMatchesLabel(string cell, List<string> labels) {
        string trimmed = cell.TrimStart();
        for(int i = 0; i < labels.Count; i++) {
            if(trimmed.StartsWith(labels[i], StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    // ===== Custom main-menu BPM =====
    // The menu's rabbit floor (ffxMenuPlanetSpeedChange) toggles planet speed
    // 1x<->2x and fades song2. With the feature on we drive the speed from the
    // configured slow/high BPMs (speed = targetBpm / authored menu bpm) and
    // track the toggle state ourselves, since the original toggles by reading
    // ctrl.speed == 1.0 which our custom speeds break.
    private static bool menuFast;

    private static bool ShouldCustomMenuBpm => Enabled && Conf.MenuBpmEnabled;

    // Sets the resting (slow) speed when the menu's rabbit floor spawns.
    internal static void ApplyInitialMenuBpm() {
        if(!ShouldCustomMenuBpm) {
            return;
        }
        scrConductor cond = ADOBase.conductor;
        if(cond == null || cond.bpm <= 0f) {
            return;
        }
        menuFast = false;
        SetAllPlayerSpeed(Conf.MenuSlowBpm / cond.bpm);
        SetMenuSong2(false);
    }

    // Replaces the rabbit toggle. Returns true when handled (the original
    // StartEffect should be skipped).
    internal static bool HandleMenuBpmToggle(scrFloor floor) {
        if(!ShouldCustomMenuBpm || floor == null) {
            return false;
        }
        scrConductor cond = ADOBase.conductor;
        if(cond == null || cond.bpm <= 0f) {
            return false;
        }

        menuFast = !menuFast;
        SetAllPlayerSpeed((menuFast ? Conf.MenuHighBpm : Conf.MenuSlowBpm) / cond.bpm);
        floor.floorIcon = menuFast ? FloorIcon.Snail : FloorIcon.Rabbit;
        floor.UpdateIconSprite();
        SetMenuSong2(menuFast);
        return true;
    }

    // Speed lives on each player's PlanetarySystem in this game version.
    private static void SetAllPlayerSpeed(double speed) {
        try {
            foreach(scrPlayer p in ADOBase.playerManager) {
                if(p != null && p.planetarySystem != null) {
                    p.planetarySystem.speed = speed;
                }
            }
        } catch {
        }
    }

    private static void SetMenuSong2(bool fast) {
        try {
            AudioSource song2 = ADOBase.conductor?.song2;
            if(song2 != null) {
                song2.volume = fast ? 0.7f : 0f;
            }
        } catch {
        }
    }

    // Mutes the theme song on the title / island-select screen. Enforced via
    // the mute flag every conductor Update: the game writes song.volume from
    // lots of places (level data, ducking, fades) but never touches mute, so
    // this can't be overwritten and unwinds instantly when toggled off.
    internal static void ApplyMenuMusicMute(scrConductor conductor) {
        if(conductor == null) {
            return;
        }

        bool target;
        try { target = ShouldDisableMenuMusic && ADOBase.isLevelSelect; }
        catch { return; }

        try {
            if(conductor.song != null && conductor.song.mute != target) {
                conductor.song.mute = target;
            }
            if(conductor.song2 != null && conductor.song2.mute != target) {
                conductor.song2.mute = target;
            }
        } catch {
        }
    }

    // Original states keyed by instance id, so every mutation is reversible.
    private static readonly Dictionary<int, bool> particleActiveStates = [];
    private static readonly Dictionary<int, bool> particleRendererEnabledStates = [];
    private static readonly Dictionary<int, bool> particleEmissionEnabledStates = [];
    private static readonly Dictionary<int, ParticleSystem.MinMaxCurve> particleEmissionRateStates = [];
    private static readonly Dictionary<int, int> particleMaxParticleStates = [];
    private static readonly Dictionary<int, bool> lightUpDisableGlowStates = [];
    private static readonly Dictionary<int, bool> planetGlowEnabledStates = [];
    private static readonly Dictionary<(Type, string), MemberInfo> planetRendererMemberCache = [];
    private static readonly HashSet<int> suppressNextRandomColorFloorIds = [];

    private static readonly ffxCheckpoint[] EmptyCheckpoints = [];
    private static readonly PlanetRenderer[] EmptyRenderers = [];
    private static readonly scrFloor[] EmptyFloors = [];

    private static ffxCheckpoint[] cachedCheckpoints;
    private static PlanetRenderer[] cachedRenderers;
    private static scrFloor[] cachedFloors;
    private static int lightUpDepth;

    private const BindingFlags PlanetRendererMemberFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static T[] FindObjectsCompat<T>() where T : UnityEngine.Object
        => UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None);

    public static void ClearSceneCaches() {
        InvalidateCheckpointCache();
        InvalidateRendererCache();
        InvalidateFloorCache();
        suppressNextRandomColorFloorIds.Clear();
    }

    private static void InvalidateCheckpointCache() => cachedCheckpoints = null;
    private static void InvalidateRendererCache() => cachedRenderers = null;
    private static void InvalidateFloorCache() => cachedFloors = null;

    private static ffxCheckpoint[] GetCheckpoints() {
        if(cachedCheckpoints != null) {
            return cachedCheckpoints;
        }
        try { cachedCheckpoints = FindObjectsCompat<ffxCheckpoint>(); }
        catch { cachedCheckpoints = EmptyCheckpoints; }
        return cachedCheckpoints ?? EmptyCheckpoints;
    }

    private static PlanetRenderer[] GetPlanetRenderers() {
        if(cachedRenderers != null) {
            return cachedRenderers;
        }
        try { cachedRenderers = FindObjectsCompat<PlanetRenderer>(); }
        catch { cachedRenderers = EmptyRenderers; }
        return cachedRenderers ?? EmptyRenderers;
    }

    private static scrFloor[] GetFloors() {
        if(cachedFloors != null) {
            return cachedFloors;
        }
        try { cachedFloors = FindObjectsCompat<scrFloor>(); }
        catch { cachedFloors = EmptyFloors; }
        return cachedFloors ?? EmptyFloors;
    }

    // Re-applies every visual tweak to the live scene (mod enable, UI change).
    public static void RefreshAll() {
        RefreshCheckpointTweak();
        RefreshBallCoreParticlesTweak();
        RefreshTileHitGlowTweak();
        RefreshPlanetGlowTweak();
    }

    // Puts back everything the visual tweaks changed (mod disable).
    public static void RestoreAll() {
        RefreshBallCoreParticlesTweak(true);
        RefreshPlanetGlowTweak(true);
    }

    public static void RefreshCheckpointTweak() {
        if(!ShouldRemoveCheckpoints) {
            return;
        }

        ffxCheckpoint[] checkpoints = GetCheckpoints();

        for(int i = 0; i < checkpoints.Length; i++) {
            RemoveCheckpointVisual(checkpoints[i]);
        }
    }

    public static void RefreshPlanetGlowTweak() => RefreshPlanetGlowTweak(false);

    private static void RefreshPlanetGlowTweak(bool forceRestore) {
        PlanetRenderer[] renderers = GetPlanetRenderers();

        for(int i = 0; i < renderers.Length; i++) {
            ApplyPlanetGlowTweak(renderers[i], forceRestore);
        }
    }

    private static void ApplyPlanetGlowTweak(PlanetRenderer renderer, bool forceRestore = false) {
        if(renderer == null) {
            return;
        }

        SpriteRenderer glow;
        try { glow = renderer.glow; } catch { return; }
        if(glow == null) {
            return;
        }

        int id = glow.GetInstanceID();
        if(ShouldRemovePlanetGlow && !forceRestore) {
            if(!planetGlowEnabledStates.ContainsKey(id)) {
                planetGlowEnabledStates[id] = glow.enabled;
            }
            glow.enabled = false;
        } else if(planetGlowEnabledStates.TryGetValue(id, out bool wasEnabled)) {
            glow.enabled = wasEnabled;
            planetGlowEnabledStates.Remove(id);
        }
    }

    public static void RefreshTileHitGlowTweak() {
        if(!ShouldDisableTileHitGlow) {
            return;
        }

        scrFloor[] floors = GetFloors();

        for(int i = 0; i < floors.Length; i++) {
            SuppressFloorHitGlow(floors[i]);
        }
    }

    private static void RemoveCheckpointVisual(ffxCheckpoint checkpoint) {
        if(checkpoint == null) {
            return;
        }

        scrFloor floor = null;
        try { floor = checkpoint.floor; } catch { }
        if(floor == null) {
            try { floor = checkpoint.GetComponent<scrFloor>(); } catch { }
        }
        if(floor == null) {
            return;
        }

        try {
            if(floor.floorIcon == FloorIcon.Checkpoint) {
                floor.floorIcon = FloorIcon.None;
                floor.UpdateIconSprite(true);
            }
        } catch {
        }
    }

    public static void RefreshBallCoreParticlesTweak() => RefreshBallCoreParticlesTweak(false);

    private static void RefreshBallCoreParticlesTweak(bool forceRestore) {
        PlanetRenderer[] renderers = GetPlanetRenderers();

        for(int i = 0; i < renderers.Length; i++) {
            ApplyBallCoreParticlesTweak(renderers[i], forceRestore);
        }
    }

    private static void ApplyBallCoreParticlesTweak(PlanetRenderer renderer, bool forceRestore = false) {
        if(renderer == null) {
            return;
        }
        ApplyPlanetParticleTweak(GetCoreParticles(renderer), forceRestore);
        ApplyPlanetParticleTweak(GetSparks(renderer), forceRestore);
    }

    private static ParticleSystem GetCoreParticles(PlanetRenderer renderer)
        => GetPlanetRendererParticle(renderer, "coreParticles");

    private static ParticleSystem GetSparks(PlanetRenderer renderer)
        => GetPlanetRendererParticle(renderer, "sparks");

    private static ParticleSystem GetPlanetRendererParticle(PlanetRenderer renderer, string name)
        => TryGetPlanetRendererMemberValue(renderer, name, out object value) ? value as ParticleSystem : null;

    // PlanetRenderer's particle members have moved between fields and
    // properties across game versions — resolve them reflectively and cache
    // the lookup per (type, name).
    private static bool TryGetPlanetRendererMemberValue(PlanetRenderer renderer, string name, out object value) {
        value = null;
        if(renderer == null || string.IsNullOrEmpty(name)) {
            return false;
        }

        MemberInfo member = GetPlanetRendererMember(renderer.GetType(), name);
        if(member == null) {
            return false;
        }

        try {
            if(member is FieldInfo field) {
                value = field.GetValue(renderer);
                return true;
            }

            if(member is PropertyInfo property && property.GetIndexParameters().Length == 0) {
                value = property.GetValue(renderer, null);
                return true;
            }
        } catch {
        }

        return false;
    }

    private static MemberInfo GetPlanetRendererMember(Type type, string name) {
        if(type == null || string.IsNullOrEmpty(name)) {
            return null;
        }
        // Tuple key: (Type, string) has structural equality and, as a struct
        // dictionary key, allocates nothing — unlike the old `type.FullName + "."
        // + name` concat that built a throwaway string on every per-frame call.
        var key = (type, name);
        if(planetRendererMemberCache.TryGetValue(key, out MemberInfo cached)) {
            return cached;
        }

        for(Type t = type; t != null; t = t.BaseType) {
            FieldInfo field = t.GetField(name, PlanetRendererMemberFlags);
            if(field != null) {
                planetRendererMemberCache[key] = field;
                return field;
            }

            PropertyInfo property = t.GetProperty(name, PlanetRendererMemberFlags);
            if(property != null && property.GetIndexParameters().Length == 0) {
                planetRendererMemberCache[key] = property;
                return property;
            }
        }

        planetRendererMemberCache[key] = null;
        return null;
    }

    private static bool IsRemovedPlanetParticle(PlanetRenderer renderer, ParticleSystem particles) {
        if(renderer == null || particles == null) {
            return false;
        }
        return particles == GetCoreParticles(renderer) || particles == GetSparks(renderer);
    }

    private static void ApplyPlanetParticleTweak(ParticleSystem particles, bool forceRestore) {
        if(particles == null) {
            return;
        }
        GameObject particleObject = particles.gameObject;
        if(particleObject == null) {
            return;
        }

        if(ShouldRemoveBallCoreParticles && !forceRestore) {
            try {
                int rootId = particleObject.GetInstanceID();
                if(particleActiveStates.ContainsKey(rootId) && !particleObject.activeSelf) {
                    return;
                }
            } catch {
            }
            DisableParticleSystemTree(particles, particleObject);
            return;
        }

        RestoreParticleSystemTree(particleObject);
    }

    private static void SuppressFloorHitGlow(scrFloor floor) {
        if(floor == null) {
            return;
        }

        HideFloorGlowObject(floor.topGlow);
        HideFloorGlowObject(floor.bottomGlow);
        RestoreFloorHitColor(floor);
    }

    private static void HideFloorGlowObject(SpriteRenderer glow) {
        if(glow == null) {
            return;
        }
        try { glow.gameObject.SetActive(false); } catch { }
    }

    private static void RestoreFloorHitColor(scrFloor floor) {
        if(floor == null) {
            return;
        }

        try {
            if(floor.floorRenderer == null) {
                return;
            }
            Color color = floor.floorRenderer.deselectedColor;
            if(color.a <= 0.001f && floor.floorRenderer.cachedColor.a > 0.001f) {
                color = floor.floorRenderer.cachedColor;
            }
            floor.floorRenderer.color = color;
        } catch {
        }
    }

    private static void DisableParticleSystemTree(ParticleSystem particles, GameObject particleObject) {
        RememberActiveState(particleObject);
        try { particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); } catch { }
        try { particles.Clear(true); } catch { }
        try { DisableParticleSystemEmission(particles); } catch { }

        DisableRenderers(particleObject);

        try {
            ParticleSystem[] children = particleObject.GetComponentsInChildren<ParticleSystem>(true);
            for(int i = 0; i < children.Length; i++) {
                ParticleSystem child = children[i];
                if(child == null) {
                    continue;
                }
                RememberActiveState(child.gameObject);
                try { child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); } catch { }
                try { child.Clear(true); } catch { }
                try { DisableParticleSystemEmission(child); } catch { }
            }
        } catch {
        }

        try { particleObject.SetActive(false); } catch { }
    }

    private static void DisableParticleSystemEmission(ParticleSystem particles) {
        int id = particles.GetInstanceID();
        ParticleSystem.EmissionModule emission = particles.emission;
        if(!particleEmissionEnabledStates.ContainsKey(id)) {
            particleEmissionEnabledStates[id] = emission.enabled;
        }
        if(!particleEmissionRateStates.ContainsKey(id)) {
            particleEmissionRateStates[id] = emission.rateOverTime;
        }
        emission.enabled = false;
        emission.rateOverTime = 0f;
        ParticleSystem.MainModule main = particles.main;
        if(!particleMaxParticleStates.ContainsKey(id)) {
            particleMaxParticleStates[id] = main.maxParticles;
        }
        main.maxParticles = 0;
    }

    private static void DisableRenderers(GameObject root) {
        if(root == null) {
            return;
        }
        try {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for(int i = 0; i < renderers.Length; i++) {
                Renderer renderer = renderers[i];
                if(renderer == null) {
                    continue;
                }
                int id = renderer.GetInstanceID();
                if(!particleRendererEnabledStates.ContainsKey(id)) {
                    particleRendererEnabledStates[id] = renderer.enabled;
                }
                renderer.enabled = false;
            }
        } catch {
        }
    }

    private static void RestoreParticleSystemTree(GameObject particleObject) {
        if(particleObject == null) {
            return;
        }

        try {
            GameObject[] objects = CollectGameObjects(particleObject);
            for(int i = 0; i < objects.Length; i++) {
                RestoreActiveState(objects[i]);
            }
        } catch {
            RestoreActiveState(particleObject);
        }

        try {
            ParticleSystem[] particles = particleObject.GetComponentsInChildren<ParticleSystem>(true);
            for(int i = 0; i < particles.Length; i++) {
                RestoreParticleSystemSettings(particles[i]);
            }
        } catch {
        }

        try {
            Renderer[] renderers = particleObject.GetComponentsInChildren<Renderer>(true);
            for(int i = 0; i < renderers.Length; i++) {
                Renderer renderer = renderers[i];
                if(renderer == null) {
                    continue;
                }
                int id = renderer.GetInstanceID();
                if(!particleRendererEnabledStates.TryGetValue(id, out bool wasEnabled)) {
                    continue;
                }
                renderer.enabled = wasEnabled;
                particleRendererEnabledStates.Remove(id);
            }
        } catch {
        }
    }

    private static void RestoreParticleSystemSettings(ParticleSystem particles) {
        if(particles == null) {
            return;
        }
        int id = particles.GetInstanceID();

        try {
            ParticleSystem.EmissionModule emission = particles.emission;
            if(particleEmissionEnabledStates.TryGetValue(id, out bool wasEmissionEnabled)) {
                emission.enabled = wasEmissionEnabled;
                particleEmissionEnabledStates.Remove(id);
            }
            if(particleEmissionRateStates.TryGetValue(id, out ParticleSystem.MinMaxCurve rate)) {
                emission.rateOverTime = rate;
                particleEmissionRateStates.Remove(id);
            }
        } catch {
        }

        try {
            if(particleMaxParticleStates.TryGetValue(id, out int maxParticles)) {
                ParticleSystem.MainModule main = particles.main;
                main.maxParticles = maxParticles;
                particleMaxParticleStates.Remove(id);
            }
        } catch {
        }
    }

    private static GameObject[] CollectGameObjects(GameObject root) {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        GameObject[] objects = new GameObject[transforms.Length];
        for(int i = 0; i < transforms.Length; i++) {
            objects[i] = transforms[i].gameObject;
        }
        return objects;
    }

    private static void RememberActiveState(GameObject obj) {
        if(obj == null) {
            return;
        }
        int id = obj.GetInstanceID();
        if(!particleActiveStates.ContainsKey(id)) {
            particleActiveStates[id] = obj.activeSelf;
        }
    }

    private static void RestoreActiveState(GameObject obj) {
        if(obj == null) {
            return;
        }
        int id = obj.GetInstanceID();
        if(!particleActiveStates.TryGetValue(id, out bool wasActive)) {
            return;
        }
        try { obj.SetActive(wasActive); } catch { }
        particleActiveStates.Remove(id);
    }

    // Auto-pause suppression must not eat pauses the game/editor asked for
    // (mode switches, the pause menu, scene resets) — only the focus-loss
    // auto-pause path. Those legitimate callers are identified by walking up
    // the stack, exactly as v1 did.
    private static bool IsSafePauseCallSite() {
        try {
            System.Diagnostics.StackTrace st = new(2, false);
            for(int i = 0; i < st.FrameCount; i++) {
                MethodBase m = st.GetFrame(i).GetMethod();
                if(m == null) {
                    continue;
                }
                Type dt = m.DeclaringType;
                if(dt == null) {
                    continue;
                }
                string name = m.Name;
                if(dt == typeof(scnGame) && name == "ResetScene") {
                    return true;
                }
                if(dt == typeof(scnEditor)) {
                    if(name == "SwitchToEditMode" || name == "TogglePause" ||
                        name == "ResetScene" || name == "SwitchToPlayMode" ||
                        name == "PauseIfUnpaused") {
                        return true;
                    }
                }
                if(dt == typeof(PauseMenu)) {
                    return true;
                }
            }
        } catch { }
        return false;
    }

    // Undo the editor's play-mode pause bookkeeping when we swallow its pause,
    // so auto-play keeps running with no "+ Paused" status text and the auto
    // button stays usable. No-ops outside the level editor (editor is null).
    private static void ResetEditorPlayModePauseState() {
        try {
            scnEditor editor = ADOBase.editor;
            if(editor == null) {
                return;
            }
            editor.pausedInPlayMode = false;
            if(editor.buttonAuto != null) {
                editor.buttonAuto.interactable = true;
            }
        } catch { }
    }
}
