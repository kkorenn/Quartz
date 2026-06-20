using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Koren.Features.Editor;

// "Show Tile Angle" — draws the angle (in degrees) of every selected tile as a
// label on that tile in the level editor.
//
// Each label is a clone of the tile's own editor sequence-number object
// (scrFloor.editorNumText, a world-space Canvas + Text). Cloning it means the
// label inherits the game's exact font, scale, and — because it stays parented
// under the floor — tracks camera pan/zoom for free; all we do is swap the text
// to the tile's angle and nudge it below the number so the two don't overlap.
//
// Reconciled every frame from EditorFeature.Ticker: labels are (re)built only
// when the selection set changes, and their text refreshed each tick so a live
// angle edit (rotate/flip/typed value) updates immediately.
public static partial class EditorFeature {
    internal static bool ShouldShowTileAngle => Enabled && Conf.ShowTileAngle;

    private static readonly Dictionary<scrFloor, GameObject> angleLabels = new();
    private static readonly List<scrFloor> angleLabelPrune = new();

    private static void ReconcileAngleLabels() {
        bool want;
        try {
            want = ShouldShowTileAngle
                && ADOBase.isLevelEditor
                && scnEditor.instance != null
                && !scnEditor.instance.playMode;
        } catch {
            return;
        }

        if(!want) {
            ClearAngleLabels();
            return;
        }

        try {
            List<scrFloor> selected = scnEditor.instance.selectedFloors;

            // Drop labels whose floor is no longer selected (or was destroyed by
            // an edit/undo that rebuilt the floor list).
            if(angleLabels.Count > 0) {
                angleLabelPrune.Clear();
                foreach(KeyValuePair<scrFloor, GameObject> kv in angleLabels) {
                    if(kv.Key == null || selected == null || !selected.Contains(kv.Key)) {
                        angleLabelPrune.Add(kv.Key);
                    }
                }
                foreach(scrFloor floor in angleLabelPrune) {
                    if(angleLabels.TryGetValue(floor, out GameObject stale) && stale != null) {
                        Object.DestroyImmediate(stale);
                    }
                    angleLabels.Remove(floor);
                }
            }

            if(selected == null) {
                return;
            }

            foreach(scrFloor floor in selected) {
                if(floor == null) {
                    continue;
                }

                if(!angleLabels.TryGetValue(floor, out GameObject label) || label == null) {
                    label = CreateAngleLabel(floor);
                    if(label == null) {
                        continue;
                    }
                    angleLabels[floor] = label;
                }

                Text text = GetLabelText(label);
                if(text != null) {
                    text.text = FormatAngle(floor.floatDirection);
                    text.color = Conf.GetAngleColor();
                }
            }
        } catch {
            // A dead reference mid-rebuild shouldn't spam every frame; the next
            // tick prunes it and recovers.
        }
    }

    private static GameObject CreateAngleLabel(scrFloor floor) {
        scrLetterPress src = floor.editorNumText;
        if(src == null) {
            return null;
        }

        GameObject clone = Object.Instantiate(src.gameObject, src.transform.parent);
        clone.name = "KorenAngleLabel";
        clone.transform.localPosition = src.transform.localPosition;
        clone.transform.localRotation = src.transform.localRotation;
        clone.transform.localScale = src.transform.localScale;

        Text text = GetLabelText(clone);
        if(text != null) {
            text.color = Conf.GetAngleColor();
            text.raycastTarget = false;
            // Centre on the tile (the clone inherits the sequence-number's centred
            // placement); text/colour are refreshed each tick.
        }

        clone.SetActive(true);
        return clone;
    }

    private static Text GetLabelText(GameObject label) {
        scrLetterPress press = label.GetComponent<scrLetterPress>();
        if(press != null && press.letterText != null) {
            return press.letterText;
        }
        return label.GetComponentInChildren<Text>(true);
    }

    // floatDirection is the tile's absolute angle in degrees, with two sentinels:
    // -999 = the start tile (no incoming angle) and 999 = a midspin (twirl) tile.
    private static string FormatAngle(float dir) {
        if(dir <= -999f) {
            return "—"; // em dash — start tile has no angle
        }
        if(dir == 999f) {
            return "↻"; // ↻ midspin
        }

        float norm = dir % 360f;
        if(norm < 0f) {
            norm += 360f;
        }

        string number = Mathf.Abs(norm - Mathf.Round(norm)) < 0.01f
            ? Mathf.RoundToInt(norm).ToString(CultureInfo.InvariantCulture)
            : norm.ToString("0.##", CultureInfo.InvariantCulture);
        return number + "°";
    }

    private static void ClearAngleLabels() {
        if(angleLabels.Count == 0) {
            return;
        }
        foreach(KeyValuePair<scrFloor, GameObject> kv in angleLabels) {
            if(kv.Value != null) {
                Object.DestroyImmediate(kv.Value);
            }
        }
        angleLabels.Clear();
    }
}
