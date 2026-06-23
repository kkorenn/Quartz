using UnityEngine;

namespace Quartz.UI;

// Accent-aware theme palette. A single accent color drives the whole UI via
// ApplyAccent; UICore snapshots Current before/after to remap already-built UI.
public static class UIColors {
    public struct Palette {
        public Color PanelBG;
        public Color TopBar;
        public Color MenuBG;
        public Color MenuNormal;
        public Color MenuHover;
        public Color MenuSelected;
        public Color MenuHighlight;
        public Color ObjectBG;
        public Color ObjectActive;
        public Color ObjectInactive;
        public Color ObjectActiveBright;
        public Color ObjectActiveLightBright;
        public Color ObjectButton;
        public Color SoftRed;
    }

    private static readonly Color BasePanelBG = new(0.165f, 0.161f, 0.196f, 1f);
    private static readonly Color BaseSoftRed = new(0.886f, 0.404f, 0.427f, 1f);

    public static Color PanelBG { get; private set; } = BasePanelBG;
    public static Color TopBar { get; private set; } = new(0.255f, 0.259f, 0.333f, 1f);

    public static Color MenuBG { get; private set; } = new(0.42f, 0.431f, 0.545f, 1f);
    public static Color MenuNormal { get; private set; } = new(0.635f, 0.655f, 0.878f, 0f);
    public static Color MenuHover { get; private set; } = new(0.635f, 0.655f, 0.878f, 0.4f);
    public static Color MenuSelected { get; private set; } = new(0.635f, 0.655f, 0.878f, 1f);
    public static Color MenuHighlight { get; private set; } = new(0.824f, 0.835f, 0.965f, 1f);

    public static Color ObjectBG { get; private set; } = new(0.235f, 0.227f, 0.294f, 1f);
    public static Color ObjectActive { get; private set; } = new(0.569f, 0.604f, 1f, 1f);
    public static Color ObjectInactive { get; private set; } = new(0.569f, 0.604f, 1f, 0.4f);
    public static Color ObjectActiveBright { get; private set; } = new(0.812f, 0.827f, 1f, 1f);
    public static Color ObjectActiveLightBright { get; private set; } = new(0.557f, 0.596f, 1f, 1f);
    public static Color ObjectButton { get; private set; } = new(0.478f, 0.514f, 0.875f, 1f);

    // Expression-evaluator slider state colors (green/yellow/red). Fixed
    // semantic colors, deliberately accent-independent — they signal eval
    // state, not theme, so ApplyAccent leaves them alone.
    public static readonly Color ObjectActiveMathOk = new(0.588f, 1f, 0.569f, 1f);
    public static readonly Color ObjectActiveMathWarn = new(1f, 0.898f, 0.569f, 1f);
    public static readonly Color ObjectActiveMathErr = new(1f, 0.569f, 0.569f, 1f);

    public static Color SoftRed { get; private set; } = BaseSoftRed;

    public static Palette Current => new() {
        PanelBG = PanelBG,
        TopBar = TopBar,
        MenuBG = MenuBG,
        MenuNormal = MenuNormal,
        MenuHover = MenuHover,
        MenuSelected = MenuSelected,
        MenuHighlight = MenuHighlight,
        ObjectBG = ObjectBG,
        ObjectActive = ObjectActive,
        ObjectInactive = ObjectInactive,
        ObjectActiveBright = ObjectActiveBright,
        ObjectActiveLightBright = ObjectActiveLightBright,
        ObjectButton = ObjectButton,
        SoftRed = SoftRed
    };

    public static void ApplyAccent(Color accent) {
        accent.r = Mathf.Clamp01(accent.r);
        accent.g = Mathf.Clamp01(accent.g);
        accent.b = Mathf.Clamp01(accent.b);
        accent.a = 1f;

        PanelBG = BasePanelBG;
        TopBar = Color.Lerp(BasePanelBG, accent, 0.16f);
        MenuBG = Color.Lerp(BasePanelBG, accent, 0.34f);
        MenuNormal = WithAlpha(accent, 0f);
        MenuHover = WithAlpha(accent, 0.38f);
        MenuSelected = accent;
        MenuHighlight = Color.Lerp(accent, Color.white, 0.46f);
        ObjectBG = Color.Lerp(BasePanelBG, accent, 0.09f);
        ObjectActive = accent;
        ObjectInactive = WithAlpha(accent, 0.4f);
        ObjectActiveBright = Color.Lerp(accent, Color.white, 0.55f);
        ObjectActiveLightBright = Color.Lerp(accent, Color.white, 0.18f);
        ObjectButton = Color.Lerp(accent, BasePanelBG, 0.18f);
        SoftRed = BaseSoftRed;
    }

    private static Color WithAlpha(Color color, float alpha) {
        color.a = alpha;
        return color;
    }
}
