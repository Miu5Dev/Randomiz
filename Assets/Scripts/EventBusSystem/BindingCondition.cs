using System;
using System.Reflection;
using UnityEngine;

// =========================================================
// CONDITION
// Defines the condition types and evaluation logic used
// by SmartBinding to gate method execution.
// =========================================================
public enum ConditionOperator
{
    Equals, NotEquals, GreaterThan, LessThan, GreaterOrEqual, LessOrEqual
}

public enum ConditionSource
{
    EventField,                  // event field vs literal value
    ComponentField,              // event field vs component field
    ComponentVsLiteral,          // component field vs literal (no event field)
    ExternalObjectVsLiteral,     // external object field vs literal (no event field)
    EventFieldVsExternalObject   // event field vs external object field
}

[Serializable]
public class BindingCondition
{
    public bool             enabled            = false;
    public string           fieldName          = "";
    public ConditionOperator op                = ConditionOperator.Equals;
    public ConditionSource  source             = ConditionSource.EventField;
    public string           compareValue       = "";
    public string           componentFieldName = "";
    public UnityEngine.Object compareObject     = null;
    public string             compareObjectField = "";

    // ── Compile ───────────────────────────────────────────────────────────
    public Func<object, bool> Compile(Type eventType, UnityEngine.Object targetObject = null)
    {
        if (!enabled) return null;

        if (source == ConditionSource.ComponentVsLiteral)
            return CompileComponentVsLiteral(targetObject);
        if (source == ConditionSource.ExternalObjectVsLiteral)
            return CompileExternalObjectVsLiteral();
        if (source == ConditionSource.EventFieldVsExternalObject)
            return CompileEventFieldVsExternalObject(eventType);

        if (string.IsNullOrEmpty(fieldName)) return null;
        var leftMember = GetMember(eventType, fieldName);
        if (leftMember == null) return null;
        var leftType = GetMemberType(leftMember);

        return source == ConditionSource.EventField
            ? CompileEventVsLiteral(leftMember, leftType)
            : CompileEventVsComponent(leftMember, leftType, targetObject);
    }

    // ── Compile helpers ───────────────────────────────────────────────────
    private Func<object, bool> CompileComponentVsLiteral(UnityEngine.Object targetObject)
    {
        if (targetObject == null || string.IsNullOrEmpty(componentFieldName)) return null;

        var compMember = ResolveComponentOrGoMember(targetObject, componentFieldName, out var resolvedTarget);
        if (compMember == null) return null;

        var compFieldType = GetMemberType(compMember);
        object parsedLiteral;
        try   { parsedLiteral = TypeHelper.Parse(compareValue, compFieldType); }
        catch { return null; }

        var capMem = compMember; var capTarget = resolvedTarget;
        var capOp = op; var capLit = parsedLiteral; var capType = compFieldType;
        return _ =>
        {
            object val = capMem is FieldInfo fi ? fi.GetValue(capTarget) : ((PropertyInfo)capMem).GetValue(capTarget);
            return Evaluate(val, capLit, capOp, capType);
        };
    }

    private Func<object, bool> CompileEventVsLiteral(MemberInfo leftMember, Type leftType)
    {
        object parsedValue;
        try   { parsedValue = TypeHelper.Parse(compareValue, leftType); }
        catch { return null; }

        var capLeft = leftMember; var capOp = op; var capVal = parsedValue; var capType = leftType;
        return evtObj =>
        {
            object lval = capLeft is FieldInfo fi ? fi.GetValue(evtObj) : ((PropertyInfo)capLeft).GetValue(evtObj);
            return Evaluate(lval, capVal, capOp, capType);
        };
    }

    private Func<object, bool> CompileEventVsComponent(MemberInfo leftMember, Type leftType, UnityEngine.Object targetObject)
    {
        if (targetObject == null || string.IsNullOrEmpty(componentFieldName)) return null;
        var ct = targetObject.GetType();
        var rightMember = GetMember(ct, componentFieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        if (rightMember == null) return null;

        var capLeft = leftMember; var capRight = rightMember;
        var capTarget = targetObject; var capOp = op; var capType = leftType;
        return evtObj =>
        {
            object lval = capLeft  is FieldInfo lf ? lf.GetValue(evtObj)    : ((PropertyInfo)capLeft).GetValue(evtObj);
            object rval = capRight is FieldInfo rf ? rf.GetValue(capTarget)  : ((PropertyInfo)capRight).GetValue(capTarget);
            return Evaluate(lval, rval, capOp, capType);
        };
    }

    private Func<object, bool> CompileExternalObjectVsLiteral()
    {
        if (compareObject == null || string.IsNullOrEmpty(compareObjectField)) return null;
        var mem = ResolveComponentOrGoMember(compareObject, compareObjectField, out var resolvedTarget);
        if (mem == null) return null;
        var memType = GetMemberType(mem);
        object parsed;
        try   { parsed = TypeHelper.Parse(compareValue, memType); }
        catch { return null; }
        var capMem = mem; var capTarget = resolvedTarget; var capOp = op; var capLit = parsed; var capType = memType;
        return _ =>
        {
            object val = capMem is FieldInfo fi ? fi.GetValue(capTarget) : ((PropertyInfo)capMem).GetValue(capTarget);
            return Evaluate(val, capLit, capOp, capType);
        };
    }

    private Func<object, bool> CompileEventFieldVsExternalObject(Type eventType)
    {
        if (compareObject == null || string.IsNullOrEmpty(compareObjectField) || string.IsNullOrEmpty(fieldName)) return null;
        var leftMember = GetMember(eventType, fieldName);
        if (leftMember == null) return null;
        var leftType = GetMemberType(leftMember);
        var rightMem = ResolveComponentOrGoMember(compareObject, compareObjectField, out var resolvedTarget);
        if (rightMem == null) return null;
        var capLeft = leftMember; var capRight = rightMem; var capTarget = resolvedTarget; var capOp = op; var capType = leftType;
        return evtObj =>
        {
            object lval = capLeft  is FieldInfo lfi ? lfi.GetValue(evtObj)   : ((PropertyInfo)capLeft).GetValue(evtObj);
            object rval = capRight is FieldInfo rfi ? rfi.GetValue(capTarget) : ((PropertyInfo)capRight).GetValue(capTarget);
            return Evaluate(lval, rval, capOp, capType);
        };
    }

    // ── Evaluate ──────────────────────────────────────────────────────────
    private static bool Evaluate(object val, object reference, ConditionOperator op, Type type)
    {
        if (type == typeof(string) || type.IsEnum)
        {
            string a = val?.ToString() ?? ""; string b = reference?.ToString() ?? "";
            return op == ConditionOperator.Equals ? a == b : a != b;
        }
        if (type == typeof(bool))
        {
            bool a = Convert.ToBoolean(val); bool b = Convert.ToBoolean(reference);
            return op == ConditionOperator.Equals ? a == b : a != b;
        }
        if (val is IComparable)
        {
            double a = Convert.ToDouble(val); double b = Convert.ToDouble(reference);
            return op switch
            {
                ConditionOperator.Equals       => a == b,
                ConditionOperator.NotEquals    => a != b,
                ConditionOperator.GreaterThan  => a > b,
                ConditionOperator.LessThan     => a < b,
                ConditionOperator.GreaterOrEqual => a >= b,
                ConditionOperator.LessOrEqual    => a <= b,
                _ => false
            };
        }
        // Vector / struct: Equals + NotEquals only
        if (val != null && reference != null)
        {
            bool eq = val.Equals(reference);
            return op == ConditionOperator.Equals ? eq : !eq;
        }
        return false;
    }

    // ── Reflection utilities ──────────────────────────────────────────────
    private static MemberInfo GetMember(Type type, string name,
        BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
        => (MemberInfo)type.GetField(name, flags)
        ?? type.GetProperty(name, flags);

    private static Type GetMemberType(MemberInfo m)
        => m is FieldInfo fi ? fi.FieldType : ((PropertyInfo)m).PropertyType;

    /// <summary>
    /// Resolves a member from a component OR its GameObject, returning the correct target.
    /// </summary>
    private static MemberInfo ResolveComponentOrGoMember(
        UnityEngine.Object target, string memberName, out UnityEngine.Object resolvedTarget)
    {
        resolvedTarget = target;

        // Try GameObject props first
        var goMember = GetMember(typeof(GameObject), memberName);
        if (goMember != null)
        {
            resolvedTarget = target is Component c ? (UnityEngine.Object)c.gameObject : target;
            return goMember;
        }

        // Try component type
        var compMember = GetMember(target.GetType(), memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        return compMember;
    }
}
