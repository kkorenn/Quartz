using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;

namespace Quartz.Features.Editor;

// Settings for the Editor tab — tweaks that target A Dance of Fire and Ice's
// own level editor (not the mod's settings window).
public sealed class EditorSettings : ISettingsFile {
    // Lay each inspector property out as "label [field]" on one row, instead of
    // the stock layout with the label stacked above the field.
    public bool HorizontalProperties = false;

    // Selected-tile readout (ported from AdofaiTweaks): overwrite one selected
    // tile's number with a colour-coded summary of the selection. Each metric is
    // independent; the readout is hidden when none are on.
    public bool ShowFloorAngle = true;   // total angle of the selection, in degrees
    public bool ShowFloorBeats = false;  // that angle as beats (angle / 180)
    public bool ShowFloorCount = false;  // number of selected tiles
    public bool ShowFloorDuration = false; // time the selection takes, in seconds

    // Tulttak-mod parity: exclude the last selected tile from the timing maths.
    public bool UseTulttakModBehavior = false;

    // BGA Mod: hide every tile and planet so only the level background shows,
    // for recording a background animation to composite gameplay over. The two
    // decoration toggles are extras that ride the same play-only gate.
    public bool BgaMod = false;
    public bool BgaHideTileDeco = false;   // also hide tile-attached decorations
    public bool BgaHidePlanetDeco = false; // also hide planet-attached decorations

    public bool ShowAny =>
        ShowFloorAngle || ShowFloorBeats || ShowFloorCount || ShowFloorDuration;

    public JToken Serialize() {
        return new JObject {
            [nameof(HorizontalProperties)] = HorizontalProperties,
            [nameof(ShowFloorAngle)] = ShowFloorAngle,
            [nameof(ShowFloorBeats)] = ShowFloorBeats,
            [nameof(ShowFloorCount)] = ShowFloorCount,
            [nameof(ShowFloorDuration)] = ShowFloorDuration,
            [nameof(UseTulttakModBehavior)] = UseTulttakModBehavior,
            [nameof(BgaMod)] = BgaMod,
            [nameof(BgaHideTileDeco)] = BgaHideTileDeco,
            [nameof(BgaHidePlanetDeco)] = BgaHidePlanetDeco,
        };
    }

    public void Deserialize(JToken token) {
        if(token == null) {
            return;
        }
        HorizontalProperties = IOUtils.Read(token, nameof(HorizontalProperties), HorizontalProperties);
        ShowFloorAngle = IOUtils.Read(token, nameof(ShowFloorAngle), ShowFloorAngle);
        ShowFloorBeats = IOUtils.Read(token, nameof(ShowFloorBeats), ShowFloorBeats);
        ShowFloorCount = IOUtils.Read(token, nameof(ShowFloorCount), ShowFloorCount);
        ShowFloorDuration = IOUtils.Read(token, nameof(ShowFloorDuration), ShowFloorDuration);
        UseTulttakModBehavior = IOUtils.Read(token, nameof(UseTulttakModBehavior), UseTulttakModBehavior);
        BgaMod = IOUtils.Read(token, nameof(BgaMod), BgaMod);
        BgaHideTileDeco = IOUtils.Read(token, nameof(BgaHideTileDeco), BgaHideTileDeco);
        BgaHidePlanetDeco = IOUtils.Read(token, nameof(BgaHidePlanetDeco), BgaHidePlanetDeco);
    }
}
