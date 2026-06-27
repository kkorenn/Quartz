using System.Reflection;
using HarmonyLib;
using Quartz.Core;
using Quartz.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Quartz.Features.UiHider;

// Hides pieces of the game's own HUD/UI, ported from the original
// KorenResourcePack's UiHider. Two profiles (Playing / Recording) hold
// independent flag sets; a rebindable shortcut flips between them so a clean
// capture layout is one keypress away.
//
// Element visibility is reasserted every frame from a ticker (the game
// re-enables several of these on its own), with reflection fallbacks for
// members that moved across game versions. The judgement text / miss
// indicator / result text / hit-error meter / last-floor flash hides live in
// UiHiderPatches since those need to intercept the game mid-call.
public static partial class UiHider {
    public static SettingsFile<UiHiderSettings> ConfMgr { get; private set; }
    public static UiHiderSettings Conf => ConfMgr?.Data;

    internal static readonly Vector3 HiddenPosition = new(123456f, 123456f, 123456f);

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<UiHiderSettings>(
            Path.Combine(MainCore.Paths.RootPath, "UiHider.json")
        );
        ConfMgr.Load();
        EnsureTicker();
    }

    public static void Save() => ConfMgr?.RequestSave();

    private static bool IsFeatureActive() {
        EnsureConf();
        return MainCore.IsModEnabled && Conf.Enabled;
    }

    internal static UiHiderProfile SelectedProfile
        => Conf.RecordingMode ? Conf.Recording : Conf.Playing;

    public static void ApplyNow() {
        EnsureConf();
        ShowOrHideElements();
    }

    public static void Restore() => ShowOrHideElements(true);

    public static void ToggleRecordingMode() {
        EnsureConf();
        Conf.RecordingMode = !Conf.RecordingMode;
        Save();
        ShowOrHideElements();
    }

    private static void TickInternal() {
        if(!MainCore.IsModEnabled) {
            Restore();
            return;
        }

        if(ShouldToggleRecordingMode()) {
            ToggleRecordingMode();
        }

        ShowOrHideElements();
    }

    private static void ShowOrHideElements(bool forceDisabled = false) {
        EnsureConf();

        bool tweakEnabled = !forceDisabled && IsFeatureActive();
        UiHiderProfile profile = tweakEnabled ? SelectedProfile : null;

        bool hideEverything = profile != null && profile.HideEverything;
        bool hideOtto = profile != null && (hideEverything || profile.HideOtto);
        bool hideTimingTarget = profile != null && (hideEverything || profile.HideTimingTarget);
        bool hideNoFail = profile != null && (hideEverything || profile.HideNoFailIcon);
        bool hideBeta = profile != null && (hideEverything || profile.HideBeta);
        bool hideTitle = profile != null && (hideEverything || profile.HideTitle);
        bool hideMeter = profile != null && (hideEverything || profile.HideHitErrorMeter);

        try { RDC.noHud = hideEverything; } catch { }

        // The hit-error meter must reconcile BOTH ways here, before the
        // scrUIController guard below — it lives on scrController and is the one
        // hidden element the game re-activates on its own (its `paused` setter
        // flips it on at every unpause). The hide patches are one-way, so a meter
        // hidden while a profile/flag asked for it (or by a stray recording-mode
        // flip / settings import) stayed dead for the rest of the session: nothing
        // ever turned it back on once the flag cleared, and Restore() / disabling
        // the mod didn't bring it back either. Restoring here closes that gap.
        ReconcileHitErrorMeter(hideMeter);

        scrUIController uiController = scrUIController.instance;
        if(uiController == null) {
            return;
        }

        if(IsEditingLevel() || scnEditor.instance != null) {
            HideGameplayDifficultyContainer(uiController);

            object editor = scnEditor.instance;
            SetEnabled(GetMemberValue(editor, "autoImage"), !hideOtto);
            SetEnabled(GetMemberValue(editor, "buttonAuto"), !hideOtto);
            SetMemberGameObjectActiveIfMatches(editor, "editorDifficultySelector", hideTimingTarget);
            SetMemberGameObjectActiveIfMatches(editor, "buttonNoFail", hideNoFail);
        } else {
            SetEnabled(uiController.noFailImage, !hideNoFail);
            SetEnabled(uiController.difficultyImage, !hideTimingTarget);
            SetGameObjectActiveIfMatches(
                uiController.difficultyContainer != null ? uiController.difficultyContainer.gameObject : null,
                hideTimingTarget);
            SetMemberGameObjectActiveIfMatches(uiController, "difficultyFadeContainer", hideTimingTarget);
        }

        if(HasSteamBranchName()) {
            SetBetaObjectsActiveIfMatches(hideBeta);
        }

        SetGameObjectActiveIfMatches(
            uiController.txtLevelName != null ? uiController.txtLevelName.gameObject : null,
            hideTitle);
    }

    internal static bool ShouldHideJudgementText()
        => IsFeatureActive() && (SelectedProfile.HideEverything || SelectedProfile.HideJudgment);

    internal static bool ShouldHideMissIndicators()
        => IsFeatureActive() && (SelectedProfile.HideEverything || SelectedProfile.HideMissIndicators);

    internal static bool ShouldHideOtto()
        => IsFeatureActive() && (SelectedProfile.HideEverything || SelectedProfile.HideOtto);

    internal static bool ShouldHideResult()
        => IsFeatureActive() && (SelectedProfile.HideEverything || SelectedProfile.HideResult);

    internal static bool ShouldHideHitErrorMeter()
        => IsFeatureActive() && (SelectedProfile.HideEverything || SelectedProfile.HideHitErrorMeter);

    internal static bool ShouldHideLastFloorFlash()
        => IsFeatureActive() && (SelectedProfile.HideEverything || SelectedProfile.HideLastFloorFlash);

    private static bool ShouldToggleRecordingMode() {
        if(!Conf.Enabled || !Conf.UseShortcut || Keybind.Capturing) {
            return false;
        }

        KeyCode key = (KeyCode)Conf.ShortcutKey;
        if(key == KeyCode.None) {
            return false;
        }

        try {
            return Keybind.ModifierHeld((Keybind.KeyModifier)Conf.ShortcutModifier)
                && Input.GetKeyDown(key);
        } catch {
            return false;
        }
    }

    // ===== game access helpers (reflection-tolerant, ported from v1) =====

    private static PropertyInfo isEditingLevelProperty;
    // Getter + its static-ness resolved once: both are immutable for the resolved
    // property, so caching keeps the per-frame editor check off reflection's
    // GetGetMethod path. For the common static-bool getter we also build a
    // zero-boxing delegate so the per-frame read no longer allocates a boxed bool.
    private static MethodInfo isEditingLevelGetter;
    private static bool isEditingLevelGetterStatic;
    private static Func<bool> isEditingLevelStaticFunc;
    private static bool reflectionReady;

    private static bool IsEditingLevel() {
        EnsureReflection();

        if(isEditingLevelProperty != null && isEditingLevelGetter != null) {
            try {
                if(isEditingLevelGetterStatic) {
                    if(isEditingLevelStaticFunc != null) {
                        return isEditingLevelStaticFunc();
                    }
                    return Convert.ToBoolean(isEditingLevelProperty.GetValue(null, null));
                }
                object target = scnEditor.instance;
                if(target != null) {
                    return Convert.ToBoolean(isEditingLevelProperty.GetValue(target, null));
                }
            } catch { }
        }

        return scnEditor.instance != null && scnGame.instance == null;
    }

    private static void EnsureReflection() {
        if(reflectionReady) {
            return;
        }
        reflectionReady = true;

        Type adoBase = AccessTools.TypeByName("ADOBase");
        if(adoBase != null) {
            isEditingLevelProperty = AccessTools.Property(adoBase, "isEditingLevel");
            if(isEditingLevelProperty != null) {
                isEditingLevelGetter = isEditingLevelProperty.GetGetMethod(true);
                isEditingLevelGetterStatic = isEditingLevelGetter != null && isEditingLevelGetter.IsStatic;
                if(isEditingLevelGetterStatic) {
                    try {
                        isEditingLevelStaticFunc =
                            (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), isEditingLevelGetter);
                    } catch {
                        isEditingLevelStaticFunc = null;
                    }
                }
            }
        }
    }

    // Resolved field/property per (type, member), so the per-frame hide loop does
    // one dict hit + reflective get instead of two AccessTools lookups each call.
    // The member identity is stable for a type, so the cache never goes stale.
    private static readonly Dictionary<(Type, string), MemberInfo> memberCache = [];

    internal static object GetMemberValue(object owner, string memberName) {
        if(owner == null || string.IsNullOrEmpty(memberName)) {
            return null;
        }

        Type type = owner.GetType();
        var key = (type, memberName);
        if(!memberCache.TryGetValue(key, out MemberInfo member)) {
            member = (MemberInfo)AccessTools.Field(type, memberName)
                  ?? AccessTools.Property(type, memberName);
            memberCache[key] = member;
        }

        try {
            if(member is FieldInfo field) {
                return field.GetValue(owner);
            }
            if(member is PropertyInfo property) {
                return property.GetValue(owner, null);
            }
        } catch { }

        return null;
    }

    internal static GameObject GetGameObject(object value) {
        if(value == null) {
            return null;
        }
        if(value is GameObject gameObject) {
            return gameObject;
        }
        return value is Component component ? component.gameObject : null;
    }

    private static void SetEnabled(object value, bool enabled) {
        if(value == null) {
            return;
        }

        if(value is Behaviour behaviour) {
            behaviour.enabled = enabled;
            return;
        }

        PropertyInfo property = AccessTools.Property(value.GetType(), "enabled");
        if(property == null || !property.CanWrite) {
            return;
        }
        try { property.SetValue(value, enabled, null); } catch { }
    }

    private static void SetMemberGameObjectActiveIfMatches(object owner, string memberName, bool hide)
        => SetGameObjectActiveIfMatches(GetGameObject(GetMemberValue(owner, memberName)), hide);

    private static void SetGameObjectActiveIfMatches(GameObject gameObject, bool hide) {
        if(gameObject == null) {
            return;
        }
        if(gameObject.activeSelf == hide) {
            gameObject.SetActive(!hide);
        }
    }

    internal static void HideGameplayDifficultyContainer(scrUIController uiController) {
        if(uiController == null) {
            return;
        }

        try {
            if(uiController.difficultyContainer != null) {
                uiController.difficultyContainer.gameObject.SetActive(false);
            }
            if(uiController.difficultyFadeContainer != null) {
                uiController.difficultyFadeContainer.blocksRaycasts = false;
                uiController.difficultyFadeContainer.gameObject.SetActive(false);
            }
            if(uiController.difficultyButtonLeft != null) {
                uiController.difficultyButtonLeft.enabled = false;
            }
            if(uiController.difficultyButtonRight != null) {
                uiController.difficultyButtonRight.enabled = false;
            }
        } catch {
        }
    }

    // Two-way reconcile for the game's hit-error meter. The hide patches
    // (UiHiderPatches) keep it flash-free on the events where the game re-shows
    // it; this puts it BACK whenever the active profile no longer hides it. We
    // touch only the top object the game's pause logic owns and mirror the game's
    // own visibility gate (gameworld + unpaused + meter size not Off), so we never
    // fight its freeroam / results / pause / meter-size-off handling.
    private static void ReconcileHitErrorMeter(bool hide) {
        scrController controller = scrController.instance;
        if(controller == null || !controller.gameworld) {
            return;
        }

        GameObject errorMeter = GetGameObject(GetMemberValue(controller, "errorMeter"));
        if(errorMeter == null) {
            return;
        }

        if(hide) {
            if(errorMeter.activeSelf) {
                errorMeter.SetActive(false);
            }
            return;
        }

        if(!controller.paused && HitErrorMeterEnabledInGame() && !errorMeter.activeSelf) {
            errorMeter.SetActive(true);
        }
    }

    private static bool HitErrorMeterEnabledInGame() {
        try { return Persistence.hitErrorMeterSize != ErrorMeterSize.Off; }
        catch { return true; }
    }

    private static bool HasSteamBranchName() {
        try { return !string.IsNullOrEmpty(GCS.steamBranchName); }
        catch { return false; }
    }

    private static Type betaType;
    private static bool betaTypeResolved;
    private static UnityEngine.Object[] cachedBetaObjects;
    private static string cachedBetaScene;

    private static void SetBetaObjectsActiveIfMatches(bool hide) {
        if(!betaTypeResolved) {
            betaType = AccessTools.TypeByName("scrEnableIfBeta");
            betaTypeResolved = true;
        }
        if(betaType == null) {
            return;
        }

        // Resources.FindObjectsOfTypeAll scans every loaded object; never run
        // it per-frame. Beta objects are static UI, so cache them per scene.
        string scene = SceneManager.GetActiveScene().name;
        if(cachedBetaObjects == null || cachedBetaScene != scene) {
            try { cachedBetaObjects = Resources.FindObjectsOfTypeAll(betaType); }
            catch { cachedBetaObjects = null; }
            cachedBetaScene = scene;
        }
        if(cachedBetaObjects == null) {
            return;
        }

        for(int i = 0; i < cachedBetaObjects.Length; i++) {
            SetGameObjectActiveIfMatches(GetGameObject(cachedBetaObjects[i]), hide);
        }
    }

    // ===== per-frame ticker =====

    private static Ticker ticker;

    private static void EnsureTicker() {
        if(ticker != null || MainCore.Root == null) {
            return;
        }
        ticker = MainCore.Root.AddComponent<Ticker>();
    }

    private sealed class Ticker : MonoBehaviour {
        private void Update() => TickInternal();
    }
}
