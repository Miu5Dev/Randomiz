using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple event bus for decoupled communication between systems.
/// Supports subscribing, unsubscribing, and raising events.
/// </summary>
public static class EventBus
{
    private static readonly Dictionary<Type, List<Delegate>> handlers = new();

    public static void Subscribe<T>(Action<T> handler) where T : class
    {
        if (handler == null) return;
        Type eventType = typeof(T);
        if (!handlers.ContainsKey(eventType))
            handlers[eventType] = new List<Delegate>();
        if (!handlers[eventType].Contains(handler))
            handlers[eventType].Add(handler);
    }

    public static void Unsubscribe<T>(Action<T> handler) where T : class
    {
        if (handler == null) return;
        Type eventType = typeof(T);
        if (handlers.TryGetValue(eventType, out var list))
            list.Remove(handler);
    }

    public static void Raise<T>(T eventData) where T : class
    {
        if (eventData == null) return;
        Type eventType = typeof(T);
        if (handlers.TryGetValue(eventType, out var list))
        {
            foreach (var handler in list.ToArray())
            {
                try { ((Action<T>)handler)?.Invoke(eventData); }
                catch (Exception e)
                {
                    Debug.LogError($"[EventBus] Error invoking handler for {eventType.Name}: {e.Message}");
                }
            }
        }
    }

    public static void Clear()
    {
        handlers.Clear();
    }

    public static void Clear<T>() where T : class
    {
        Type eventType = typeof(T);
        if (handlers.ContainsKey(eventType))
            handlers[eventType].Clear();
    }

    public static bool HasSubscribers<T>() where T : class
    {
        Type eventType = typeof(T);
        return handlers.TryGetValue(eventType, out var list) && list.Count > 0;
    }
}
