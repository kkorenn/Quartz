using UnityEngine;

namespace Quartz.UI.Utility;

// Marks an Image (or a whole subtree) as off-limits to accent re-theming.
// UICore.RefreshTheme recolors every Image whose color matches an old palette
// entry; that's wrong for images that show data colors rather than theme
// colors — colour-picker swatches/previews, the logo, etc. Put this on those
// objects so a swatch that happens to equal a palette colour isn't hijacked
// when the accent changes.
public sealed class ThemeExempt : MonoBehaviour {
}
