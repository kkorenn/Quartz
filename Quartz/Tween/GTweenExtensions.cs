using GTweens.Extensions;
using GTweens.Tweens;
using UnityEngine;
using UnityEngine.UI;

namespace Quartz.Tween;

// Thin GTween helpers for uGUI types. Every setter/getter null-checks its
// target: a tween lives in the shared GTweensContext and keeps ticking even
// after its target GameObject is destroyed (rows rebuilt, panel torn down,
// profile switch). Writing color/alpha/position on a destroyed Graphic throws
// inside Unity every frame — and because the throw happens before the tween
// completes, it never leaves the context, so it spams forever. Unity's
// overloaded `== null` is true for destroyed objects, so these guards turn an
// orphaned tween into a harmless no-op that still finishes and unregisters.
public static class GTweenExtensions {
    public static GTween GTAlpha(this CanvasGroup target, float to, float duration)
        => GTweens.Extensions.GTweenExtensions.Tween(
            () => target == null ? to : target.alpha,
            x => { if(target != null) target.alpha = x; },
            to,
            duration
        );

    extension(Graphic target) {
        public GTween GTAlpha(float to, float duration) {
            return GTweens.Extensions.GTweenExtensions.Tween(
                () => target == null ? to : target.color.a,
                x => {
                    if(target == null) {
                        return;
                    }
                    var c = target.color;
                    c.a = x;
                    target.color = c;
                },
                to,
                duration
            );
        }

        public GTween GTColor(Color to, float duration) {
            var from = target == null ? to : target.color;
            return GTweens.Extensions.GTweenExtensions.Tween(
                () => 0f,
                x => { if(target != null) target.color = Color.Lerp(from, to, x); },
                1f,
                duration
            );
        }
    }

    public static GTween GTFade(this CanvasGroup target, float to, float duration) {
        return GTweens.Extensions.GTweenExtensions.Tween(
            () => target == null ? to : target.alpha,
            x => { if(target != null) target.alpha = x; },
            to,
            duration
        );
    }

    extension(RectTransform target) {
        public GTween GTAnchorPos(Vector2 to, float duration) {
            var from = target == null ? to : target.anchoredPosition;
            return GTweens.Extensions.GTweenExtensions.Tween(
                () => 0f,
                x => { if(target != null) target.anchoredPosition = Vector2.LerpUnclamped(from, to, x); },
                1f,
                duration
            );
        }

        public GTween GTAnchorPosX(float to, float duration) {
            return GTweens.Extensions.GTweenExtensions.Tween(
                () => target == null ? to : target.anchoredPosition.x,
                x => {
                    if(target == null) {
                        return;
                    }
                    var pos = target.anchoredPosition;
                    pos.x = x;
                    target.anchoredPosition = pos;
                },
                to,
                duration
            );
        }

        public GTween GTAnchorPosY(float to, float duration) {
            return GTweens.Extensions.GTweenExtensions.Tween(
                () => target == null ? to : target.anchoredPosition.y,
                x => {
                    if(target == null) {
                        return;
                    }
                    var pos = target.anchoredPosition;
                    pos.y = x;
                    target.anchoredPosition = pos;
                },
                to,
                duration
            );
        }

        public GTween GTSizeDelta(Vector2 to, float duration) {
            var from = target == null ? to : target.sizeDelta;
            return GTweens.Extensions.GTweenExtensions.Tween(
                () => 0f,
                x => { if(target != null) target.sizeDelta = Vector2.LerpUnclamped(from, to, x); },
                1f,
                duration
            );
        }

        public GTween GTOffsetMin(Vector2 to, float duration) {
            var from = target == null ? to : target.offsetMin;
            return GTweens.Extensions.GTweenExtensions.Tween(
                () => 0f,
                x => { if(target != null) target.offsetMin = Vector2.LerpUnclamped(from, to, x); },
                1f,
                duration
            );
        }

        public GTween GTRotate(Vector3 to, float duration) {
            Vector3 from = target == null ? to : target.localEulerAngles;
            Vector3 targetAngle = to;

            Vector3 delta = new(
                Mathf.DeltaAngle(from.x, targetAngle.x),
                Mathf.DeltaAngle(from.y, targetAngle.y),
                Mathf.DeltaAngle(from.z, targetAngle.z)
            );

            return GTweens.Extensions.GTweenExtensions.Tween(
                () => 0f,
                x => { if(target != null) target.localEulerAngles = from + (delta * x); },
                1f,
                duration
            );
        }
    }
}
