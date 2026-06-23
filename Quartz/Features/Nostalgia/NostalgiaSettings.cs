using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;

namespace Quartz.Features.Nostalgia;

// Persisted config for the Nostalgia feature — a faithful port of tjwogud's
// "BackToThePast" mod, which reverts modern ADOFAI behaviour/visuals back to
// how the game used to look and sound. Field names follow BTTP's Settings.cs;
// defaults match BTTP's defaults (everything off except the OldXO secret level,
// which BTTP ships on — we default it off here since it injects a fake world
// into the level-select and is the most game-version-fragile piece).
// Lives in UserData/Quartz/Nostalgia.json.
public sealed class NostalgiaSettings : ISettingsFile {
    // === Play ===
    public bool LegacyResult = false;
    public bool NoResult = false;
    public bool HideDifficulty = false;
    public bool HideNoFail = false;
    public bool OldPracticeMode = false;
    public bool ShowSmallSpeedChange = false;
    public bool ShowDetailSpeedChange = false;
    public float MinBpmToShowSpeedChange = 0.05f;
    public bool LegacyFlash = false;
    public bool NoJudgeAnimation = false;
    public bool LateJudgement = false;
    public bool ForceJudgeCount = false;
    public int JudgeCount = 100;
    public bool LegacyTwirl = false;
    public bool TwirlWithoutArrow = false;

    // === Editor ===
    public bool Space360Tile = false;
    public bool WeakAuto = false;
    public bool WhiteAuto = false;
    public bool LegacyEditorButtonsPositions = false;
    public bool LegacyEditorButtonsDesigns = false;
    public bool LegacyTexts = false; // Korean-only editor string reverts

    // === SFX ===
    public bool DisablePurePerfectSound = false;
    public bool DisableWindSound = false;
    public bool DisableDeathSound = false;
    public bool DisableCountdownSound = false;
    public bool DisableEndingSound = false;
    public bool DisableNewBestSound = false;

    // === Etc ===
    public bool LegacyCLS = false;
    public bool DisableAlphaWarning = false;
    public bool DisableAnnounceSign = false;
    public bool OldBackground = false;
    public int OldBackgroundIndex = 0; // 0 = A, 1 = B

    public JToken Serialize() {
        return new JObject {
            [nameof(LegacyResult)] = LegacyResult,
            [nameof(NoResult)] = NoResult,
            [nameof(HideDifficulty)] = HideDifficulty,
            [nameof(HideNoFail)] = HideNoFail,
            [nameof(OldPracticeMode)] = OldPracticeMode,
            [nameof(ShowSmallSpeedChange)] = ShowSmallSpeedChange,
            [nameof(ShowDetailSpeedChange)] = ShowDetailSpeedChange,
            [nameof(MinBpmToShowSpeedChange)] = MinBpmToShowSpeedChange,
            [nameof(LegacyFlash)] = LegacyFlash,
            [nameof(NoJudgeAnimation)] = NoJudgeAnimation,
            [nameof(LateJudgement)] = LateJudgement,
            [nameof(ForceJudgeCount)] = ForceJudgeCount,
            [nameof(JudgeCount)] = JudgeCount,
            [nameof(LegacyTwirl)] = LegacyTwirl,
            [nameof(TwirlWithoutArrow)] = TwirlWithoutArrow,
            [nameof(Space360Tile)] = Space360Tile,
            [nameof(WeakAuto)] = WeakAuto,
            [nameof(WhiteAuto)] = WhiteAuto,
            [nameof(LegacyEditorButtonsPositions)] = LegacyEditorButtonsPositions,
            [nameof(LegacyEditorButtonsDesigns)] = LegacyEditorButtonsDesigns,
            [nameof(LegacyTexts)] = LegacyTexts,
            [nameof(DisablePurePerfectSound)] = DisablePurePerfectSound,
            [nameof(DisableWindSound)] = DisableWindSound,
            [nameof(DisableDeathSound)] = DisableDeathSound,
            [nameof(DisableCountdownSound)] = DisableCountdownSound,
            [nameof(DisableEndingSound)] = DisableEndingSound,
            [nameof(DisableNewBestSound)] = DisableNewBestSound,
            [nameof(LegacyCLS)] = LegacyCLS,
            [nameof(DisableAlphaWarning)] = DisableAlphaWarning,
            [nameof(DisableAnnounceSign)] = DisableAnnounceSign,
            [nameof(OldBackground)] = OldBackground,
            [nameof(OldBackgroundIndex)] = OldBackgroundIndex,
        };
    }

    public void Deserialize(JToken token) {
        LegacyResult = IOUtils.Read(token, nameof(LegacyResult), LegacyResult);
        NoResult = IOUtils.Read(token, nameof(NoResult), NoResult);
        HideDifficulty = IOUtils.Read(token, nameof(HideDifficulty), HideDifficulty);
        HideNoFail = IOUtils.Read(token, nameof(HideNoFail), HideNoFail);
        OldPracticeMode = IOUtils.Read(token, nameof(OldPracticeMode), OldPracticeMode);
        ShowSmallSpeedChange = IOUtils.Read(token, nameof(ShowSmallSpeedChange), ShowSmallSpeedChange);
        ShowDetailSpeedChange = IOUtils.Read(token, nameof(ShowDetailSpeedChange), ShowDetailSpeedChange);
        MinBpmToShowSpeedChange = IOUtils.Read(token, nameof(MinBpmToShowSpeedChange), MinBpmToShowSpeedChange);
        LegacyFlash = IOUtils.Read(token, nameof(LegacyFlash), LegacyFlash);
        NoJudgeAnimation = IOUtils.Read(token, nameof(NoJudgeAnimation), NoJudgeAnimation);
        LateJudgement = IOUtils.Read(token, nameof(LateJudgement), LateJudgement);
        ForceJudgeCount = IOUtils.Read(token, nameof(ForceJudgeCount), ForceJudgeCount);
        JudgeCount = IOUtils.Read(token, nameof(JudgeCount), JudgeCount);
        LegacyTwirl = IOUtils.Read(token, nameof(LegacyTwirl), LegacyTwirl);
        TwirlWithoutArrow = IOUtils.Read(token, nameof(TwirlWithoutArrow), TwirlWithoutArrow);
        Space360Tile = IOUtils.Read(token, nameof(Space360Tile), Space360Tile);
        WeakAuto = IOUtils.Read(token, nameof(WeakAuto), WeakAuto);
        WhiteAuto = IOUtils.Read(token, nameof(WhiteAuto), WhiteAuto);
        LegacyEditorButtonsPositions = IOUtils.Read(token, nameof(LegacyEditorButtonsPositions), LegacyEditorButtonsPositions);
        LegacyEditorButtonsDesigns = IOUtils.Read(token, nameof(LegacyEditorButtonsDesigns), LegacyEditorButtonsDesigns);
        LegacyTexts = IOUtils.Read(token, nameof(LegacyTexts), LegacyTexts);
        DisablePurePerfectSound = IOUtils.Read(token, nameof(DisablePurePerfectSound), DisablePurePerfectSound);
        DisableWindSound = IOUtils.Read(token, nameof(DisableWindSound), DisableWindSound);
        DisableDeathSound = IOUtils.Read(token, nameof(DisableDeathSound), DisableDeathSound);
        DisableCountdownSound = IOUtils.Read(token, nameof(DisableCountdownSound), DisableCountdownSound);
        DisableEndingSound = IOUtils.Read(token, nameof(DisableEndingSound), DisableEndingSound);
        DisableNewBestSound = IOUtils.Read(token, nameof(DisableNewBestSound), DisableNewBestSound);
        LegacyCLS = IOUtils.Read(token, nameof(LegacyCLS), LegacyCLS);
        DisableAlphaWarning = IOUtils.Read(token, nameof(DisableAlphaWarning), DisableAlphaWarning);
        DisableAnnounceSign = IOUtils.Read(token, nameof(DisableAnnounceSign), DisableAnnounceSign);
        OldBackground = IOUtils.Read(token, nameof(OldBackground), OldBackground);
        OldBackgroundIndex = IOUtils.Read(token, nameof(OldBackgroundIndex), OldBackgroundIndex);
    }
}
