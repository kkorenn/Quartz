using GTweens.Easings;
using System.Drawing;

namespace GTweens.Interpolators;

public sealed class SystemColorInterpolator : IInterpolator<Color> {
    public static readonly SystemColorInterpolator Instance = new();

    SystemColorInterpolator() {

    }

    public Color Evaluate(
        Color initialValue,
        Color finalValue,
        float time,
        EasingDelegate easingDelegate
        ) {
        // System.Drawing channels are bytes (0-255), so the eased values are
        // already in 0-255 space — build the Color directly (the old FromRgba
        // path both rotated channels A->R->G->B->A and re-multiplied by 255,
        // overflowing every in-range value). Clamp because eased overshoot
        // (Back/Elastic) can leave [0,255] and Color.FromArgb throws outside it.
        return Color.FromArgb(
            ToByte(easingDelegate!(initialValue.A, finalValue.A, time)),
            ToByte(easingDelegate(initialValue.R, finalValue.R, time)),
            ToByte(easingDelegate(initialValue.G, finalValue.G, time)),
            ToByte(easingDelegate(initialValue.B, finalValue.B, time))
            );
    }

    static int ToByte(float channel) {
        int v = (int)(channel + 0.5f);
        return v < 0 ? 0 : v > 255 ? 255 : v;
    }

    public Color Subtract(Color initialValue, Color finalValue) {
        return Color.FromArgb(
            finalValue.A - initialValue.A,
            finalValue.R - initialValue.R,
            finalValue.G - initialValue.G,
            finalValue.B - initialValue.B
        );
    }

    public Color Add(Color initialValue, Color finalValue) {
        return Color.FromArgb(
            finalValue.A + initialValue.A,
            finalValue.R + initialValue.R,
            finalValue.G + initialValue.G,
            finalValue.B + initialValue.B
        );
    }
}