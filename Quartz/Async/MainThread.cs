using Quartz.Core;
using System.Collections.Concurrent;
using UnityEngine;

namespace Quartz.Async;

public class MainThread : MonoBehaviour {
    private static readonly ConcurrentQueue<Action> queue = new();

    public static void Enqueue(Action action) {
        if(action == null) {
            return;
        }

        queue.Enqueue(action);
    }

    private void Update() {
        while(queue.TryDequeue(out Action action)) {
            try {
                action();
            } catch(Exception e) {
                // Sink for every queued main-thread callback — log type + stack,
                // not just Message, or a background-originated NRE is unreadable.
                MainCore.Log.Err(e.ToString());
            }
        }
    }
}