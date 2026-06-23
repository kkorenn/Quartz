using GTweens.Easings;
using GTweens.Extensions;
using GTweens.Tweens;
using Quartz.Core;
using UnityEngine;

namespace Quartz.UI.Utility;

public class UIScrollController : MonoBehaviour {
    public RectTransform content;
    public RectTransform viewport;

    public float dragSensitivity = 1f;
    public float dragToScrollRatio = 1f;

    public float scrollDuration = 0.2f;
    public Easing scrollEase = Easing.OutCirc;

    private bool rightDragging;
    private Vector2 lastMousePos;

    private float targetY;
    private GTween scrollTween;

    private void Awake() {
        if(content != null) {
            targetY = content.anchoredPosition.y;
        }
    }

    private void Update() {
        if(content == null || viewport == null) {
            return;
        }

        HandleWheel();
        HandleRightDrag();
    }

    private void HandleWheel() {
        float wheel = Input.mouseScrollDelta.y;

        if(Mathf.Abs(wheel) <= 0.0001f) {
            return;
        }

        // Fixed pixels per wheel notch (independent of content length), so a long
        // list like the font dropdown scrolls at the same speed as a short page.
        AddDelta(-wheel * MainCore.Conf.ScrollSpeed);
        ApplyTween();
    }

    private void HandleRightDrag() {
        if(Input.GetMouseButtonDown(1)) {
            rightDragging = true;
        }

        if(Input.GetMouseButtonUp(1)) {
            rightDragging = false;
            ApplyTween();
        }

        if(!rightDragging) {
            return;
        }

        float contentHeight = content.rect.height;
        float viewportHeight = viewport.rect.height;

        float maxOffset = Mathf.Max(0f, contentHeight - viewportHeight);

        if(maxOffset <= 0f) {
            return;
        }

        Vector2 mouse = Input.mousePosition;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            viewport,
            mouse,
            null,
            out Vector2 local
        );

        float normalized = 1f - Mathf.Clamp01(
            (local.y + (viewportHeight * 0.5f)) / viewportHeight
        );

        targetY = normalized * maxOffset;

        ApplyTween();
    }

    private void AddDelta(float deltaPixels) {
        float contentHeight = content.rect.height;
        float viewportHeight = viewport.rect.height;

        float maxOffset = Mathf.Max(0f, contentHeight - viewportHeight);

        targetY += deltaPixels;
        targetY = Mathf.Clamp(targetY, 0f, maxOffset);
    }

    private void ApplyTween() {
        scrollTween?.Kill();

        scrollTween = GTweenExtensions.Tween(
            () => content.anchoredPosition.y,
            x => {
                content.anchoredPosition = new Vector2(
                    content.anchoredPosition.x,
                    x
                );
            },
            targetY,
            scrollDuration
        )
        .SetEasing(scrollEase);
        MainCore.TC.Play(scrollTween);
    }

    // Scrolls so the content sits `y` pixels down from its top, with the same
    // tween as wheel scrolling. Clamped to the scrollable range.
    public void ScrollTo(float y) {
        if(content == null || viewport == null) {
            return;
        }

        float maxOffset = Mathf.Max(0f, content.rect.height - viewport.rect.height);

        targetY = Mathf.Clamp(y, 0f, maxOffset);
        ApplyTween();
    }

    public void SetContent(RectTransform content, RectTransform viewport) {
        this.content = content;
        this.viewport = viewport;

        if(content != null) {
            targetY = content.anchoredPosition.y;
        }
    }
}