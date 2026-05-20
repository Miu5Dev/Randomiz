using System;
using System.Collections.Generic;
using UnityEngine;

public static class EventBus
{
    // ── Estructura interna con prioridad ──────────────────────────────────
    private struct HandlerEntry
    {
        public Delegate handler;
        public int      priority; // Mayor número = se ejecuta ANTES
    }

    private static readonly Dictionary<Type, List<HandlerEntry>> _handlers  = new();
    private static readonly HashSet<Type>                        _cancelled = new();

    // Stack de profundidad para aislar cancelaciones por Raise anidado
    private static readonly Dictionary<Type, int> _raiseDepth = new();

    // =========================================================
    // SUBSCRIBE / UNSUBSCRIBE
    // =========================================================

    /// <param name="priority">Mayor = más prioritario. Default 0. Puede ser negativo.</param>
    public static void Subscribe<T>(Action<T> handler, int priority = 0) where T : class
    {
        if (handler == null) return;
        var type = typeof(T);
        if (!_handlers.ContainsKey(type))
            _handlers[type] = new List<HandlerEntry>();

        var list = _handlers[type];
        if (list.Exists(e => e.handler == (Delegate)handler)) return; // ya registrado

        list.Add(new HandlerEntry { handler = handler, priority = priority });
        // Orden descendente: mayor prioridad primero
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

        // Pre-cancelado antes de llamar Raise()
        if (_cancelled.Contains(type))
        {
            _cancelled.Remove(type);
            return false;
        }

        if (!_handlers.TryGetValue(type, out var list) || list.Count == 0)
            return true;

        // Profundidad para Raises anidados del mismo tipo
        _raiseDepth.TryGetValue(type, out int depth);
        _raiseDepth[type] = depth + 1;

        bool wasCancelled = false;
        foreach (var entry in list.ToArray())
        {
            if (_cancelled.Contains(type))
            {
                wasCancelled = true;
                break;
            }
            try { ((Action<T>)entry.handler)?.Invoke(eventData); }
            catch (Exception e)
            {
                Debug.LogError($"[EventBus] Handler error on {type.Name}: {e.Message}\n{e.StackTrace}");
            }
        }

        // Solo limpia la cancelación si es el Raise más externo (no anidado)
        _raiseDepth[type] = depth; // restaura profundidad anterior
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
}