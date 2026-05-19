using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple event bus for decoupled communication between systems.
/// Supports subscribing, unsubscribing, raising, and cancelling events.
///
/// CANCELLATION:
/// Any handler can cancel propagation by calling EventBus.Cancel<T>() inside
/// its callback. Remaining handlers in that Raise() call are skipped.
/// Cancellation is automatically cleared after each Raise().
/// You can also pre-cancel from outside: EventBus.Cancel<T>() before Raise<T>()
/// will prevent all handlers from running.
/// </summary>
public static class EventBus
{
    private static readonly Dictionary<Type, List<Delegate>> handlers = new();
    private static readonly HashSet<Type>                    _cancelled = new();

    // =========================================================
    // SUBSCRIBE / UNSUBSCRIBE
    // =========================================================
    public static void Subscribe<T>(Action<T> handler) where T : class
    {
        if (handler == null) return;
        var eventType = typeof(T);
        if (!handlers.ContainsKey(eventType))
            handlers[eventType] = new List<Delegate>();
        if (!handlers[eventType].Contains(handler))
            handlers[eventType].Add(handler);
    }

    public static void Unsubscribe<T>(Action<T> handler) where T : class
    {
        if (handler == null) return;
        if (handlers.TryGetValue(typeof(T), out var list))
            list.Remove(handler);
    }

    // =========================================================
    // RAISE
    // Returns true if the event completed without being cancelled.
    // =========================================================
    public static bool Raise<T>(T eventData) where T : class
    {
        if (eventData == null) return false;
        var eventType = typeof(T);

        // Pre-cancelled before Raise() was even called
        if (_cancelled.Contains(eventType))
        {
            _cancelled.Remove(eventType);
            return false;
        }

        if (!handlers.TryGetValue(eventType, out var list))
            return true;

        bool wasCancelled = false;
        foreach (var handler in list.ToArray())
        {
            // Check cancellation before each handler (set by previous handler)
            if (_cancelled.Contains(eventType))
            {
                wasCancelled = true;
                break;
            }
            try   { ((Action<T>)handler)?.Invoke(eventData); }
            catch (Exception e)
            {
                Debug.LogError($"[EventBus] Error invoking handler for {eventType.Name}: {e.Message}");
            }
        }

        _cancelled.Remove(eventType);
        return !wasCancelled;
    }

    // =========================================================
    // CANCELLATION
    // Call from inside a handler (or before Raise) to stop propagation.
    // =========================================================
    /// <summary>
    /// Cancels propagation of the current (or next) Raise&lt;T&gt;().
    /// Safe to call from inside a handler or from external code before Raise().
    /// </summary>
    public static void Cancel<T>() where T : class
        => _cancelled.Add(typeof(T));

    /// <summary>
    /// Cancel by runtime Type (used by EventBusListener via reflection).
    /// </summary>
    public static void Cancel(Type eventType)
        => _cancelled.Add(eventType);

    /// <summary>
    /// Returns true if this event type is currently flagged for cancellation.
    /// </summary>
    public static bool IsCancelled<T>() where T : class
        => _cancelled.Contains(typeof(T));

    public static bool IsCancelled(Type eventType)
        => _cancelled.Contains(eventType);

    // =========================================================
    // CLEAR
    // =========================================================
    public static void Clear()
    {
        handlers.Clear();
        _cancelled.Clear();
    }

    public static void Clear<T>() where T : class
    {
        if (handlers.ContainsKey(typeof(T)))
            handlers[typeof(T)].Clear();
        _cancelled.Remove(typeof(T));
    }

    public static bool HasSubscribers<T>() where T : class
    {
        var eventType = typeof(T);
        return handlers.TryGetValue(eventType, out var list) && list.Count > 0;
    }
}
