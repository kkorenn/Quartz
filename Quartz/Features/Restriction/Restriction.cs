using HarmonyLib;
using Quartz.Core;
using Quartz.Features.Interop;
using Quartz.Features.Status;
using Quartz.IO;

namespace Quartz.Features.Restriction;

// Judgement Restriction + Death Limit, ported from the original
// KorenResourcePack's JudgementRestriction. Both watch every judged hit
// (scrMarginTracker.AddHit) and kill the run via scrPlayer.DieByHitbox when
// the rule is broken — the message shown on the fail screen is configurable
// in v2 (v1 hardcoded it).
//
// Judgement Restriction fails the run the moment a hit breaks the chosen
// rule (accuracy floor / Perfect-only / custom allowed set / no Too Early).
// Death Limit counts misses and overloads (which don't end a no-fail run on
// their own) and fails the run once a configured cap is exceeded.
public static class Restriction {
    public static SettingsFile<RestrictionSettings> ConfMgr { get; private set; }
    public static RestrictionSettings Conf => ConfMgr?.Data;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<RestrictionSettings>(
            Path.Combine(MainCore.Paths.RootPath, "Restriction.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    private static int missCount;
    private static int overloadCount;
    private static bool failTriggered;

    private static void ResetCounters() {
        missCount = 0;
        overloadCount = 0;
        failTriggered = false;
    }

    private static void TriggerFail(string reason) {
        try {
            scrController c = scrController.instance;
            if(c == null || failTriggered) {
                return;
            }
            scrPlayer p = c.playerOne;
            if(p == null) {
                return;
            }
            failTriggered = true;
            p.DieByHitbox(reason ?? "");
        } catch { }
    }

    // Display name for the judgement that broke the restriction, reusing the
    // same labels shown in the custom-allowed list so the vocabulary matches.
    public static string JudgementName(HitMargin hit) {
        string key = hit switch {
            HitMargin.TooEarly => "JR_ALLOW_TOOEARLY",
            HitMargin.VeryEarly => "JR_ALLOW_VERYEARLY",
            HitMargin.EarlyPerfect => "JR_ALLOW_EARLYPERFECT",
            HitMargin.Perfect => "JR_ALLOW_PERFECT",
            HitMargin.LatePerfect => "JR_ALLOW_LATEPERFECT",
            HitMargin.VeryLate => "JR_ALLOW_VERYLATE",
            HitMargin.TooLate => "JR_ALLOW_TOOLATE",
            HitMargin.Multipress => "JR_ALLOW_MULTIPRESS",
            HitMargin.FailMiss => "JR_ALLOW_MISS",
            HitMargin.FailOverload => "JR_ALLOW_OVERLOAD_FAIL",
            HitMargin.OverPress => "JR_ALLOW_OVERLOAD_FAIL",
            _ => null,
        };

        string fallback = hit.ToString();
        return key == null ? fallback : MainCore.Tr.Get(key, fallback);
    }

    // Substitutes the {judgement} tag in the fail message with the judgement
    // that broke the restriction. Also accepts the US spelling {judgment}.
    private static string FormatJrMessage(string msg, HitMargin hit) {
        if(string.IsNullOrEmpty(msg)) {
            return msg;
        }

        string name = JudgementName(hit);
        return msg.Replace("{judgement}", name).Replace("{judgment}", name);
    }

    private static bool ShouldFailFor(HitMargin margin) {
        int marginInt = (int)margin;
        switch(Conf.JRestrictMode) {
            // Pure Perfect only.
            case 1:
                return marginInt != (int)HitMargin.Perfect;

            // XPure Perfect: only X (dead-centre) Perfects from the XPerfect mod
            // pass. With XPerfect absent/inactive there are no X grades, so every
            // Perfect is accepted rather than failing the whole run.
            case 2: {
                if(marginInt != (int)HitMargin.Perfect) {
                    return true;
                }
                if(!XPerfectBridge.Active) {
                    return false;
                }
                XPerfectBridge.Judge xj = XPerfectBridge.LastJudge();
                return xj != XPerfectBridge.Judge.None && xj != XPerfectBridge.Judge.X;
            }

            // Custom: any judgement whose bit isn't in the allowed mask
            // fails. An empty mask is treated as "off" rather than
            // "everything fails", like v1.
            case 3: {
                int mask = Conf.JRestrictAllowedMask;
                if(mask == 0) {
                    return false;
                }
                int bit = 1 << marginInt;
                return (mask & bit) == 0;
            }

            // No Too Early.
            case 4:
                return margin == HitMargin.TooEarly;

            // Minimum accuracy.
            case 0:
            default: {
                try {
                    scrMistakesManager m = MistakesAccess.Get();
                    if(m == null) {
                        return false;
                    }
                    float acc = MistakesAccess.PercentAcc(m);
                    if(float.IsNaN(acc) || float.IsInfinity(acc)) {
                        return false;
                    }
                    return acc * 100f < Conf.JRestrictAccuracy;
                } catch {
                    return false;
                }
            }
        }
    }

    private static void AfterAddHit(HitMargin hit) {
        EnsureConf();

        if(!MainCore.IsModEnabled || hit == HitMargin.Auto) {
            return;
        }

        bool jrOn = Conf.JRestrictEnabled;
        bool dlOn = Conf.DeathLimitEnabled;
        if(!jrOn && !dlOn) {
            return;
        }

        if(hit == HitMargin.FailMiss) {
            missCount++;
        } else if(hit == HitMargin.FailOverload) {
            overloadCount++;
        }

        if(jrOn && ShouldFailFor(hit)) {
            TriggerFail(FormatJrMessage(Conf.JRestrictMessage, hit));
            return;
        }

        if(dlOn) {
            int deaths = missCount + overloadCount;
            if(Conf.MaxDeathsOn && deaths > Conf.MaxDeaths) {
                TriggerFail(Conf.DeathLimitMessage);
                return;
            }
            if(Conf.MaxMissesOn && missCount > Conf.MaxMisses) {
                TriggerFail(Conf.DeathLimitMessage);
                return;
            }
            if(Conf.MaxOverloadsOn && overloadCount > Conf.MaxOverloads) {
                TriggerFail(Conf.DeathLimitMessage);
            }
        }
    }

    [HarmonyPatch(typeof(scrMarginTracker), "AddHit", typeof(HitMargin))]
    private static class AddHitPatch {
        private static void Postfix(HitMargin hit) => AfterAddHit(hit);
    }

    [HarmonyPatch(typeof(scnGame), "Play")]
    private static class ResetOnRunStartPatch {
        private static void Postfix() => ResetCounters();
    }

    [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
    private static class ResetOnRunExitPatch {
        private static void Postfix() => ResetCounters();
    }
}
