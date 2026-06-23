using Quartz.Core;
using Quartz.Resource;
using Quartz.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Quartz.UI.Utility;

public enum ResizeHandleType {
    Top,
    Left,
    Right,
    Bottom,

    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public class ResizeHandle : MonoBehaviour {

    public ResizeHandleType Type;
    public RectTransform Panel;
    public RectTransform PanelParent;

    private Vector2 startMouse;
    private Vector2 startSize;
    private Vector2 startPos;

    private bool hovered;
    private bool dragging;

    public const float MIN_WIDTH = 900f;
    public const float MIN_HEIGHT = 500f;

    private ResizeCursorShape Shape => Type switch {
        ResizeHandleType.Left or ResizeHandleType.Right => ResizeCursorShape.Horizontal,
        ResizeHandleType.Top or ResizeHandleType.Bottom => ResizeCursorShape.Vertical,
        ResizeHandleType.TopRight or ResizeHandleType.BottomLeft => ResizeCursorShape.DiagNESW,
        _ => ResizeCursorShape.DiagNWSE, // TopLeft / BottomRight
    };

    private void Awake() {
        var trigger = gameObject.AddComponent<EventTrigger>();

        var downEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        downEntry.callback.AddListener(_ => { dragging = true; OnPointerDownInternal(); });
        trigger.triggers.Add(downEntry);

        var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        dragEntry.callback.AddListener(_ => OnDragInternal());
        trigger.triggers.Add(dragEntry);

        var upEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        upEntry.callback.AddListener(_ => {
            dragging = false;
            // Persist the new size only when an actual resize happened (not a bare
            // click), so it's restored on the next launch.
            if(Panel != null && Panel.sizeDelta != startSize) {
                UICore.SavePanelSize();
            }
            if(!hovered) {
                NativeCursor.Reset();
            }
        });
        trigger.triggers.Add(upEntry);

        var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enterEntry.callback.AddListener(_ => { hovered = true; NativeCursor.Apply(Shape); });
        trigger.triggers.Add(enterEntry);

        var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(_ => {
            hovered = false;
            if(!dragging) {
                NativeCursor.Reset();
            }
        });
        trigger.triggers.Add(exitEntry);
    }

    // The OS reclaims the cursor on every mouse-move message, so re-assert it
    // each frame while this handle is hovered or being dragged.
    private void Update() {
        if(hovered || dragging) {
            NativeCursor.Apply(Shape);
        }
    }

    private void OnDisable() {
        if(hovered || dragging) {
            hovered = false;
            dragging = false;
            NativeCursor.Reset();
        }
    }

    private void OnPointerDownInternal() {
        startSize = Panel.sizeDelta;
        startPos = Panel.anchoredPosition;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            PanelParent,
            Input.mousePosition,
            null,
            out startMouse
        );
    }

    public void OnDragInternal() {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            PanelParent,
            Input.mousePosition,
            null,
            out Vector2 currentMouse
        );

        Vector2 delta = currentMouse - startMouse;
        Vector2 newSize = startSize;
        Vector2 newPos = startPos;

        float minW = MIN_WIDTH / MainCore.Conf.UIScale;
        float minH = MIN_HEIGHT / MainCore.Conf.UIScale;

        Vector2 pivot = Panel.pivot;

        if(Type is ResizeHandleType.Right or ResizeHandleType.TopRight or ResizeHandleType.BottomRight) {
            newSize.x = Mathf.Max(minW, startSize.x + delta.x);
        } else if(Type is ResizeHandleType.Left or ResizeHandleType.TopLeft or ResizeHandleType.BottomLeft) {
            newSize.x = Mathf.Max(minW, startSize.x - delta.x);
        }

        if(Type is ResizeHandleType.Top or ResizeHandleType.TopLeft or ResizeHandleType.TopRight) {
            newSize.y = Mathf.Max(minH, startSize.y + delta.y);
        } else if(Type is ResizeHandleType.Bottom or ResizeHandleType.BottomLeft or ResizeHandleType.BottomRight) {
            newSize.y = Mathf.Max(minH, startSize.y - delta.y);
        }

        Vector2 sizeDiff = newSize - startSize;

        if(Type is ResizeHandleType.Right or ResizeHandleType.TopRight or ResizeHandleType.BottomRight) {
            newPos.x = startPos.x + (sizeDiff.x * (1f - pivot.x));
        } else if(Type is ResizeHandleType.Left or ResizeHandleType.TopLeft or ResizeHandleType.BottomLeft) {
            newPos.x = startPos.x - (sizeDiff.x * pivot.x);
        }

        if(Type is ResizeHandleType.Top or ResizeHandleType.TopLeft or ResizeHandleType.TopRight) {
            newPos.y = startPos.y + (sizeDiff.y * (1f - pivot.y));
        } else if(Type is ResizeHandleType.Bottom or ResizeHandleType.BottomLeft or ResizeHandleType.BottomRight) {
            newPos.y = startPos.y - (sizeDiff.y * pivot.y);
        }

        Panel.sizeDelta = newSize;
        Panel.anchoredPosition = newPos;
    }

    private static readonly ResizeHandleType[] HandleOrder = {
        ResizeHandleType.TopLeft,
        ResizeHandleType.Top,
        ResizeHandleType.TopRight,

        ResizeHandleType.Left,
        ResizeHandleType.Right,

        ResizeHandleType.BottomLeft,
        ResizeHandleType.Bottom,
        ResizeHandleType.BottomRight
    };

    // Edge band thickness (the ring that sits OUTSIDE the panel) and the corner
    // square size. The handles live on a frame parented to the canvas — not the
    // panel — so the panel's RectMask2D no longer clips them and they never
    // overlap interior controls like the close button.
    private const float RING = 12f;
    private const float CORNER = 20f;

    public static void CreateResizeHandles(RectTransform panel, RectTransform panelParent) {
        // Frame sits on the canvas, mirrors the panel's centre, and is RING
        // larger on every side so its outer band falls just outside the panel.
        GameObject frameObj = new("ResizeFrame");
        frameObj.transform.SetParent(panelParent, false);

        RectTransform frame = frameObj.AddComponent<RectTransform>();
        frame.anchorMin = panel.anchorMin;
        frame.anchorMax = panel.anchorMax;
        frame.pivot = panel.pivot;

        // Child container so the frame root can stay active (and keep ticking
        // LateUpdate) while the handles themselves are shown/hidden with the menu.
        GameObject handlesObj = new("Handles");
        handlesObj.transform.SetParent(frame, false);
        RectTransform handles = handlesObj.AddComponent<RectTransform>();
        handles.anchorMin = Vector2.zero;
        handles.anchorMax = Vector2.one;
        handles.offsetMin = Vector2.zero;
        handles.offsetMax = Vector2.zero;

        foreach(ResizeHandleType type in HandleOrder) {
            GameObject handle = new($"Resize_{type}");
            handle.transform.SetParent(handles, false);

            RectTransform rect = handle.AddComponent<RectTransform>();

            switch(type) {
                case ResizeHandleType.Top:
                    rect.anchorMin = new(0, 1);
                    rect.anchorMax = new(1, 1);
                    rect.offsetMin = new(CORNER, -RING);
                    rect.offsetMax = new(-CORNER, 0f);
                    break;

                case ResizeHandleType.Bottom:
                    rect.anchorMin = new(0, 0);
                    rect.anchorMax = new(1, 0);
                    rect.offsetMin = new(CORNER, 0f);
                    rect.offsetMax = new(-CORNER, RING);
                    break;

                case ResizeHandleType.Left:
                    rect.anchorMin = new(0, 0);
                    rect.anchorMax = new(0, 1);
                    rect.offsetMin = new(0f, CORNER);
                    rect.offsetMax = new(RING, -CORNER);
                    break;

                case ResizeHandleType.Right:
                    rect.anchorMin = new(1, 0);
                    rect.anchorMax = new(1, 1);
                    rect.offsetMin = new(-RING, CORNER);
                    rect.offsetMax = new(0f, -CORNER);
                    break;

                case ResizeHandleType.TopLeft:
                    rect.anchorMin = rect.anchorMax = new(0, 1);
                    rect.pivot = new(0f, 1f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new(CORNER, CORNER);
                    break;

                case ResizeHandleType.TopRight:
                    rect.anchorMin = rect.anchorMax = new(1, 1);
                    rect.pivot = new(1f, 1f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new(CORNER, CORNER);
                    break;

                case ResizeHandleType.BottomLeft:
                    rect.anchorMin = rect.anchorMax = new(0, 0);
                    rect.pivot = new(0f, 0f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new(CORNER, CORNER);
                    break;

                case ResizeHandleType.BottomRight:
                    rect.anchorMin = rect.anchorMax = new(1, 0);
                    rect.pivot = new(1f, 0f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new(CORNER, CORNER);
                    break;
            }

            Image image = handle.AddComponent<Image>();
            image.color = Color.clear;

            ResizeHandle resize = handle.AddComponent<ResizeHandle>();
            resize.Type = type;
            resize.Panel = panel;
            resize.PanelParent = panelParent;
        }

        // Visible bottom-right grip so resizing stays discoverable now that the
        // hit zones are invisible and sit outside the panel.
        {
            GameObject grip = new("ResizeGrip");
            grip.transform.SetParent(handles, false);

            RectTransform gr = grip.AddComponent<RectTransform>();
            gr.anchorMin = new(1f, 0f);
            gr.anchorMax = new(1f, 0f);
            gr.pivot = new(1f, 0f);
            gr.anchoredPosition = new(-2f, 2f);
            gr.sizeDelta = new(16f, 16f);
            // Triangle128 points up; rotate so its right angle fills the corner.
            gr.localEulerAngles = new Vector3(0f, 0f, -135f);

            Image gi = grip.AddComponent<Image>();
            gi.sprite = MainCore.Spr.Get(UISprite.Triangle128);
            gi.color = new Color(1f, 1f, 1f, 0.35f);
            gi.preserveAspect = true;
            gi.raycastTarget = false;
        }

        ResizeFrame follow = frameObj.AddComponent<ResizeFrame>();
        follow.Panel = panel;
        follow.Self = frame;
        follow.Handles = handlesObj;
        follow.Ring = RING;
    }
}

// Keeps the canvas-level resize frame glued to the panel: matches its centre,
// grows by the ring on every side, and hides the handles while the menu is
// closed or in reorganize mode.
public sealed class ResizeFrame : MonoBehaviour {

    public RectTransform Panel;
    public RectTransform Self;
    public GameObject Handles;
    public float Ring;

    // Last-applied panel geometry; the frame only moves during a drag/resize/tween,
    // so on static frames skip the native RectTransform writes (which re-dirty the
    // transform and schedule a canvas/layout pass even for an identical value).
    private Vector2 lastPanelPos;
    private Vector2 lastPanelSize;
    private bool hasApplied;

    private void LateUpdate() {
        if(Panel == null || Self == null) {
            return;
        }

        Vector2 panelPos = Panel.anchoredPosition;
        Vector2 panelSize = Panel.sizeDelta;
        if(!hasApplied || panelPos != lastPanelPos || panelSize != lastPanelSize) {
            Self.anchoredPosition = panelPos;
            Self.sizeDelta = panelSize + new Vector2(Ring * 2f, Ring * 2f);
            lastPanelPos = panelPos;
            lastPanelSize = panelSize;
            hasApplied = true;
        }

        bool show = Panel.gameObject.activeInHierarchy
            && UICore.IsOpen
            && !UICore.IsReorganizing;

        if(Handles != null && Handles.activeSelf != show) {
            Handles.SetActive(show);
        }
    }
}
