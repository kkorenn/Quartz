using System.Collections.Generic;
using Koren.Core;
using Koren.Resource;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Koren.UI.Utility;

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

    public const float MIN_WIDTH = 900f;
    public const float MIN_HEIGHT = 500f;

    private void Awake() {
        var trigger = gameObject.AddComponent<EventTrigger>();

        var downEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        downEntry.callback.AddListener(_ => OnPointerDownInternal());
        trigger.triggers.Add(downEntry);

        var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        dragEntry.callback.AddListener(_ => OnDragInternal());
        trigger.triggers.Add(dragEntry);

        var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enterEntry.callback.AddListener(_ => ApplyCursor());
        trigger.triggers.Add(enterEntry);

        var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(_ => ResetCursor());
        trigger.triggers.Add(exitEntry);

        var upEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        upEntry.callback.AddListener(_ => ResetCursor());
        trigger.triggers.Add(upEntry);
    }

    private void OnDisable() => ResetCursor();

    // ===== resize cursor =====

    // One generated double-headed arrow per orientation, cached by angle.
    private static readonly Dictionary<int, Texture2D> cursorCache = new();
    private static bool cursorActive;

    private int CursorAngle => Type switch {
        ResizeHandleType.Left or ResizeHandleType.Right => 0,
        ResizeHandleType.Top or ResizeHandleType.Bottom => 90,
        ResizeHandleType.TopRight or ResizeHandleType.BottomLeft => 45,
        _ => 135, // TopLeft / BottomRight
    };

    private void ApplyCursor() {
        Texture2D tex = GetCursor(CursorAngle);
        if(tex != null) {
            Cursor.SetCursor(tex, new Vector2(tex.width * 0.5f, tex.height * 0.5f), CursorMode.Auto);
            cursorActive = true;
        }
    }

    private static void ResetCursor() {
        if(cursorActive) {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            cursorActive = false;
        }
    }

    private static Texture2D GetCursor(int angleDeg) {
        if(cursorCache.TryGetValue(angleDeg, out Texture2D cached)) {
            return cached;
        }

        const int size = 32;
        const float c = (size - 1) * 0.5f;
        float rad = angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        Texture2D tex = new(size, size, TextureFormat.RGBA32, false);
        Color clear = new(0f, 0f, 0f, 0f);

        for(int y = 0; y < size; y++) {
            for(int x = 0; x < size; x++) {
                float px = x - c;
                float py = y - c;
                // Rotate into the arrow's axis (u along the arrow, v across).
                float u = (px * cos) + (py * sin);
                float v = (-px * sin) + (py * cos);

                Color col = clear;
                // Black outline first, white core on top.
                if(IsArrow(u, v, 14f, 6f, 5f, 2.6f)) {
                    col = Color.black;
                }
                if(IsArrow(u, v, 13f, 5f, 4f, 1.5f)) {
                    col = Color.white;
                }
                tex.SetPixel(x, y, col);
            }
        }

        tex.Apply(false);
        tex.filterMode = FilterMode.Bilinear;
        cursorCache[angleDeg] = tex;
        return tex;
    }

    // Double-headed arrow test: a shaft of half-thickness t out to (ltip - hh),
    // then a triangular head tapering to the tip at ltip.
    private static bool IsArrow(float u, float v, float ltip, float hh, float headHalf, float t) {
        float au = Mathf.Abs(u);
        float shaft = ltip - hh;
        if(au <= shaft) {
            return Mathf.Abs(v) <= t;
        }
        if(au <= ltip) {
            return Mathf.Abs(v) <= headHalf * (ltip - au) / hh;
        }
        return false;
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

    // Handles sit fully INSIDE the panel: the panel's RectMask2D culls (and
    // stops raycasts on) anything past its edge, so edge-straddling handles
    // only caught clicks on their inner half. Corners are square hit zones;
    // edges are strips between the corners.
    private const float HANDLE_CORNER = 40f;
    private const float HANDLE_SIDE = 22f;
    public static void CreateResizeHandles(RectTransform panel, RectTransform panelParent) {
        foreach(ResizeHandleType type in HandleOrder) {
            GameObject handle = new($"Resize_{type}");
            handle.transform.SetParent(panel, false);

            RectTransform rect =
                handle.AddComponent<RectTransform>();

            switch(type) {
                case ResizeHandleType.Top:
                    rect.anchorMin = new(0, 1);
                    rect.anchorMax = new(1, 1);
                    rect.pivot = new(0.5f, 1f);
                    rect.offsetMin = new(HANDLE_CORNER, -HANDLE_SIDE);
                    rect.offsetMax = new(-HANDLE_CORNER, 0f);
                    break;

                case ResizeHandleType.Bottom:
                    rect.anchorMin = new(0, 0);
                    rect.anchorMax = new(1, 0);
                    rect.pivot = new(0.5f, 0f);
                    rect.offsetMin = new(HANDLE_CORNER, 0f);
                    rect.offsetMax = new(-HANDLE_CORNER, HANDLE_SIDE);
                    break;

                case ResizeHandleType.Left:
                    rect.anchorMin = new(0, 0);
                    rect.anchorMax = new(0, 1);
                    rect.pivot = new(0f, 0.5f);
                    rect.offsetMin = new(0f, HANDLE_CORNER);
                    rect.offsetMax = new(HANDLE_SIDE, -HANDLE_CORNER);
                    break;

                case ResizeHandleType.Right:
                    rect.anchorMin = new(1, 0);
                    rect.anchorMax = new(1, 1);
                    rect.pivot = new(1f, 0.5f);
                    rect.offsetMin = new(-HANDLE_SIDE, HANDLE_CORNER);
                    rect.offsetMax = new(0f, -HANDLE_CORNER);
                    break;

                case ResizeHandleType.TopLeft:
                    rect.anchorMin = rect.anchorMax = new(0, 1);
                    rect.pivot = new(0f, 1f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new(HANDLE_CORNER, HANDLE_CORNER);
                    break;

                case ResizeHandleType.TopRight:
                    rect.anchorMin = rect.anchorMax = new(1, 1);
                    rect.pivot = new(1f, 1f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new(HANDLE_CORNER, HANDLE_CORNER);
                    break;

                case ResizeHandleType.BottomLeft:
                    rect.anchorMin = rect.anchorMax = new(0, 0);
                    rect.pivot = new(0f, 0f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new(HANDLE_CORNER, HANDLE_CORNER);
                    break;

                case ResizeHandleType.BottomRight:
                    rect.anchorMin = rect.anchorMax = new(1, 0);
                    rect.pivot = new(1f, 0f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new(HANDLE_CORNER, HANDLE_CORNER);
                    break;
            }

            Image image = handle.AddComponent<Image>();
            image.sprite = MainCore.Spr.Get(UISprite.Circle256);
            image.color = Color.clear;

            ResizeHandle resize = handle.AddComponent<ResizeHandle>();

            resize.Type = type;
            resize.Panel = panel;
            resize.PanelParent = panelParent;
        }

        // Visible bottom-right grip so resizing is discoverable. The corner
        // handles above straddle the panel edge and get culled by the panel's
        // RectMask2D; this grip sits fully inside the corner so it shows and
        // stays grabbable.
        {
            GameObject grip = new("ResizeGrip");
            grip.transform.SetParent(panel, false);

            RectTransform gr = grip.AddComponent<RectTransform>();
            gr.anchorMin = new(1f, 0f);
            gr.anchorMax = new(1f, 0f);
            gr.pivot = new(1f, 0f);
            gr.anchoredPosition = new(-5f, 5f);
            gr.sizeDelta = new(20f, 20f);
            // Triangle128 points up; rotate so its right angle fills the corner.
            gr.localEulerAngles = new Vector3(0f, 0f, -135f);

            Image gi = grip.AddComponent<Image>();
            gi.sprite = MainCore.Spr.Get(UISprite.Triangle128);
            gi.color = new Color(1f, 1f, 1f, 0.35f);
            gi.preserveAspect = true;

            ResizeHandle grh = grip.AddComponent<ResizeHandle>();
            grh.Type = ResizeHandleType.BottomRight;
            grh.Panel = panel;
            grh.PanelParent = panelParent;
        }
    }
}
