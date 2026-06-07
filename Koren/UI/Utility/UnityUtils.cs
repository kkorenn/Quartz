using UnityEngine.EventSystems;

namespace Koren.UI.Utility;

public class UnityUtils {
    public static void AddEvent(EventTriggerType type, Action<PointerEventData> cb, EventTrigger trigger) {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(e => cb((PointerEventData)e));
        trigger.triggers.Add(entry);
    }
}
