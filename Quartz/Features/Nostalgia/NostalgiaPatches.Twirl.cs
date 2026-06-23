using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Quartz.Features.Nostalgia;

// Legacy Twirl: redraw spin tiles with the old swirl sprite plus a red/blue
// direction arrow, ported from BackToThePast's LegacyTwirl. The modern
// scrController.usingOutlines flag is gone, so the per-floor `outline` bool is
// used to decide whether to add the arrow's outline.
public static partial class Nostalgia {
    // Set once Legacy Twirl has been used, so the per-floor cleanup (two
    // transform.Find calls) is skipped entirely on UpdateIconSprite for users
    // who never enable the feature — this postfix runs on every floor icon
    // update, so an unconditional Find/Destroy was needless always-on cost.
    private static bool twirlEverApplied;

    [HarmonyPatch(typeof(scrFloor), "UpdateIconSprite")]
    private static class LegacyTwirlPatch {
        private static void Postfix(scrFloor __instance) {
            if(ShouldLegacyTwirl) {
                twirlEverApplied = true;
            } else if(!twirlEverApplied) {
                return;
            }

            // Clear any twirl arrows from a previous application before deciding
            // whether to (re)add them.
            Object.DestroyImmediate(__instance.transform.Find("arrow_renderer")?.gameObject);
            Object.DestroyImmediate(__instance.transform.Find("arrow_outline_renderer")?.gameObject);

            if(!ShouldLegacyTwirl
               || __instance.isportal
               || (__instance.floorIcon != FloorIcon.Swirl && __instance.floorIcon != FloorIcon.SwirlCW)) {
                return;
            }

            NostalgiaImages.EnsureLoaded();

            float num = (float)scrMisc.GetAngleMoved(
                (float)__instance.entryangle, (float)__instance.exitangle, !__instance.isCCW);
            if(Mathf.Abs(num) <= 1E-06f && !__instance.midSpin) {
                num = Mathf.PI * 2;
            }

            __instance.SetIconSprite(__instance.isCCW ? NostalgiaImages.SwirlCcw : NostalgiaImages.SwirlCw);
            __instance.SetIconFlipped(false);

            float num2 = 0f;
            if(__instance.floorRenderer is FloorSpriteRenderer) {
                float num3 = (ADOBase.lm?.lm2?.BigTiles ?? false) ? Mathf.PI / -2 : Mathf.PI / 2;
                num2 = (float)(((scrMisc.mod((float)(__instance.exitangle - __instance.entryangle), Mathf.PI * 2) <= Mathf.PI)
                    ? __instance.entryangle : __instance.exitangle) - num3);
            }
            float num4 = -(float)__instance.entryangle + Mathf.PI / 2
                - num / 2f * (__instance.isCCW ? -1 : 1) - Mathf.PI / 2 + num2;
            __instance.SetIconAngle((__instance.floorRenderer is FloorSpriteRenderer) ? num4 : (-num4));
            __instance.SetIconOutlineSprite(__instance.isCCW ? NostalgiaImages.SwirlCcwOutline : NostalgiaImages.SwirlCwOutline);

            if(Conf.TwirlWithoutArrow) {
                return;
            }

            Renderer iconRef = (Renderer)__instance.iconsprite ?? __instance.floorRenderer.renderer;

            GameObject arrowObj = new();
            arrowObj.transform.parent = __instance.transform;
            TwirlRenderer arrow = arrowObj.AddComponent<TwirlRenderer>();
            arrow.outline = false;
            arrow.floor = __instance;
            arrow.sr.sprite = __instance.isCCW ? NostalgiaImages.ArrowCcw : NostalgiaImages.ArrowCw;
            arrow.sr.sortingLayerID = iconRef.sortingLayerID;
            arrow.sr.sortingLayerName = iconRef.sortingLayerName;
            arrow.name = "arrow_renderer";
            Vector3 localPos = new(
                0.3f * Mathf.Cos(num4 + 90 * Mathf.Deg2Rad),
                0.3f * Mathf.Sin(num4 + 90 * Mathf.Deg2Rad), 0f);
            arrow.transform.localPosition = localPos;
            arrow.transform.localEulerAngles = new Vector3(0f, 0f, num4 * Mathf.Rad2Deg);
            bool forward = num < Mathf.PI - Mathf.Pow(10f, -6f);
            arrow.sr.color = forward ? Color.red : Color.blue;

            if(__instance.outline) {
                GameObject arrowOutlineObj = new();
                arrowOutlineObj.transform.parent = __instance.transform;
                TwirlRenderer arrowOutline = arrowOutlineObj.AddComponent<TwirlRenderer>();
                arrowOutline.outline = true;
                arrowOutline.floor = __instance;
                arrowOutline.sr.sprite = __instance.isCCW ? NostalgiaImages.ArrowCcwOutline : NostalgiaImages.ArrowCwOutline;
                arrowOutline.sr.sortingLayerID = iconRef.sortingLayerID;
                arrowOutline.sr.sortingLayerName = iconRef.sortingLayerName;
                arrowOutline.name = "arrow_outline_renderer";
                arrowOutline.transform.localPosition = localPos;
                arrowOutline.transform.localEulerAngles = new Vector3(0f, 0f, num4 * Mathf.Rad2Deg);
            }
        }
    }
}

// Keeps a twirl arrow sprite aligned to its floor: sorts just above the icon,
// matches the floor scale, and fades with the floor's opacity. Self-destructs
// when the floor is no longer a swirl. Ported from BackToThePast's
// TwirlRenderer.
public sealed class TwirlRenderer : MonoBehaviour {
    public SpriteRenderer sr;
    public scrFloor floor;
    public bool outline;

    private void Awake() {
        sr = gameObject.GetOrAddComponent<SpriteRenderer>();
    }

    private void LateUpdate() {
        if(floor == null) {
            return;
        }
        if(floor.floorIcon != FloorIcon.Swirl && floor.floorIcon != FloorIcon.SwirlCW) {
            Destroy(this);
            return;
        }
        Renderer iconRef = (Renderer)floor.iconsprite ?? floor.floorRenderer.renderer;
        sr.sortingOrder = iconRef.sortingOrder + (outline ? 1 : 2);
        sr.transform.localScale = floor.transform.localScale;
        sr.SetAlpha(floor.floorRenderer.color.a * floor.opacity);
    }
}
