using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.Editor;

// Settings for the Editor tab — tweaks that target A Dance of Fire and Ice's
// own level editor (not the mod's settings window).
public sealed class EditorSettings : ISettingsFile {
    // Lay each inspector property out as "label [field]" on one row, instead of
    // the stock layout with the label stacked above the field.
    public bool HorizontalProperties = false;

    // Show the selected tile's angle (in degrees) as a label on the tile in the
    // level editor.
    public bool ShowTileAngle = false;

    // Colour of that on-tile angle label. White by default, to match the
    // editor's own tile-number text.
    public float AngleColorR = 1f, AngleColorG = 1f, AngleColorB = 1f, AngleColorA = 1f;

    public Color GetAngleColor() => new(
        Mathf.Clamp01(AngleColorR), Mathf.Clamp01(AngleColorG),
        Mathf.Clamp01(AngleColorB), Mathf.Clamp01(AngleColorA));

    public void SetAngleColor(Color c) {
        AngleColorR = Mathf.Clamp01(c.r); AngleColorG = Mathf.Clamp01(c.g);
        AngleColorB = Mathf.Clamp01(c.b); AngleColorA = Mathf.Clamp01(c.a);
    }

    public JToken Serialize() {
        return new JObject {
            [nameof(HorizontalProperties)] = HorizontalProperties,
            [nameof(ShowTileAngle)] = ShowTileAngle,
            [nameof(AngleColorR)] = AngleColorR,
            [nameof(AngleColorG)] = AngleColorG,
            [nameof(AngleColorB)] = AngleColorB,
            [nameof(AngleColorA)] = AngleColorA,
        };
    }

    public void Deserialize(JToken token) {
        if(token == null) {
            return;
        }
        HorizontalProperties = IOUtils.Read(token, nameof(HorizontalProperties), HorizontalProperties);
        ShowTileAngle = IOUtils.Read(token, nameof(ShowTileAngle), ShowTileAngle);
        AngleColorR = IOUtils.Read(token, nameof(AngleColorR), AngleColorR);
        AngleColorG = IOUtils.Read(token, nameof(AngleColorG), AngleColorG);
        AngleColorB = IOUtils.Read(token, nameof(AngleColorB), AngleColorB);
        AngleColorA = IOUtils.Read(token, nameof(AngleColorA), AngleColorA);
    }
}
