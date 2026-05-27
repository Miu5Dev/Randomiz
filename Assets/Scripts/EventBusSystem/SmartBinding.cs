using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// =========================================================
// SMART BINDING
// Maps N event fields → a component method with N params,
// with optional per-binding condition logic (If/ElseIf/Else).
// =========================================================
[Serializable]
public enum ParamSourceMode
{
    EventField,
    FixedValue,
    ComponentField,
    WholeEvent,
    Toggle,
    ObjectReference,
    CallerObject,   // the GameObject that called Use() directly
    CallerRoot      // the caller's transform.root.gameObject
}

[Serializable]
public class ParamSource
{
    public ParamSourceMode      mode            = ParamSourceMode.EventField;
    public string               eventFieldName  = "";
    public UnityEngine.Object   objectReference = null;   // used when mode == ObjectReference
    public string          fixedValue     = "";
    public string          componentMember = "";
}

public enum BindingLogic { If, ElseIf, Else }

[Serializable]
public class SmartBinding
{
    public UnityEngine.Object  targetObject;
    public string              methodName   = "";
    public List<ParamSource>   paramSources = new();
    public List<BindingCondition> conditions = new();
    public BindingLogic        logic        = BindingLogic.If;

    public bool IsConfigured() =>
        targetObject != null && !string.IsNullOrEmpty(methodName);

    // ── CompileSplit ──────────────────────────────────────────────────────
    /// <summary>Returns separate check and execute delegates for chain evaluation.</summary>
    public (Func<object, bool> check, Action<object> execute) CompileSplit(Type eventType, GameObject callerObject = null)
    {
        if (!IsConfigured()) return (null, null);

        var method = ResolveMethod(eventType);
        if (method == null) return (null, null);

        var resolvers = BuildResolvers(method, eventType, callerObject);
        if (resolvers == null) return (null, null);

        var compiledConditions = conditions
            .Where(c => c.enabled)
            .Select(c => c.Compile(eventType, targetObject))
            .Where(fn => fn != null)
            .ToList();

        Func<object, bool> checkFn =
            evtObj => compiledConditions.Count == 0 || compiledConditions.All(fn => fn(evtObj));

        var capturedTarget   = targetObject;
        var capturedMethod   = method;
        var capturedResolvers = resolvers;
        Action<object> executeFn = evtObj =>
        {
            var args = new object[capturedResolvers.Length];
            for (int i = 0; i < capturedResolvers.Length; i++)
                args[i] = capturedResolvers[i](evtObj);
            capturedMethod.Invoke(capturedTarget, args);
        };

        return (checkFn, executeFn);
    }

    // ── Compile (convenience) ─────────────────────────────────────────────
    public Action<object> Compile(Type eventType, GameObject callerObject = null)
    {
        var (check, execute) = CompileSplit(eventType, callerObject);
        if (check == null || execute == null) return null;
        return evtObj => { if (check(evtObj)) execute(evtObj); };
    }

    // ── Private helpers ───────────────────────────────────────────────────
    private MethodInfo ResolveMethod(Type eventType)
    {
        var targetType = targetObject.GetType();
        foreach (var m in targetType.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (m.Name == methodName && m.GetParameters().Length == paramSources.Count)
                return m;
        }
        Debug.LogWarning($"[SmartBinding] Method '{methodName}' not found on {targetType.Name}");
        return null;
    }

    private Func<object, object>[] BuildResolvers(MethodInfo method, Type eventType, GameObject callerObject)
    {
        var mParams   = method.GetParameters();
        var resolvers = new Func<object, object>[paramSources.Count];
        var targetType = targetObject.GetType();

        for (int i = 0; i < paramSources.Count; i++)
        {
            var ps    = paramSources[i];
            var pType = mParams[i].ParameterType;

            switch (ps.mode)
            {
                case ParamSourceMode.WholeEvent:
                    if (!pType.IsAssignableFrom(eventType))
                    {
                        Debug.LogWarning(
                            $"[SmartBinding] WholeEvent: '{eventType.Name}' is not assignable to '{pType.Name}'");
                        return null;
                    }
                    resolvers[i] = evtObj => evtObj;
                    break;

                case ParamSourceMode.EventField:
                {
                    var evtMember = GetMember(eventType, ps.eventFieldName);
                    if (evtMember == null)
                    {
                        Debug.LogWarning($"[SmartBinding] Event field '{ps.eventFieldName}' not found");
                        return null;
                    }
                    var cap = evtMember;
                    resolvers[i] = evtObj =>
                        cap is FieldInfo fi ? fi.GetValue(evtObj) : ((PropertyInfo)cap).GetValue(evtObj);
                    break;
                }

                case ParamSourceMode.FixedValue:
                {
                    object parsed;
                    try   { parsed = TypeHelper.Parse(ps.fixedValue, pType); }
                    catch
                    {
                        Debug.LogWarning($"[SmartBinding] Could not parse '{ps.fixedValue}' as {pType.Name}");
                        return null;
                    }
                    var capParsed = parsed;
                    resolvers[i] = _ => capParsed;
                    break;
                }

                case ParamSourceMode.Toggle:
                {
                    if (pType != typeof(bool))
                    {
                        Debug.LogWarning($"[SmartBinding] Toggle only works with bool params, got {pType.Name}");
                        return null;
                    }
                    var toggleMember = ResolveComponentOrGoMember(
                        targetObject, targetType, ps.componentMember, out var capToggleTarget);
                    if (toggleMember == null)
                    {
                        Debug.LogWarning($"[SmartBinding] Toggle: member '{ps.componentMember}' not found");
                        return null;
                    }
                    var capTM = toggleMember; var capTT = capToggleTarget;
                    resolvers[i] = _ =>
                        !(bool)(capTM is FieldInfo tfi ? tfi.GetValue(capTT) : ((PropertyInfo)capTM).GetValue(capTT));
                    break;
                }

                case ParamSourceMode.ObjectReference:
                {
                    var capObjRef = ps.objectReference;
                    if (capObjRef != null && !pType.IsAssignableFrom(capObjRef.GetType()))
                    {
                        Debug.LogWarning(
                            $"[SmartBinding] ObjectReference type mismatch: {capObjRef.GetType().Name} → {pType.Name}");
                        return null;
                    }
                    resolvers[i] = _ => capObjRef;
                    break;
                }

                case ParamSourceMode.CallerObject:
                {
                    // Resolved at runtime from the caller passed to Use(caller)
                    if (pType == typeof(GameObject) || pType.IsAssignableFrom(typeof(GameObject)))
                    {
                        var capGO = callerObject;
                        resolvers[i] = _ => capGO;
                    }
                    else if (typeof(Component).IsAssignableFrom(pType))
                    {
                        resolvers[i] = _ =>
                        {
                            if (callerObject == null) return null;
                            var comp = callerObject.GetComponent(pType);
                            if (comp == null)
                                Debug.LogWarning(
                                    $"[SmartBinding] CallerObject: no {pType.Name} on {callerObject.name}");
                            return comp;
                        };
                    }
                    else
                    {
                        Debug.LogWarning($"[SmartBinding] CallerObject: {pType.Name} must be GameObject or Component.");
                        return null;
                    }
                    break;
                }
                
                case ParamSourceMode.CallerRoot:
                {
                    if (pType == typeof(GameObject) || pType.IsAssignableFrom(typeof(GameObject)))
                    {
                        resolvers[i] = _ =>
                        {
                            if (callerObject == null) return null;
                            return callerObject.transform.root.gameObject;
                        };
                    }
                    else if (typeof(Component).IsAssignableFrom(pType))
                    {
                        resolvers[i] = _ =>
                        {
                            if (callerObject == null) return null;
                            var root = callerObject.transform.root.gameObject;
                            var comp = root.GetComponent(pType);
                            if (comp == null)
                                Debug.LogWarning(
                                    $"[SmartBinding] CallerRoot: no {pType.Name} on root '{root.name}'");
                            return comp;
                        };
                    }
                    else
                    {
                        Debug.LogWarning($"[SmartBinding] CallerRoot: {pType.Name} must be GameObject or Component.");
                        return null;
                    }
                    break;
                }

                default: // ComponentField
                {
                    var compMember = ResolveComponentOrGoMember(
                        targetObject, targetType, ps.componentMember, out var capTarget);
                    if (compMember == null)
                    {
                        Debug.LogWarning($"[SmartBinding] Member '{ps.componentMember}' not found");
                        return null;
                    }
                    var capMem = compMember; var capT = capTarget;
                    resolvers[i] = _ =>
                        capMem is FieldInfo fi ? fi.GetValue(capT) : ((PropertyInfo)capMem).GetValue(capT);
                    break;
                }
            }
        }
        return resolvers;
    }

    // ── Reflection utilities ──────────────────────────────────────────────
    private static MemberInfo GetMember(Type type, string name,
        BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
        => (MemberInfo)type.GetField(name, flags)
        ?? type.GetProperty(name, flags);

    private static MemberInfo ResolveComponentOrGoMember(
        UnityEngine.Object target, Type targetType, string memberName,
        out UnityEngine.Object resolvedTarget)
    {
        resolvedTarget = target;

        var compMember = GetMember(targetType, memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        if (compMember != null) return compMember;

        // Fallback to GameObject properties
        var goMember = GetMember(typeof(GameObject), memberName);
        if (goMember != null && target is Component c)
            resolvedTarget = c.gameObject;
        return goMember;
    }
}
