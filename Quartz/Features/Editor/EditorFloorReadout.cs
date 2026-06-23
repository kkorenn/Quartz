using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ADOFAI;
using Quartz.Features.GameOverlayFont;
using Quartz.Resource;
using Quartz.UI.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Quartz.Features.Editor;

// "Selected-tile readout" — ported from PizzaLovers007/AdofaiTweaks' EditorTweaks.
// Draws a colour-coded summary of the current selection — total angle, beats,
// tile count, and/or duration in seconds — on one tile in the level editor. Each
// metric toggles independently on the Editor tab.
//
// Unlike AdofaiTweaks (which overwrites the tile's own legacy UnityEngine.UI.Text
// sequence number), the readout is the mod's OWN TextMeshProUGUI in the mod's
// overlay font — like the mod's other in-game overlays. Reusing the game's Text
// meant it first rendered in the game font and only switched to the overlay font a
// few frames later, once GameOverlayFont's twin sweep caught it; an owned TMP
// renders in the overlay font immediately, every frame, and is independent of the
// "apply font to game text" toggle. It carries a GameFontExclude marker so that
// sweep leaves it alone.
//
// For placement the label clones the tile's editorNumText (inheriting its
// world-space canvas, scale, and camera tracking for free), then strips the legacy
// Text and drops a TMP in its place. The game's own number is never touched.
//
// AdofaiTweaks drives this from a web of Harmony patches; KRP folds it into the
// editor's existing per-frame reconcile tick, with a cheap per-frame signature
// (selection identity, each tile's angle/speed, bpm, the toggles) gating the
// heavier rebuild. Each selected tile's arc is recomputed with the game's own
// CalculateSingleFloorAngleLength rather than the cached scrFloor.angleLength:
// this game version's CalculateFloorAngleLengths() only refreshes tile 0, and the
// editor can rebuild a tile's geometry on an angle edit without refreshing its
// angleLength, leaving the field a stale/reflex arc (315° for a tile just set to
// 45°).
//
// Known gap: editing a Pause/FreeRoam duration alone isn't in the signature, so
// the total refreshes on the next selection/angle change rather than instantly.
public static partial class EditorFeature {
    internal static bool ShouldShowFloorReadout => Enabled && Conf.ShowAny;

    // The mod-owned label and the tile it's parented to.
    private static scrFloor readoutFloor;
    private static GameObject readoutLabel;
    private static TextMeshProUGUI readoutTmp;

    // Cached readout + the signature of the inputs that produced it. "" is a real
    // cached value (selection totals zero → nothing to show).
    private static string readoutCache;
    private static long readoutSig;
    private static bool hasReadoutSig;

    // Per-metric colours, matched to AdofaiTweaks.
    private const string AngleColor = "#ff5252";
    private const string BeatsColor = "#52a9ff";
    private const string CountColor = "#8a8a8a";
    private const string DurationColor = "#ffffff";

    // Shrink the readout below the game's own floor-number size — it's an
    // auxiliary annotation, not the tile's number.
    private const float ReadoutFontScale = 0.7f;

    private static void ReconcileFloorReadout() {
        bool want;
        try {
            want = ShouldShowFloorReadout
                && ADOBase.isLevelEditor
                && scnEditor.instance != null
                && !scnEditor.instance.playMode;
        } catch {
            return;
        }

        if(!want) {
            ClearReadout();
            return;
        }

        try {
            scnEditor editor = scnEditor.instance;

            // No selection, or the editor's own floor numbers are showing — hide
            // the readout and let the game's numbers be.
            if(editor.SelectionIsEmpty() || editor.showFloorNums) {
                ClearReadout();
                return;
            }

            UpdateReadout(editor);
        } catch {
            // A dead reference mid-rebuild shouldn't spam every frame; the next
            // tick recovers.
        }
    }

    private static void UpdateReadout(scnEditor editor) {
        List<scrFloor> selected = editor.selectedFloors;
        if(selected == null || selected.Count == 0) {
            ClearReadout();
            return;
        }

        scrFloor host = PickReadoutFloor(selected);
        if(host == null) {
            ClearReadout();
            return;
        }

        // Rebuild the total only when its inputs change; otherwise reuse the cache.
        long sig = ReadoutSignature(editor, selected);
        string text;
        if(hasReadoutSig && sig == readoutSig) {
            text = readoutCache;
        } else {
            text = BuildReadout(editor, selected) ?? "";
            readoutCache = text;
            readoutSig = sig;
            hasReadoutSig = true;
        }

        if(text.Length == 0) {
            // Nothing to show (e.g. a single tile whose angle is zero).
            ClearReadout();
            return;
        }

        if(!EnsureLabel(host)) {
            return;
        }

        bool dirty = false;
        TMP_FontAsset want = FontManager.GameOverlayFontAsset ?? FontManager.Current;
        if(want != null && readoutTmp.font != want) {
            readoutTmp.font = want;
            dirty = true;
        }
        if(readoutTmp.text != text) {
            readoutTmp.text = text; // TMP's setter re-tessellates, so guard on change.
            dirty = true;
        }
        // Resync the shadow only when the silhouette could have changed (fresh
        // label sets text from "" → dirty). Position needs no per-frame sync: the
        // shadow root is a sibling under the same tile and rides its transform.
        if(dirty) {
            ApplyReadoutShadow();
        }
    }

    // A crisp drop shadow behind the readout via the mod's shared TMP shadow
    // helper. Layered silhouette, not material underlay: the overlay font is
    // multi-atlas, so a ♩/° glyph that lands on a second atlas page would miss a
    // per-material underlay but is covered by the re-laid silhouette. Offset
    // scales with the (shrunk) font so it stays proportional. Black at half alpha,
    // crisp (softness 0 → a single layer), matching the HUD overlays' defaults.
    private static void ApplyReadoutShadow() {
        if(readoutTmp == null) {
            return;
        }
        float offset = readoutTmp.fontSize * 0.12f;
        TMPTextShadow.Apply(
            readoutTmp,
            true,
            offset,
            -offset,
            0f,
            new Color(0f, 0f, 0f, 0.5f)
        );
    }

    // (Re)build the label on the host tile, reusing it while the host is unchanged.
    private static bool EnsureLabel(scrFloor host) {
        if(readoutLabel != null && readoutFloor == host && readoutTmp != null) {
            return true;
        }

        ClearReadout();
        return CreateLabel(host);
    }

    private static bool CreateLabel(scrFloor host) {
        scrLetterPress src = host.editorNumText;
        if(src == null) {
            return false;
        }

        // Clone the tile's number object for its world-space canvas + placement,
        // then replace its rendering with our own TMP.
        GameObject clone = Object.Instantiate(src.gameObject, src.transform.parent);
        clone.name = "QuartzFloorReadout";
        clone.transform.localPosition = src.transform.localPosition;
        clone.transform.localRotation = src.transform.localRotation;
        clone.transform.localScale = src.transform.localScale;

        // Strip the game's text + any UI mesh effects (outline/shadow) so they
        // don't fight the TMP we add in their place.
        float baseSize = 24f;
        GameObject textGo = clone;
        Text gameText = clone.GetComponentInChildren<Text>(true);
        if(gameText != null) {
            baseSize = gameText.fontSize;
            textGo = gameText.gameObject;
            Object.DestroyImmediate(gameText);
        }
        foreach(scrLetterPress lp in clone.GetComponentsInChildren<scrLetterPress>(true)) {
            Object.DestroyImmediate(lp);
        }
        foreach(BaseMeshEffect fx in clone.GetComponentsInChildren<BaseMeshEffect>(true)) {
            Object.DestroyImmediate(fx);
        }

        TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
        textGo.AddComponent<GameFontExclude>();
        tmp.font = FontManager.GameOverlayFontAsset ?? FontManager.Current;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.richText = true;
        tmp.color = Color.white;
        // Same UI.Text→TMP point-size mapping the GameOverlayFont twins use, so the
        // readout matches the size the tile number rendered at, then ReadoutFontScale
        // shrinks it below that.
        tmp.fontSize = Mathf.Max(1f, baseSize * GameFontMirror.SizeScale * ReadoutFontScale);

        clone.SetActive(true);

        readoutLabel = clone;
        readoutTmp = tmp;
        readoutFloor = host;
        return true;
    }

    // Host the readout on the first selected tile, else the last; if neither is
    // on-screen (culled, so its label wouldn't render), fall back to the enabled
    // selected tile nearest the camera. Mirrors AdofaiTweaks' display-floor pick.
    private static scrFloor PickReadoutFloor(List<scrFloor> selected) {
        scrFloor first = selected[0];
        if(first != null && first.enabled) {
            return first;
        }

        scrFloor last = selected[selected.Count - 1];
        if(last != null && last.enabled) {
            return last;
        }

        scrCamera camera = scrCamera.instance;
        Vector3 cam = camera != null ? camera.transform.position : Vector3.zero;
        cam.z = 0f;

        float best = float.PositiveInfinity;
        scrFloor nearest = null;
        foreach(scrFloor floor in selected) {
            if(floor == null || !floor.enabled) {
                continue;
            }
            Vector3 p = floor.transform.position;
            p.z = 0f;
            float d = Vector3.Distance(p, cam);
            if(d < best) {
                best = d;
                nearest = floor;
            }
        }
        return nearest;
    }

    // Cheap O(selected) hash of everything BuildReadout consumes except level
    // events: which tiles are selected, their angle and speed, the bpm, and the
    // active metrics. Changes here force a rebuild; equality lets us reuse the
    // cached string without rescanning events.
    private static long ReadoutSignature(scnEditor editor, List<scrFloor> selected) {
        unchecked {
            long h = 17;
            h = h * 31 + selected.Count;
            foreach(scrFloor floor in selected) {
                if(floor == null) {
                    h = h * 31 + 1;
                    continue;
                }
                h = h * 31 + floor.seqID;
                h = h * 31 + floor.floatDirection.GetHashCode();
                h = h * 31 + floor.speed.GetHashCode();
            }
            h = h * 31 + editor.levelData.bpm.GetHashCode();

            int flags = (Conf.ShowFloorAngle ? 1 : 0)
                | (Conf.ShowFloorBeats ? 2 : 0)
                | (Conf.ShowFloorCount ? 4 : 0)
                | (Conf.ShowFloorDuration ? 8 : 0)
                | (Conf.UseTulttakModBehavior ? 16 : 0);
            h = h * 31 + flags;
            return h;
        }
    }

    private static string BuildReadout(scnEditor editor, List<scrFloor> selected) {
        int iterations = selected.Count;
        if(Conf.UseTulttakModBehavior && iterations > 1) {
            // Tulttak-mod parity: the last selected tile isn't counted in timing.
            iterations--;
        }

        int lastSeq = editor.floors.Count - 1;
        double totalAngle = 0d;

        for(int i = 0; i < iterations; i++) {
            scrFloor floor = selected[i];
            if(floor == null || floor.seqID == lastSeq) {
                continue; // the final tile of the level has no outgoing angle.
            }

            // Read the live arc, not the cached scrFloor.angleLength. This game
            // version's CalculateFloorAngleLengths() only refreshes tile 0, and the
            // editor can rebuild a tile's geometry on an angle edit
            // (RemakePath applyEventsToFloors:false) without refreshing its
            // angleLength — leaving the field a stale/reflex value (e.g. 315° for a
            // tile you just set to 45°). CalculateSingleFloorAngleLength recomputes
            // it the exact way the game does, from the now-live entry/exit/isCCW.
            double arc;
            try {
                arc = ADOBase.lm.CalculateSingleFloorAngleLength(floor);
            } catch {
                arc = floor.angleLength;
            }

            float speedFactor = i == 0 ? 1f : selected[0].speed / floor.speed;
            totalAngle += arc * speedFactor * Mathf.Rad2Deg;

            // Pauses and free-roam waits add to the angular length; twirls/holds
            // are already folded into the arc by the game.
            foreach(LevelEvent e in editor.events) {
                if(e == null || !e.active || e.floor != floor.seqID) {
                    continue;
                }
                double extra = e.eventType switch {
                    LevelEventType.Pause => e.GetFloat("duration") * 180d,
                    LevelEventType.FreeRoam => e.GetInt("duration") * 180d,
                    _ => 0d,
                };
                totalAngle += extra * speedFactor;
            }
        }

        if(totalAngle == 0d) {
            return null;
        }

        StringBuilder sb = new();
        bool any = false;

        if(Conf.ShowFloorAngle) {
            Append(sb, ref any, AngleColor,
                totalAngle.ToString("#.####", CultureInfo.InvariantCulture) + "°");
        }
        if(Conf.ShowFloorBeats) {
            Append(sb, ref any, BeatsColor,
                (totalAngle / 180d).ToString("#.####", CultureInfo.InvariantCulture) + "♩");
        }
        if(Conf.ShowFloorCount) {
            Append(sb, ref any, CountColor,
                selected.Count.ToString(CultureInfo.InvariantCulture) + "#");
        }
        if(Conf.ShowFloorDuration) {
            double seconds = totalAngle / (selected[0].speed * editor.levelData.bpm * 3d);
            Append(sb, ref any, DurationColor,
                seconds.ToString("0.######", CultureInfo.InvariantCulture) + "s");
        }

        return sb.ToString();
    }

    private static void Append(StringBuilder sb, ref bool any, string color, string body) {
        if(any) {
            sb.Append('\n');
        }
        sb.Append("<color=").Append(color).Append('>').Append(body).Append("</color>");
        any = true;
    }

    private static void ClearReadout() {
        // Tear the shadow's sibling root down first, while the TMP it's keyed to is
        // still alive — editorNumText's Text sits on the clone root, so the shadow
        // root is a SIBLING of the clone, not a child, and wouldn't fall with it.
        if(readoutTmp != null) {
            TMPTextShadow.Remove(readoutTmp);
        }
        if(readoutLabel != null) {
            Object.DestroyImmediate(readoutLabel);
        }
        readoutLabel = null;
        readoutTmp = null;
        readoutFloor = null;
    }
}
