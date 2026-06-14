using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;

namespace Koren.Features.Editor;

// Settings for the Editor tab — tweaks that target A Dance of Fire and Ice's
// own level editor (not the mod's settings window).
public sealed class EditorSettings : ISettingsFile {
    // Lay each inspector property out as "label [field]" on one row, instead of
    // the stock layout with the label stacked above the field.
    public bool HorizontalProperties = false;

    public JToken Serialize() {
        return new JObject {
            [nameof(HorizontalProperties)] = HorizontalProperties,
        };
    }

    public void Deserialize(JToken token) {
        if(token == null) {
            return;
        }
        HorizontalProperties = IOUtils.Read(token, nameof(HorizontalProperties), HorizontalProperties);
    }
}
