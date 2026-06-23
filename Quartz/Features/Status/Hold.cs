namespace Quartz.Features.Status;

// Player's hold-behavior setting (Normal / Hold Tap / No Holding Required).
// Persistence.holdBehavior lives in Assembly-CSharp.
internal static class Hold {
    internal static string GetHoldBehaviorLabel() {
        try {
            HoldBehavior behavior = Persistence.holdBehavior;
            return behavior switch {
                HoldBehavior.Normal => "Normal",
                HoldBehavior.CanHitEnd => "Hold Tap",
                HoldBehavior.NoHoldNeeded => "No Holding Required",
                _ => behavior.ToString(),
            };
        } catch {
            return null;
        }
    }
}
