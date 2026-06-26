using System.Reflection;
using HarmonyLib;
using Quartz.Core;
using Quartz.IO;
using UnityEngine;

namespace Quartz.Features.PlanetColors;

// Custom planet (ball) colors, ported from the original KorenResourcePack's
// ResourceChanger "Change ball color" feature. Each of the three planet slots
// gets its own ball color/opacity and tail color/opacity; the planet sprite
// is swapped to the white texture so the tint reads as the actual color, and
// special planet skins are disabled while active.
//
// The game re-asserts planet colors from many places (LoadPlanetColor on
// spawn, SetColor from level events, the level-select rainbow planets), so
// the patches both rewrite the color arguments in flight and re-apply ours
// after the game's own calls. `applying` guards re-entry: our apply path goes
// through the same SetPlanetColor/SetTailColor/... methods the patches hook.
//
// v1 also forced the planet ring transparent while ball colors are changed
// (the ring is drawn in the vanilla planet color and clashes), and recolored
// the title-screen logo's FIRE/ICE words with Planet 1's color — both live
// here too. The v1 ring color and tile color options were not ported.
public static partial class PlanetColors {
    public static SettingsFile<PlanetColorsSettings> ConfMgr { get; private set; }
    public static PlanetColorsSettings Conf => ConfMgr?.Data;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<PlanetColorsSettings>(
            Path.Combine(MainCore.Paths.RootPath, "PlanetColors.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    private static bool ShouldChange {
        get {
            EnsureConf();
            return MainCore.IsModEnabled && Conf.Enabled;
        }
    }

    // True while our own apply path is running, so the patches on the
    // PlanetRenderer color methods let those calls through untouched.
    private static bool applying;

    private static readonly Dictionary<int, int> rendererSlots = [];
    private static readonly Dictionary<(Type, string), MemberInfo> memberCache = [];
    private static readonly Dictionary<string, MethodInfo> colorMethodCache = [];
    private static readonly object[] colorInvokeArgs = new object[1];
    private static MethodInfo setParticleSystemColorMethod;
    private static readonly object[] particleColorInvokeArgs = new object[3];

    // ApplyPlanetRing reads ring/onlyRing once per planet off PlanetRenderer's
    // per-frame LateUpdate. Resolve zero-boxing typed field accessors once
    // (Harmony emits direct field IL) instead of reflecting + boxing the bool
    // every frame. Falls back to the member resolver if a game version exposes
    // these as something other than a plain field.
    private static bool ringAccessorsResolved;
    private static AccessTools.FieldRef<PlanetRenderer, LineRenderer> ringRef;
    private static AccessTools.FieldRef<PlanetRenderer, bool> onlyRingRef;

    private static readonly scrPlanet[] EmptyPlanets = [];
    private static PlanetarySystem cachedSystem;
    private static int cachedSystemCount = -1;
    private static scrPlanet[] cachedSystemPlanets;

    // The game darkens the tail particles' start color relative to the tail
    // color; same multiplier v1 used.
    private static readonly Color TailStartColorMultiplier = new(0.5f, 0.5f, 0.5f, 1f);

    private const BindingFlags MemberFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static void ClearSceneCaches() {
        InvalidatePlanetCache();
        rendererSlots.Clear();
    }

    private static void InvalidatePlanetCache() {
        cachedSystem = null;
        cachedSystemCount = -1;
        cachedSystemPlanets = null;
    }

    private static T[] FindObjectsCompat<T>() where T : UnityEngine.Object
        => UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None);

    private static scrPlanet[] GetSystemPlanets(PlanetarySystem system) {
        if(system == null) {
            return EmptyPlanets;
        }

        int count = system.allPlanets != null ? system.allPlanets.Count : 0;
        if(cachedSystemPlanets != null && cachedSystem == system && cachedSystemCount == count) {
            return cachedSystemPlanets;
        }

        cachedSystem = system;
        cachedSystemCount = count;
        cachedSystemPlanets = system.allPlanets != null && count > 0
            ? [.. system.allPlanets]
            : EmptyPlanets;
        return cachedSystemPlanets;
    }

    private static scrPlanet[] GetPlanets() {
        try {
            PlanetarySystem system = ADOBase.controller != null ? ADOBase.controller.planetarySystem : null;
            scrPlanet[] planets = GetSystemPlanets(system);
            if(planets.Length > 0) {
                return planets;
            }
        } catch {
        }

        try { return FindObjectsCompat<scrPlanet>(); }
        catch { return EmptyPlanets; }
    }

    private static bool IsRedPlanet(scrPlanet planet) {
        try {
            PlanetarySystem system = planet != null ? planet.planetarySystem : null;
            system ??= ADOBase.controller != null ? ADOBase.controller.planetarySystem : null;

            if(system != null) {
                if(system.planetRed == planet) {
                    return true;
                }
                if(system.planetBlue == planet) {
                    return false;
                }
            }
        } catch {
        }

        try { return planet != null && planet.planetIndex == 0; }
        catch { return true; }
    }

    private static int GetPlanetSlot(scrPlanet planet) {
        if(planet == null) {
            return 0;
        }

        try {
            PlanetarySystem system = planet.planetarySystem;
            system ??= ADOBase.controller != null ? ADOBase.controller.planetarySystem : null;

            if(system != null) {
                if(system.planetRed == planet) {
                    return 0;
                }
                if(system.planetBlue == planet) {
                    return 1;
                }
                int index = system.allPlanets != null ? system.allPlanets.IndexOf(planet) : -1;
                if(index >= 0) {
                    return Mathf.Clamp(index, 0, PlanetColorsSettings.Slots - 1);
                }
            }
        } catch {
        }

        try { return Mathf.Clamp(planet.planetIndex, 0, PlanetColorsSettings.Slots - 1); }
        catch { return 0; }
    }

    private static int GetPlanetSlot(PlanetRenderer renderer) {
        if(renderer == null) {
            return 0;
        }

        int rendererId = renderer.GetInstanceID();
        if(rendererSlots.TryGetValue(rendererId, out int slot)) {
            return Mathf.Clamp(slot, 0, PlanetColorsSettings.Slots - 1);
        }

        scrPlanet planet = FindPlanetForRenderer(renderer);
        if(planet == null) {
            return 0;
        }

        slot = GetPlanetSlot(planet);
        rendererSlots[rendererId] = slot;
        return slot;
    }

    private static scrPlanet FindPlanetForRenderer(PlanetRenderer renderer) {
        if(renderer == null) {
            return null;
        }

        scrPlanet[] planets = GetPlanets();
        for(int i = 0; i < planets.Length; i++) {
            scrPlanet planet = planets[i];
            if(planet == null) {
                continue;
            }
            try {
                if(planet.planetRenderer == renderer) {
                    return planet;
                }
            } catch {
            }
        }

        return null;
    }

    private static void RememberRendererSlot(scrPlanet planet) {
        if(planet == null) {
            return;
        }
        try {
            if(planet.planetRenderer != null) {
                rendererSlots[planet.planetRenderer.GetInstanceID()] = GetPlanetSlot(planet);
            }
        } catch {
        }
    }

    private static Color BallColor(int slot) => Conf.GetBallColor(slot);
    private static Color TailColor(int slot) => Conf.GetTailColor(slot);

    // Re-applies the configured colors to every live planet (mod enable,
    // UI change).
    public static void Refresh() {
        if(!ShouldChange) {
            return;
        }

        scrPlanet[] planets = GetPlanets();
        for(int i = 0; i < planets.Length; i++) {
            ApplyPlanetColor(planets[i]);
        }

        try { ApplyLogoColor(scrLogoText.instance); } catch { }
    }

    // Hands every planet back to the game's own colors (mod disable, master
    // toggle off).
    public static void Restore() {
        scrPlanet[] planets = GetPlanets();
        for(int i = 0; i < planets.Length; i++) {
            scrPlanet planet = planets[i];
            if(planet == null || planet.planetRenderer == null) {
                continue;
            }

            bool wasApplying = applying;
            applying = true;
            try { planet.planetRenderer.LoadPlanetColor(IsRedPlanet(planet)); }
            catch { }
            finally { applying = wasApplying; }
        }

        // ShouldChange is false by the time this runs, so the UpdateColors
        // patch lets the vanilla tint through.
        try { scrLogoText.instance?.UpdateColors(); } catch { }

        rendererSlots.Clear();
    }

    // ===== title logo (FIRE / ICE words) =====
    //
    // Same as v1: both words get Planet 1's color. ColorLogo's first
    // parameter has been Color and Color? across game versions, so the
    // method is resolved reflectively once.

    private static MethodInfo logoColorMethod;
    private static bool logoColorMethodResolved;
    private static readonly object[] logoColorInvokeArgs = new object[2];

    private static Color LogoColor {
        get {
            Color ball = Conf.GetBallColor(0);
            return new Color(ball.r, ball.g, ball.b, 1f);
        }
    }

    private static void ApplyLogoColor(scrLogoText logoText) {
        if(logoText == null || !ShouldChange) {
            return;
        }

        Color color = LogoColor;
        InvokeLogoColor(logoText, color, true);
        InvokeLogoColor(logoText, color, false);
    }

    private static void InvokeLogoColor(scrLogoText logoText, Color color, bool isFire) {
        try {
            MethodInfo method = GetLogoColorMethod(logoText.GetType());
            if(method == null) {
                return;
            }

            logoColorInvokeArgs[0] = color;
            logoColorInvokeArgs[1] = isFire;
            method.Invoke(logoText, logoColorInvokeArgs);
        } catch {
        }
    }

    private static MethodInfo GetLogoColorMethod(Type type) {
        if(logoColorMethodResolved) {
            return logoColorMethod;
        }
        logoColorMethodResolved = true;

        if(type == null) {
            return null;
        }

        MethodInfo[] methods = type.GetMethods(MemberFlags);
        for(int i = 0; i < methods.Length; i++) {
            MethodInfo method = methods[i];
            if(method.Name != "ColorLogo") {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if(parameters.Length != 2 || parameters[1].ParameterType != typeof(bool)) {
                continue;
            }

            Type colorType = parameters[0].ParameterType;
            if(colorType == typeof(Color) || Nullable.GetUnderlyingType(colorType) == typeof(Color)) {
                logoColorMethod = method;
                return logoColorMethod;
            }
        }

        return null;
    }

    private static void ApplyPlanetColor(scrPlanet planet) {
        if(planet == null) {
            return;
        }
        RememberRendererSlot(planet);
        ApplyPlanetRendererColor(planet.planetRenderer, GetPlanetSlot(planet));
    }

    private static void ApplyPlanetRendererColor(PlanetRenderer renderer)
        => ApplyPlanetRendererColor(renderer, GetPlanetSlot(renderer));

    private static void ApplyPlanetRendererColor(PlanetRenderer renderer, int slot) {
        if(renderer == null || !ShouldChange || applying) {
            return;
        }

        applying = true;
        try {
            slot = Mathf.Clamp(slot, 0, PlanetColorsSettings.Slots - 1);
            Color ballColor = BallColor(slot);
            Color tailColor = TailColor(slot);

            try { renderer.DisableAllSpecialPlanets(); } catch { }
            try {
                if(renderer.sprite != null && ADOBase.gc != null && ADOBase.gc.tex_planetWhite != null) {
                    renderer.sprite.sprite = ADOBase.gc.tex_planetWhite;
                }
            } catch {
            }

            try { renderer.SetPlanetColor(ballColor); } catch { }
            try { renderer.SetTailColor(tailColor); } catch { }
            ApplyTailParticleColor(renderer, tailColor);
            try { renderer.SetCoreColor(ballColor); } catch { }
            InvokeRendererColor(renderer, "SetFaceColor", ballColor);
        } finally {
            applying = false;
        }
    }

    private static void ApplyTailParticleColor(PlanetRenderer renderer, Color tailColor) {
        if(renderer == null) {
            return;
        }

        Color startColor = tailColor * TailStartColorMultiplier;
        ParticleSystem tail = GetParticles(renderer, "tailParticles");
        ParticleSystem tailCoop = GetParticles(renderer, "tailParticlesCoop");

        ApplyTailParticleSystemColor(renderer, tail, tailColor, startColor);
        if(tailCoop != tail) {
            ApplyTailParticleSystemColor(renderer, tailCoop, tailColor, startColor);
        }
    }

    private static void ApplyTailParticleSystemColor(PlanetRenderer renderer, ParticleSystem particles, Color baseColor, Color startColor) {
        if(renderer == null || particles == null) {
            return;
        }

        try {
            setParticleSystemColorMethod ??= AccessTools.Method(
                typeof(PlanetRenderer),
                "SetParticleSystemColor",
                [typeof(ParticleSystem), typeof(Color), typeof(Color)]
            );

            if(setParticleSystemColorMethod != null) {
                particleColorInvokeArgs[0] = particles;
                particleColorInvokeArgs[1] = baseColor;
                particleColorInvokeArgs[2] = startColor;
                setParticleSystemColorMethod.Invoke(renderer, particleColorInvokeArgs);
                return;
            }
        } catch {
        }

        try {
            ParticleSystem.MainModule main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(startColor);
        } catch {
        }
    }

    // The ring is drawn in the vanilla planet color and clashes with custom
    // ball colors. With ring recolor off it's forced transparent (v1 default);
    // with it on the ring is painted the configured color instead. Skipped for
    // onlyRing planets, where the ring IS the planet.
    private static void ApplyPlanetRing(PlanetRenderer renderer) {
        if(!ShouldChange || renderer == null) {
            return;
        }

        LineRenderer ring = GetRing(renderer);
        if(ring == null) {
            return;
        }

        if(IsOnlyRing(renderer)) {
            return;
        }

        try {
            if(Conf.EnableRingRecolor) {
                Color rc = Conf.GetRingColor();
                if(ring.startColor != rc) {
                    ring.startColor = rc;
                }
                if(ring.endColor != rc) {
                    ring.endColor = rc;
                }
            } else {
                Color s = ring.startColor;
                if(s.a != 0f) {
                    s.a = 0f;
                    ring.startColor = s;
                }
                Color e = ring.endColor;
                if(e.a != 0f) {
                    e.a = 0f;
                    ring.endColor = e;
                }
            }
        } catch {
        }
    }

    private static void EnsureRingAccessors() {
        if(ringAccessorsResolved) {
            return;
        }
        ringAccessorsResolved = true;
        try {
            ringRef = AccessTools.FieldRefAccess<PlanetRenderer, LineRenderer>("ring");
        } catch {
        }
        try {
            onlyRingRef = AccessTools.FieldRefAccess<PlanetRenderer, bool>("onlyRing");
        } catch {
        }
    }

    private static LineRenderer GetRing(PlanetRenderer renderer) {
        EnsureRingAccessors();
        if(ringRef != null) {
            return ringRef(renderer);
        }
        return TryGetMemberValue(renderer, "ring", out object ringObj) ? ringObj as LineRenderer : null;
    }

    private static bool IsOnlyRing(PlanetRenderer renderer) {
        EnsureRingAccessors();
        if(onlyRingRef != null) {
            return onlyRingRef(renderer);
        }
        return TryGetMemberValue(renderer, "onlyRing", out object onlyRing) && onlyRing is bool b && b;
    }

    private static ParticleSystem GetParticles(PlanetRenderer renderer, string name)
        => TryGetMemberValue(renderer, name, out object value) ? value as ParticleSystem : null;

    private static void InvokeRendererColor(PlanetRenderer renderer, string methodName, Color color) {
        try {
            if(!colorMethodCache.TryGetValue(methodName, out MethodInfo method)) {
                method = AccessTools.Method(typeof(PlanetRenderer), methodName, [typeof(Color)]);
                colorMethodCache[methodName] = method;
            }
            if(method != null) {
                colorInvokeArgs[0] = color;
                method.Invoke(renderer, colorInvokeArgs);
            }
        } catch {
        }
    }

    private static bool TryGetMemberValue(object target, string name, out object value) {
        value = null;
        if(target == null || string.IsNullOrEmpty(name)) {
            return false;
        }

        MemberInfo member = GetMember(target.GetType(), name);
        if(member == null) {
            return false;
        }

        try {
            if(member is FieldInfo field) {
                value = field.GetValue(target);
                return true;
            }

            if(member is PropertyInfo property && property.GetIndexParameters().Length == 0) {
                value = property.GetValue(target, null);
                return true;
            }
        } catch {
        }

        return false;
    }

    private static MemberInfo GetMember(Type type, string name) {
        if(type == null || string.IsNullOrEmpty(name)) {
            return null;
        }
        // Tuple key: (Type, string) has structural equality and, as a struct
        // dictionary key, allocates nothing — unlike the old `type.FullName + "."
        // + name` concat that built a throwaway string on every per-frame call.
        var key = (type, name);
        if(memberCache.TryGetValue(key, out MemberInfo cached)) {
            return cached;
        }

        for(Type t = type; t != null; t = t.BaseType) {
            FieldInfo field = t.GetField(name, MemberFlags);
            if(field != null) {
                memberCache[key] = field;
                return field;
            }

            PropertyInfo property = t.GetProperty(name, MemberFlags);
            if(property != null && property.GetIndexParameters().Length == 0) {
                memberCache[key] = property;
                return property;
            }
        }

        memberCache[key] = null;
        return null;
    }
}
