using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Central static publish/subscribe hub. Handlers run by priority (higher first) and may cancel an event for the rest of a raise. Uses a pooled buffer per raise to avoid allocations and supports nested raises.</summary>
public static class EventBus
{
    // ── Estructura interna con prioridad ──────────────────────────────────
    private struct HandlerEntry
    {
        public Delegate handler;
        public int      priority; // Higher number = runs FIRST
    }

    private static readonly Dictionary<Type, List<HandlerEntry>> _handlers  = new();
    private static readonly HashSet<Type>                        _cancelled = new();

    // Per-type depth counter to isolate cancellation across nested Raise calls.
    private static readonly Dictionary<Type, int> _raiseDepth = new();

    // Pool of reusable handler-list buffers — avoids allocating a snapshot array per
    // Raise. Each Raise borrows a buffer, copies the current handlers into it,
    // iterates, then returns the buffer. Supports nested raises (each takes its own).
    private static readonly Stack<List<HandlerEntry>> _bufferPool = new();

    // =========================================================
    // SUBSCRIBE / UNSUBSCRIBE
    // =========================================================

    /// <param name="priority">Higher = higher priority. Default 0. Can be negative.</param>
    public static void Subscribe<T>(Action<T> handler, int priority = 0) where T : class
    {
        if (handler == null) return;
        var type = typeof(T);
        if (!_handlers.ContainsKey(type))
            _handlers[type] = new List<HandlerEntry>();

        var list = _handlers[type];
        if (list.Exists(e => e.handler == (Delegate)handler)) return; // already subscribed

        list.Add(new HandlerEntry { handler = handler, priority = priority });
        // Descending order: highest priority first.
        list.Sort((a, b) => b.priority.CompareTo(a.priority));
    }

    public static void Unsubscribe<T>(Action<T> handler) where T : class
    {
        if (handler == null) return;
        if (_handlers.TryGetValue(typeof(T), out var list))
            list.RemoveAll(e => e.handler == (Delegate)handler);
    }

    // =========================================================
    // RAISE
    // =========================================================
    public static bool Raise<T>(T eventData) where T : class
    {
        if (eventData == null) return false;
        var type = typeof(T);

        // Pre-cancellation set before Raise() was called.
        if (_cancelled.Contains(type))
        {
            _cancelled.Remove(type);
            return false;
        }

        if (!_handlers.TryGetValue(type, out var list) || list.Count == 0)
            return true;

        // Depth for nested Raises of the same type.
        _raiseDepth.TryGetValue(type, out int depth);
        _raiseDepth[type] = depth + 1;

        // Borrow a buffer from the pool, snapshot the current handlers into it.
        // This isolates iteration from subscribe/unsubscribe during the raise AND
        // avoids the per-call array allocation that list.ToArray() used to cause.
        var buffer = _bufferPool.Count > 0 ? _bufferPool.Pop() : new List<HandlerEntry>(8);
        buffer.Clear();
        for (int i = 0; i < list.Count; i++) buffer.Add(list[i]);

        bool wasCancelled = false;
        try
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                if (_cancelled.Contains(type))
                {
                    wasCancelled = true;
                    break;
                }
                try { ((Action<T>)buffer[i].handler)?.Invoke(eventData); }
                catch (Exception e)
                {
                    Debug.LogError($"[EventBus] Handler error on {type.Name}: {e.Message}\n{e.StackTrace}");
                }
            }
        }
        finally
        {
            buffer.Clear();
            _bufferPool.Push(buffer);
        }

        // Only clear cancellation on the outermost (non-nested) Raise.
        _raiseDepth[type] = depth;
        if (depth == 0)
            _cancelled.Remove(type);

        return !wasCancelled;
    }

    // =========================================================
    // CANCEL
    // =========================================================
    public static void Cancel<T>()    where T : class => _cancelled.Add(typeof(T));
    public static void Cancel(Type t)                 => _cancelled.Add(t);

    public static bool IsCancelled<T>() where T : class => _cancelled.Contains(typeof(T));
    public static bool IsCancelled(Type t)               => _cancelled.Contains(t);

    // =========================================================
    // CLEAR
    // =========================================================
    public static void Clear()
    {
        _handlers.Clear();
        _cancelled.Clear();
        _raiseDepth.Clear();
    }

    public static void Clear<T>() where T : class
    {
        if (_handlers.ContainsKey(typeof(T)))
            _handlers[typeof(T)].Clear();
        _cancelled.Remove(typeof(T));
    }

    public static bool HasSubscribers<T>() where T : class
        => _handlers.TryGetValue(typeof(T), out var list) && list.Count > 0;

    // Clears all static state when Unity enters Play mode so stale delegates
    // from previous Play sessions never accumulate in the editor.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void DomainReload() => Clear();
}