using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.Status;

// Persisted config for the Status HUD. Lives in its own JSON
// (UserData/Koren/Status.json), separate from CoreSettings.
//
// The HUD has two independent panels — Left (anchored top-left of screen)
// and Right (anchored top-right). Each stat has both a Show toggle and an
// "OnRight" flag; OnRight=true routes the line to the right panel.
//
// Defaults route the BPM stats (TBPM/CBPM/KPS) to the right panel and
// everything else to the left, matching the original pack's layout.
public sealed class StatusSettings : ISettingsFile {
    public bool Enabled = true;

    public bool ShowProgress = true;
    public bool ShowAccuracy = false;
    public bool ShowXAccuracy = true;
    public bool ShowMaxAccuracy = false;
    public bool ShowMaxXAccuracy = true;
    public bool ShowMusicTime = false;
    public bool ShowMapTime = false;
    public bool ShowCheckpoint = false;
    public bool ShowTbpm = false;
    public bool ShowCbpm = false;
    public bool ShowKps = false;
    public bool ShowHold = false;
    public bool ShowTimingScale = false;
    public bool ShowCombo = false;
    public bool ComboCountAuto = true;
    public bool ShowAttempt = false;
    public bool ShowTotalAttempt = false;
    public bool ShowBest = false;
    public bool ShowFps = true;

    // Per-stat side: false = Left panel, true = Right panel.
    // Defaults: BPM/KPS on the right, everything else on the left.
    public bool ProgressOnRight = false;
    public bool AccuracyOnRight = false;
    public bool XAccuracyOnRight = false;
    public bool MaxAccuracyOnRight = false;
    public bool MaxXAccuracyOnRight = false;
    public bool MusicTimeOnRight = false;
    public bool MapTimeOnRight = false;
    public bool CheckpointOnRight = false;
    public bool TbpmOnRight = true;
    public bool CbpmOnRight = true;
    public bool KpsOnRight = true;
    public bool HoldOnRight = false;
    public bool TimingScaleOnRight = false;
    public bool ComboOnRight = false;
    public bool AttemptOnRight = false;
    public bool TotalAttemptOnRight = false;
    public bool BestOnRight = false;
    public bool FpsOnRight = false;

    public string Prefix = "";
    public int Decimals = 2;
    public float FontSize = 22f;
    public string LabelSeparator = "  ";
    public float LineSpacing = 0f;
    public bool BackgroundEnabled = true;

    public float TextR = 1f;
    public float TextG = 1f;
    public float TextB = 1f;
    public float TextA = 1f;

    public float LeftPosX = 24f;
    public float LeftPosY = -24f;
    public float RightPosX = -24f;
    public float RightPosY = -24f;

    public Color GetTextColor() => new(
        Mathf.Clamp01(TextR),
        Mathf.Clamp01(TextG),
        Mathf.Clamp01(TextB),
        Mathf.Clamp01(TextA)
    );

    public void SetTextColor(Color c) {
        TextR = Mathf.Clamp01(c.r);
        TextG = Mathf.Clamp01(c.g);
        TextB = Mathf.Clamp01(c.b);
        TextA = Mathf.Clamp01(c.a);
    }

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(ShowProgress)] = ShowProgress,
            [nameof(ShowAccuracy)] = ShowAccuracy,
            [nameof(ShowXAccuracy)] = ShowXAccuracy,
            [nameof(ShowMaxAccuracy)] = ShowMaxAccuracy,
            [nameof(ShowMaxXAccuracy)] = ShowMaxXAccuracy,
            [nameof(ShowMusicTime)] = ShowMusicTime,
            [nameof(ShowMapTime)] = ShowMapTime,
            [nameof(ShowCheckpoint)] = ShowCheckpoint,
            [nameof(ShowTbpm)] = ShowTbpm,
            [nameof(ShowCbpm)] = ShowCbpm,
            [nameof(ShowKps)] = ShowKps,
            [nameof(ShowHold)] = ShowHold,
            [nameof(ShowTimingScale)] = ShowTimingScale,
            [nameof(ShowCombo)] = ShowCombo,
            [nameof(ComboCountAuto)] = ComboCountAuto,
            [nameof(ShowAttempt)] = ShowAttempt,
            [nameof(ShowTotalAttempt)] = ShowTotalAttempt,
            [nameof(ShowBest)] = ShowBest,
            [nameof(ShowFps)] = ShowFps,
            [nameof(ProgressOnRight)] = ProgressOnRight,
            [nameof(AccuracyOnRight)] = AccuracyOnRight,
            [nameof(XAccuracyOnRight)] = XAccuracyOnRight,
            [nameof(MaxAccuracyOnRight)] = MaxAccuracyOnRight,
            [nameof(MaxXAccuracyOnRight)] = MaxXAccuracyOnRight,
            [nameof(MusicTimeOnRight)] = MusicTimeOnRight,
            [nameof(MapTimeOnRight)] = MapTimeOnRight,
            [nameof(CheckpointOnRight)] = CheckpointOnRight,
            [nameof(TbpmOnRight)] = TbpmOnRight,
            [nameof(CbpmOnRight)] = CbpmOnRight,
            [nameof(KpsOnRight)] = KpsOnRight,
            [nameof(HoldOnRight)] = HoldOnRight,
            [nameof(TimingScaleOnRight)] = TimingScaleOnRight,
            [nameof(ComboOnRight)] = ComboOnRight,
            [nameof(AttemptOnRight)] = AttemptOnRight,
            [nameof(TotalAttemptOnRight)] = TotalAttemptOnRight,
            [nameof(BestOnRight)] = BestOnRight,
            [nameof(FpsOnRight)] = FpsOnRight,
            [nameof(Prefix)] = Prefix,
            [nameof(Decimals)] = Decimals,
            [nameof(FontSize)] = FontSize,
            [nameof(LabelSeparator)] = LabelSeparator,
            [nameof(LineSpacing)] = LineSpacing,
            [nameof(BackgroundEnabled)] = BackgroundEnabled,
            [nameof(TextR)] = TextR,
            [nameof(TextG)] = TextG,
            [nameof(TextB)] = TextB,
            [nameof(TextA)] = TextA,
            [nameof(LeftPosX)] = LeftPosX,
            [nameof(LeftPosY)] = LeftPosY,
            [nameof(RightPosX)] = RightPosX,
            [nameof(RightPosY)] = RightPosY,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        ShowProgress = IOUtils.Read(token, nameof(ShowProgress), ShowProgress);
        ShowAccuracy = IOUtils.Read(token, nameof(ShowAccuracy), ShowAccuracy);
        ShowXAccuracy = IOUtils.Read(token, nameof(ShowXAccuracy), ShowXAccuracy);
        ShowMaxAccuracy = IOUtils.Read(token, nameof(ShowMaxAccuracy), ShowMaxAccuracy);
        ShowMaxXAccuracy = IOUtils.Read(token, nameof(ShowMaxXAccuracy), ShowMaxXAccuracy);
        ShowMusicTime = IOUtils.Read(token, nameof(ShowMusicTime), ShowMusicTime);
        ShowMapTime = IOUtils.Read(token, nameof(ShowMapTime), ShowMapTime);
        ShowCheckpoint = IOUtils.Read(token, nameof(ShowCheckpoint), ShowCheckpoint);
        ShowTbpm = IOUtils.Read(token, nameof(ShowTbpm), ShowTbpm);
        ShowCbpm = IOUtils.Read(token, nameof(ShowCbpm), ShowCbpm);
        ShowKps = IOUtils.Read(token, nameof(ShowKps), ShowKps);
        ShowHold = IOUtils.Read(token, nameof(ShowHold), ShowHold);
        ShowTimingScale = IOUtils.Read(token, nameof(ShowTimingScale), ShowTimingScale);
        ShowCombo = IOUtils.Read(token, nameof(ShowCombo), ShowCombo);
        ComboCountAuto = IOUtils.Read(token, nameof(ComboCountAuto), ComboCountAuto);
        ShowAttempt = IOUtils.Read(token, nameof(ShowAttempt), ShowAttempt);
        ShowTotalAttempt = IOUtils.Read(token, nameof(ShowTotalAttempt), ShowTotalAttempt);
        ShowBest = IOUtils.Read(token, nameof(ShowBest), ShowBest);
        ShowFps = IOUtils.Read(token, nameof(ShowFps), ShowFps);
        ProgressOnRight = IOUtils.Read(token, nameof(ProgressOnRight), ProgressOnRight);
        AccuracyOnRight = IOUtils.Read(token, nameof(AccuracyOnRight), AccuracyOnRight);
        XAccuracyOnRight = IOUtils.Read(token, nameof(XAccuracyOnRight), XAccuracyOnRight);
        MaxAccuracyOnRight = IOUtils.Read(token, nameof(MaxAccuracyOnRight), MaxAccuracyOnRight);
        MaxXAccuracyOnRight = IOUtils.Read(token, nameof(MaxXAccuracyOnRight), MaxXAccuracyOnRight);
        MusicTimeOnRight = IOUtils.Read(token, nameof(MusicTimeOnRight), MusicTimeOnRight);
        MapTimeOnRight = IOUtils.Read(token, nameof(MapTimeOnRight), MapTimeOnRight);
        CheckpointOnRight = IOUtils.Read(token, nameof(CheckpointOnRight), CheckpointOnRight);
        TbpmOnRight = IOUtils.Read(token, nameof(TbpmOnRight), TbpmOnRight);
        CbpmOnRight = IOUtils.Read(token, nameof(CbpmOnRight), CbpmOnRight);
        KpsOnRight = IOUtils.Read(token, nameof(KpsOnRight), KpsOnRight);
        HoldOnRight = IOUtils.Read(token, nameof(HoldOnRight), HoldOnRight);
        TimingScaleOnRight = IOUtils.Read(token, nameof(TimingScaleOnRight), TimingScaleOnRight);
        ComboOnRight = IOUtils.Read(token, nameof(ComboOnRight), ComboOnRight);
        AttemptOnRight = IOUtils.Read(token, nameof(AttemptOnRight), AttemptOnRight);
        TotalAttemptOnRight = IOUtils.Read(token, nameof(TotalAttemptOnRight), TotalAttemptOnRight);
        BestOnRight = IOUtils.Read(token, nameof(BestOnRight), BestOnRight);
        FpsOnRight = IOUtils.Read(token, nameof(FpsOnRight), FpsOnRight);
        Prefix = IOUtils.Read(token, nameof(Prefix), Prefix);
        Decimals = IOUtils.Read(token, nameof(Decimals), Decimals);
        FontSize = IOUtils.Read(token, nameof(FontSize), FontSize);
        LabelSeparator = IOUtils.Read(token, nameof(LabelSeparator), LabelSeparator);
        LineSpacing = IOUtils.Read(token, nameof(LineSpacing), LineSpacing);
        BackgroundEnabled = IOUtils.Read(token, nameof(BackgroundEnabled), BackgroundEnabled);
        TextR = IOUtils.Read(token, nameof(TextR), TextR);
        TextG = IOUtils.Read(token, nameof(TextG), TextG);
        TextB = IOUtils.Read(token, nameof(TextB), TextB);
        TextA = IOUtils.Read(token, nameof(TextA), TextA);
        // Back-compat: pre-two-panel saves stored PosX/Y; honor them as the
        // left panel's anchor if the new fields are missing.
        LeftPosX = IOUtils.Read(token, nameof(LeftPosX), IOUtils.Read(token, "PosX", LeftPosX));
        LeftPosY = IOUtils.Read(token, nameof(LeftPosY), IOUtils.Read(token, "PosY", LeftPosY));
        RightPosX = IOUtils.Read(token, nameof(RightPosX), RightPosX);
        RightPosY = IOUtils.Read(token, nameof(RightPosY), RightPosY);
    }
}
