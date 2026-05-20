using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Visual listener for the EventBus.
/// Supports direct method binding with typed parameters extracted from the event fields.
/// </summary>
public class EventBusListener : MonoBehaviour
{
    [HideInInspector] public string selectedEventTypeName = "";

    // Classic no-param callbacks
    public UnityEvent onRaised;
    public UnityEvent onReleased;
    public bool callOnBothStates = false;
    
    [Tooltip("Listeners con mayor prioridad se ejecutan primero y pueden cancelar a los de menor prioridad.\n" +
             "Default: 0. Puede ser negativo para ejecutarse al final.")]
    public int priority = 0;

    // Smart bindings: each one maps N event fields → a method with N params
    [HideInInspector] public List<SmartBinding> smartBindings = new();

    // ── Cancellation ──────────────────────────────────────────────────────
    [Tooltip("If true, this listener will cancel event propagation after its callbacks fire.\n" +
             "Remaining listeners subscribed to the same event will NOT be called.")]
    public bool cancelAfterHandle = false;

    [Tooltip("Only valid when cancelAfterHandle = true.\n" +
             "Only cancels if at least one If/ElseIf binding actually executed.\n" +
             "If false, always cancels regardless of whether smart bindings matched.")]
    public bool cancelOnlyIfBindingFired = true;

    // =========================================================
    // TYPE CACHE
    // =========================================================
    private static Dictionary<string, Type> _typeCache;

    public static void InvalidateTypeCache() => _typeCache = null;

    private static void RebuildTypeCache()
    {
        _typeCache = new Dictionary<string, Type>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            try
            {
                foreach (var t in asm.GetTypes())
                    if (!_typeCache.ContainsKey(t.Name)) _typeCache[t.Name] = t;
            }
            catch { }
    }

    public static Type ResolveType(string typeName)
    {
        if (_typeCache == null) RebuildTypeCache();
        if (_typeCache.TryGetValue(typeName, out var type)) return type;
        RebuildTypeCache();
        return _typeCache.TryGetValue(typeName, out type) ? type : null;
    }

    // ── Bool member helpers (for button events) ───────────────────────────
    public static MemberInfo GetBoolMember(Type t)
    {
        const string P = "pressed";
        return (MemberInfo)t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                   .FirstOrDefault(f => f.FieldType == typeof(bool) &&
                                        f.Name.Equals(P, StringComparison.OrdinalIgnoreCase))
               ?? t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   .FirstOrDefault(p => p.PropertyType == typeof(bool) &&
                                        p.Name.Equals(P, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ReadBool(MemberInfo m, object obj)
    {
        if (m is FieldInfo fi)    return (bool)fi.GetValue(obj);
        if (m is PropertyInfo pi) return (bool)pi.GetValue(obj);
        return true;
    }

    // =========================================================
    // RUNTIME STATE
    // =========================================================
    private Delegate       _subscribedDelegate;
    private MethodInfo     _unsubscribeMethod;

    // Synthetic release on disable
    private bool           _wasPressed;
    private MemberInfo     _boolMemberRef;
    private Type           _subscribedEventType;
    private Action<object> _innerCallback;

    // =========================================================
    // LIFECYCLE
    // =========================================================
    private void OnEnable()
    {
        Unsubscribe();
        Subscribe(selectedEventTypeName);
    }

    private void OnDisable()
    {
        // If the GameObject is disabled while pressed=true, emit a synthetic release
        // so the game doesn't think the button is still held.
        if (_wasPressed && _boolMemberRef != null && !callOnBothStates && _innerCallback != null)
        {
            try
            {
                var syntheticEvt = Activator.CreateInstance(_subscribedEventType);
                if (_boolMemberRef is FieldInfo sfi)        sfi.SetValue(syntheticEvt, false);
                else if (_boolMemberRef is PropertyInfo spi) spi.SetValue(syntheticEvt, false);
                _innerCallback.Invoke(syntheticEvt);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EventBusListener] Could not send synthetic release: {ex.Message}");
            }
        }

        Unsubscribe();
        _wasPressed = false;
    }

    // =========================================================
    // SUBSCRIBE / UNSUBSCRIBE
    // =========================================================
    private void Subscribe(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return;

        var eventType = ResolveType(typeName);
        if (eventType == null)
        {
            Debug.LogWarning($"[EventBusListener] Type not found: '{typeName}'");
            return;
        }

        var  boolMember    = GetBoolMember(eventType);
        bool isButtonEvent = boolMember != null;

        _boolMemberRef       = boolMember;
        _subscribedEventType = eventType;

        // Pre-compile all smart bindings
        var splitBindings = smartBindings
            .Where(b => b.IsConfigured())
            .Select(b => b.CompileSplit(eventType))
            .Where(p => p.check != null && p.execute != null)
            .ToList();

        var logicModes = smartBindings
            .Where(b => b.IsConfigured())
            .Select(b => b.logic)
            .ToList();

        Action<object> callback = evtObj =>
        {
            // Phase 1: snapshot all conditions
            var results = new bool[splitBindings.Count];
            for (int idx = 0; idx < splitBindings.Count; idx++)
                results[idx] = splitBindings[idx].check(evtObj);

            // Phase 2: execute with If/ElseIf/Else chain
            bool chainFired = false;
            for (int idx = 0; idx < splitBindings.Count; idx++)
            {
                var logic = logicModes[idx];
                if (logic == BindingLogic.If)
                {
                    chainFired = results[idx];
                    if (chainFired) splitBindings[idx].execute(evtObj);
                }
                else if (logic == BindingLogic.ElseIf)
                {
                    if (!chainFired && results[idx]) { chainFired = true; splitBindings[idx].execute(evtObj); }
                }
                else // Else
                {
                    if (!chainFired) { splitBindings[idx].execute(evtObj); chainFired = true; }
                }
            }

            if (!isButtonEvent)
            {
                onRaised?.Invoke();
                TryCancelEvent(chainFired);
                return;
            }

            bool pressed = ReadBool(boolMember, evtObj);
            if (callOnBothStates) { onRaised?.Invoke(); TryCancelEvent(chainFired); return; }

            _wasPressed = pressed;
            if (pressed) onRaised?.Invoke();
            else         onReleased?.Invoke();

            TryCancelEvent(chainFired);
        };

        _innerCallback = callback;

        // Wrap in Action<TEvent> and subscribe via reflection
        _subscribedDelegate = (Delegate)typeof(EventBusListener)
            .GetMethod(nameof(CreateTypedCallback), BindingFlags.NonPublic | BindingFlags.Static)
            .MakeGenericMethod(eventType)
            .Invoke(null, new object[] { callback });

        _unsubscribeMethod = typeof(EventBus)
            .GetMethod("Unsubscribe", BindingFlags.Public | BindingFlags.Static)
            .MakeGenericMethod(eventType);

        typeof(EventBus)
            .GetMethod("Subscribe", BindingFlags.Public | BindingFlags.Static)
            .MakeGenericMethod(eventType)
            .Invoke(null, new object[] { _subscribedDelegate, priority });
    }

    // ── Cancellation helper ───────────────────────────────────────────────
    private void TryCancelEvent(bool bindingFired)
    {
        if (!cancelAfterHandle) return;
        if (cancelOnlyIfBindingFired && !bindingFired) return;
        EventBus.Cancel(_subscribedEventType);
    }

    private static Action<T> CreateTypedCallback<T>(Action<object> inner) => evt => inner(evt);

    private void Unsubscribe()
    {
        if (_subscribedDelegate == null || _unsubscribeMethod == null) return;
        _unsubscribeMethod.Invoke(null, new object[] { _subscribedDelegate });
        _subscribedDelegate = null;
        _unsubscribeMethod  = null;
        _innerCallback      = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (!enabled || !gameObject.activeInHierarchy) { Unsubscribe(); return; }
        Unsubscribe();
        Subscribe(selectedEventTypeName);
    }
#endif
}
