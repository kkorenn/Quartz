using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace Quartz.Features.Nostalgia;

// Play-category patches ported from BackToThePast: noResult, hideDifficulty,
// hideNoFail, oldPracticeMode, showSpeedChange, legacyFlash, noJudgeAnimation,
// lateJudgement, forceJudgeCount. (legacyResult is an IL transpiler that can't
// be runtime-gated in Quartz's always-applied patch model, so it is not ported.)
public static partial class Nostalgia {

    // --- No Result: blank the congrats/results text on portal land ---
    [HarmonyPatch(typeof(scrController), "OnLandOnPortal")]
    private static class NoResultPatch {
        private static void Postfix(scrController __instance) {
            if(__instance.gameworld && ShouldNoResult) {
                __instance.txtCongrats.text = string.Empty;
                scrUIController.instance.txtResults.gameObject.SetActive(false);
                __instance.txtAllStrictClear.text = string.Empty;
            }
        }
    }

    // --- Hide Difficulty: re-hide whenever the editor/UI rebuilds it ---
    [HarmonyPatch]
    private static class HideDifficultyApplyPatch {
        private static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(scnEditor), "Start");
            yield return AccessTools.Method(typeof(scnEditor), "SwitchToEditMode");
            yield return AccessTools.Method(typeof(scrUIController), "ShowDifficultyContainer");
            yield return AccessTools.Method(typeof(EditorDifficultySelector), "SetChangeable");
        }
        private static void Postfix() {
            if(ShouldHideDifficulty) {
                ToggleDifficulty(false);
            }
        }
    }

    [HarmonyPatch]
    private static class HideDifficultyCancelPatch {
        private static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(EditorDifficultySelector), "ToggleDifficulty");
            yield return AccessTools.Method(typeof(scrUIController), "DifficultyArrowPressed");
        }
        private static bool Prefix() => !ShouldHideDifficulty;
    }

    // --- Hide No-Fail: re-hide on editor build, block toggling it on ---
    [HarmonyPatch]
    private static class HideNoFailApplyPatch {
        private static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(scnEditor), "SwitchToEditMode");
            yield return AccessTools.Method(typeof(scnEditor), "Start");
        }
        private static void Postfix() {
            if(ShouldHideNoFail) {
                ToggleNoFail(false);
            }
        }
    }

    [HarmonyPatch(typeof(scnEditor), "ToggleNoFail")]
    private static class EditorToggleNoFailPatch {
        private static bool Prefix() => !ShouldHideNoFail;
    }

    // The editor re-shows the difficulty selector and no-fail button on its own
    // (HUD refresh, unpause, etc.), reverting a one-shot hide and leaving only
    // the grayed-out look. Re-hide from LateUpdate (after the editor's Update,
    // before render) — but only when a button has actually been re-shown, so
    // it's a cheap activeSelf check every frame and a real hide only on the
    // frame it reappears.
    [HarmonyPatch(typeof(scnEditor), "LateUpdate")]
    private static class EditorReHidePatch {
        private static void Postfix(scnEditor __instance) {
            if(ShouldHideDifficulty
               && __instance.editorDifficultySelector != null
               && __instance.editorDifficultySelector.gameObject.activeSelf) {
                ToggleDifficulty(false);
            }
            if(ShouldHideNoFail
               && __instance.buttonNoFail != null
               && __instance.buttonNoFail.gameObject.activeSelf) {
                ToggleNoFail(false);
            }
            RepositionDifficulty();
        }
    }

    [HarmonyPatch]
    private static class ClsToggleNoFailPatch {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(scnCLS), "ToggleNoFail")
            ?? AccessTools.Method(
                typeof(ADOBase).Assembly.GetType("OptionsPanelsCLS"), "ToggleNoFail");
        private static bool Prepare(MethodBase original) => TargetMethod() != null;
        private static bool Prefix() => !ShouldHideNoFail;
    }

    // --- Old Practice Mode: 'P' starts practice from the fail screen ---
    [HarmonyPatch(typeof(scrController), "Fail2_Update")]
    private static class OldPracticeModePatch {
        private static bool Prefix() {
            if(ShouldOldPracticeMode
               && scrController.instance.practiceAvailable
               && !GCS.practiceMode
               && Input.GetKeyDown(KeyCode.P)) {
                scrController.instance.SetPracticeMode(true);
                return false;
            }
            return true;
        }
    }

    // --- Show Speed Change: tint same-speed tiles with a rabbit/snail icon ---
    [HarmonyPatch(typeof(scrFloor), "UpdateIconSprite")]
    private static class ShowSpeedChangePatch {
        private static void Prefix(scrFloor __instance) {
            if(!ShouldShowSmallSpeedChange || scrLevelMaker.instance == null) {
                return;
            }
            switch(__instance.floorIcon) {
                case FloorIcon.Rabbit:
                case FloorIcon.DoubleRabbit:
                case FloorIcon.Snail:
                case FloorIcon.DoubleSnail:
                case FloorIcon.AnimatedRabbit:
                case FloorIcon.AnimatedDoubleRabbit:
                case FloorIcon.AnimatedSnail:
                case FloorIcon.AnimatedDoubleSnail:
                case FloorIcon.SameSpeed:
                    break;
                default:
                    return;
            }
            float prevSpeed = __instance.seqID > 0
                ? scrLevelMaker.instance.listFloors[__instance.seqID - 1].speed
                : 1f;
            float speedDifference = (__instance.speed - prevSpeed) / prevSpeed;
            bool detail = Conf.ShowDetailSpeedChange;
            if(detail && Mathf.Abs(speedDifference) <= Conf.MinBpmToShowSpeedChange) {
                __instance.floorIcon = FloorIcon.SameSpeed;
            } else if(!detail && Mathf.Abs(speedDifference) == 0) {
                __instance.floorIcon = FloorIcon.SameSpeed;
            } else {
                __instance.floorIcon = (speedDifference > 0f)
                    ? ((Mathf.Abs(speedDifference) < 1.05f) ? FloorIcon.Rabbit : FloorIcon.DoubleRabbit)
                    : ((1f - Mathf.Abs(speedDifference) > 0.45f) ? FloorIcon.Snail : FloorIcon.DoubleSnail);
            }
        }
    }

    // --- Legacy Flash: re-fire the old red full-screen damage flash ---
    // The damage handler moved scrController.OnDamage -> scrPlayer.OnDamage in
    // newer builds; the flash effect scrFlash.OnDamage() (red, half-alpha) is
    // unchanged. Postfix the player's damage event and fire the legacy flash on
    // top, exactly like the old game did.
    [HarmonyPatch]
    private static class LegacyFlashPatch {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(scrPlayer), "OnDamage")
            ?? AccessTools.Method(typeof(scrController), "OnDamage");
        private static bool Prepare() => TargetMethod() != null;
        private static void Postfix() {
            if(!ShouldLegacyFlash) {
                return;
            }
            // scrFlash.OnDamage() starts the flash already half-faded
            // (colortimer = colorduration / 2), so only ~0.2s shows. FlashEx
            // starts from full red and fades over the whole duration, so it
            // reads as a proper damage flash.
            scrFlash.FlashEx(Color.red.WithAlpha(0.5f), Color.clear, 0.6f);
        }
    }

    // --- No Judge Animation: snap the hit-text instead of the pop tween ---
    [HarmonyPatch(typeof(scrHitTextMesh), "Show")]
    private static class NoJudgeAnimationPatch {
        private static void Postfix(scrHitTextMesh __instance) {
            if(!ShouldNoJudgeAnimation) {
                return;
            }
            Renderer meshRenderer = Traverse.Create(__instance).Field("meshRenderer").GetValue<Renderer>();
            __instance.transform.DOKill();
            __instance.transform.localRotation = scrCamera.instance.transform.rotation;
            if(meshRenderer != null) {
                var tweens = DOTween.TweensByTarget(meshRenderer.material);
                if(tweens != null && tweens.Count > 0) {
                    tweens[0].SetEase(Ease.InExpo);
                }
            }
        }
    }

    // --- Late Judgement: place the judgement text on the previous tile ---
    // ShowHitText moved scrController -> scrHitTextManager and now takes the
    // planet (not a ref position), computing the spot internally. So instead of
    // editing an argument, we Postfix it: find the text that was just shown and
    // move it (via its textPos) onto the previous tile, like the old game did.
    [HarmonyPatch(typeof(scrHitTextManager), "ShowHitText")]
    private static class LateJudgementPatch {
        private static bool Prepare() => AccessTools.Method(typeof(scrHitTextManager), "ShowHitText") != null;
        private static void Postfix(scrHitTextManager __instance, HitMargin hitMargin, scrPlanet planet) {
            if(!ShouldLateJudgement) {
                return;
            }
            switch(hitMargin) {
                case HitMargin.TooEarly:
                case HitMargin.TooLate:
                case HitMargin.FailMiss:
                case HitMargin.FailOverload:
                    return;
            }
            try {
                // The other planet's current floor IS the previous tile (the one
                // just left), so use it directly — no seqID offset.
                scrFloor other = planet?.other?.currfloor;
                if(other == null) {
                    return;
                }
                Vector3 pos = other.transform.position;
                pos.y += 1f;

                var cached = Traverse.Create(__instance).Field("cachedHitTexts")
                    .GetValue<Dictionary<HitMargin, scrHitTextMesh[]>>();
                if(cached == null || !cached.TryGetValue(hitMargin, out scrHitTextMesh[] arr)) {
                    return;
                }
                // The text shown this call is the most recently shown live one.
                scrHitTextMesh newest = null;
                int best = int.MinValue;
                foreach(scrHitTextMesh m in arr) {
                    if(m == null || m.dead) {
                        continue;
                    }
                    int fs = Traverse.Create(m).Field("frameShown").GetValue<int>();
                    if(fs >= best) {
                        best = fs;
                        newest = m;
                    }
                }
                if(newest != null) {
                    Traverse.Create(newest).Field("textPos").SetValue(pos);
                    newest.transform.position = pos;
                }
            } catch { }
        }
    }
}
