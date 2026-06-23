using UnityEngine;
using UnityEngine.EventSystems;

namespace Quartz.UI.Utility;

public class DragHandler : MonoBehaviour {

    private RectTransform rect;
    private Vector2 offset;

    private void Awake() {
        rect = transform.parent?.GetComponent<RectTransform>();
        SetupEvents();
    }

    private void SetupEvents() {
        var trigger = gameObject.AddComponent<EventTrigger>();

        var downEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        downEntry.callback.AddListener(_ => OnPointerDownInternal());
        trigger.triggers.Add(downEntry);

        var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        dragEntry.callback.AddListener(_ => OnDragInternal());
        trigger.triggers.Add(dragEntry);
    }

    private void OnPointerDownInternal() {
        if(rect == null) {
            rect = transform.parent?.GetComponent<RectTransform>();
        }

        if(rect == null) {
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rect.parent as RectTransform,
            Input.mousePosition,
            null,
            out Vector2 localPoint
        );
        offset = rect.anchoredPosition - localPoint;
    }

    private void OnDragInternal() {
        if(rect == null) {
            rect = transform.parent?.GetComponent<RectTransform>();
        }

        if(rect == null) {
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rect.parent as RectTransform,
            Input.mousePosition,
            null,
            out Vector2 localPoint
        );
        rect.anchoredPosition = localPoint + offset;
    }
}
