using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Interactable object. Call Use() to trigger all bindings and the onUse callback.
/// Reuses SmartBinding/BindingCondition from the EventBus system — no duplicate logic.
/// Only FixedValue, ComponentField and Toggle param sources are valid here
/// (EventField and WholeEvent require an event payload, which Use() does not have).
/// </summary>
public class Interactable : MonoBehaviour
{
    [SerializeField] private UnityEvent onUse;
    [HideInInspector] public List<SmartBinding> useBindings = new();

    /// <summary>
    /// Register a callback to be invoked when Use() is called.
    /// Use this instead of accessing onUse directly (it is serialized/private).
    /// </summary>
    public void AddUseListener(UnityEngine.Events.UnityAction callback)
        => onUse ??= new UnityEvent();  // ensure not null before adding

    // Actual add happens via the getter below — keeping onUse private for inspector safety.
    // Callers: DoorController, NPCController, etc.
    public UnityEvent OnUse => onUse ??= new UnityEvent();

    public void Use(GameObject caller = null)
    {
        bool chainFired = false;
        foreach (var binding in useBindings)
        {
            if (!binding.IsConfigured()) continue;
            try
            {
                // CompileSplit needs a type — use the sentinel so no EventField
                // resolvers are built (param sources are FixedValue/ComponentField/Toggle).
                var (check, execute) = binding.CompileSplit(typeof(InteractableDummy), caller);
                if (check == null || execute == null) continue;

                switch (binding.logic)
                {
                    case BindingLogic.If:
                        chainFired = check(null);
                        if (chainFired) execute(null);
                        break;
                    case BindingLogic.ElseIf:
                        if (!chainFired && check(null)) { chainFired = true; execute(null); }
                        break;
                    case BindingLogic.Else:
                        if (!chainFired) { execute(null); chainFired = true; }
                        break;
                }
            }
            catch (Exception e) { Debug.LogError($"[Interactable] {e}"); }
        }
        onUse?.Invoke();
    }
}

/// <summary>Sentinel type used as dummy event type for CompileSplit in Interactable.</summary>
public sealed class InteractableDummy { }
