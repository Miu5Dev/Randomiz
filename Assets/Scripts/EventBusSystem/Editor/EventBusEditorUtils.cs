#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// =========================================================
// EDITOR SHARED UTILITIES
// Common inspector helpers shared by EventBusListenerEditor
// and InteractableEditor. Do NOT modify per-editor code here.
// =========================================================
public static class EventBusEditorUtils
{
    // Unity base types excluded from component member pickers
    public static readonly HashSet<Type> UnityBaseTypes = new()
    {
        typeof(UnityEngine.Object), typeof(Component),
        typeof(Behaviour), typeof(MonoBehaviour), typeof(Transform),
    };

    public static readonly string[] GoProps =
        { "activeSelf", "activeInHierarchy", "name", "tag", "layer", "isStatic" };

    // ── Member resolution ─────────────────────────────────────────────────
    public static MemberInfo[] GetComponentMembers(UnityEngine.Object obj, Type filter) =>
        obj.GetType()
           .GetFields(BindingFlags.Public | BindingFlags.Instance)
           .Where(f => !UnityBaseTypes.Contains(f.DeclaringType))
           .Cast<MemberInfo>()
           .Concat(obj.GetType()
               .GetProperties(BindingFlags.Public | BindingFlags.Instance)
               .Where(p => p.CanRead && !UnityBaseTypes.Contains(p.DeclaringType)))
           .Where(m =>
           {
               var mt = m is FieldInfo fi ? fi.FieldType : ((PropertyInfo)m).PropertyType;
               return filter == null || mt == filter ||
                      (IsNumeric(mt) && IsNumeric(filter)) ||
                      mt.IsAssignableFrom(filter) || filter.IsAssignableFrom(mt);
           })
           .ToArray();

    public static MemberInfo[] GetGameObjectMembers() =>
        typeof(GameObject)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && GoProps.Contains(p.Name))
            .Cast<MemberInfo>()
            .ToArray();

    public static bool IsNumeric(Type t) =>
        t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long);

    /// <summary>True for any type that is or derives from UnityEngine.Object.</summary>
    public static bool IsObjectType(Type t) =>
        typeof(UnityEngine.Object).IsAssignableFrom(t);

    public static string OpSymbol(ConditionOperator op) => op switch
    {
        ConditionOperator.Equals         => "==",
        ConditionOperator.NotEquals      => "!=",
        ConditionOperator.GreaterThan    => ">",
        ConditionOperator.LessThan       => "<",
        ConditionOperator.GreaterOrEqual => ">=",
        ConditionOperator.LessOrEqual    => "<=",
        _                                => "?"
    };

    // ── Eligible method picker ────────────────────────────────────────────
    /// <param name="eventFields">Pass null or empty for Interactable (no event payload).</param>
    /// <param name="evtType">Pass null for Interactable.</param>
    public static MethodInfo[] GetEligibleMethods(
        Type type, FieldInfo[] eventFields = null, Type evtType = null)
    {
        eventFields ??= Array.Empty<FieldInfo>();

        var regular = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.ReturnType == typeof(void))
            .Where(m =>
            {
                var ps = m.GetParameters();
                if (ps.Length == 0) return true;
                return ps.All(p =>
                    (evtType != null && p.ParameterType.IsAssignableFrom(evtType)) ||
                    eventFields.Any(f => IsCompatible(f.FieldType, p.ParameterType)) ||
                    TypeHelper.IsSupported(p.ParameterType) ||
                    IsObjectType(p.ParameterType));   // ObjectReference / CallerObject
            });

        var propSetters = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetSetMethod(false) != null)
            .Where(p =>
                eventFields.Length == 0 ||
                eventFields.Any(f => IsCompatible(f.FieldType, p.PropertyType)) ||
                TypeHelper.IsSupported(p.PropertyType) ||
                IsObjectType(p.PropertyType))
            .Select(p => p.GetSetMethod(false));

        return regular.Concat(propSetters)
            .OrderBy(m => m.GetParameters().Length == 0 ? 1 : 0)
            .ThenBy(m => m.Name)
            .ToArray();
    }

    public static bool IsCompatible(Type a, Type b) =>
        b.IsAssignableFrom(a) || a == b;

    // ── Typed field drawer ────────────────────────────────────────────────
    public static string DrawTypedField(string label, string current, Type t)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var ns = System.Globalization.NumberStyles.Float;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (!string.IsNullOrEmpty(label))
                EditorGUILayout.LabelField(label, GUILayout.Width(80));

            if (t == typeof(bool))
            {
                string[] o = { "true", "false" };
                return o[EditorGUILayout.Popup(current == "false" ? 1 : 0, o)];
            }
            if (t.IsEnum)
            {
                string[] v = Enum.GetNames(t);
                int c = Mathf.Max(0, Array.IndexOf(v, current));
                return v[EditorGUILayout.Popup(c, v)];
            }
            if (t == typeof(int))    { int.TryParse(current, out int v);             return EditorGUILayout.IntField(v).ToString(); }
            if (t == typeof(float))  { float.TryParse(current, ns, ic, out float v); return EditorGUILayout.FloatField(v).ToString(ic); }
            if (t == typeof(double)) { double.TryParse(current, ns, ic, out double v);return EditorGUILayout.DoubleField(v).ToString(ic); }
            if (t == typeof(long))   { long.TryParse(current, out long v);            return EditorGUILayout.LongField(v).ToString(); }

            if (t == typeof(Vector2))
            {
                var f = SplitF(current, 2); var v = EditorGUILayout.Vector2Field("", new Vector2(f[0], f[1]));
                return $"{v.x.ToString(ic)},{v.y.ToString(ic)}";
            }
            if (t == typeof(Vector3))
            {
                var f = SplitF(current, 3); var v = EditorGUILayout.Vector3Field("", new Vector3(f[0], f[1], f[2]));
                return $"{v.x.ToString(ic)},{v.y.ToString(ic)},{v.z.ToString(ic)}";
            }
            if (t == typeof(Vector4))
            {
                var f = SplitF(current, 4); var v = EditorGUILayout.Vector4Field("", new Vector4(f[0], f[1], f[2], f[3]));
                return $"{v.x.ToString(ic)},{v.y.ToString(ic)},{v.z.ToString(ic)},{v.w.ToString(ic)}";
            }
            if (t == typeof(Vector2Int))
            {
                var f = SplitI(current, 2); var v = EditorGUILayout.Vector2IntField("", new Vector2Int(f[0], f[1]));
                return $"{v.x},{v.y}";
            }
            if (t == typeof(Vector3Int))
            {
                var f = SplitI(current, 3); var v = EditorGUILayout.Vector3IntField("", new Vector3Int(f[0], f[1], f[2]));
                return $"{v.x},{v.y},{v.z}";
            }
            if (t == typeof(Color) || t == typeof(Color32))
            {
                var f = SplitF(current, 4, 1f);
                var v = EditorGUILayout.ColorField(new Color(f[0], f[1], f[2], f[3]));
                return $"{v.r.ToString(ic)},{v.g.ToString(ic)},{v.b.ToString(ic)},{v.a.ToString(ic)}";
            }
            if (t == typeof(Quaternion))
            {
                var f = SplitF(current, 4);
                var q = new Quaternion(f[0], f[1], f[2], f[3]);
                var eu = EditorGUILayout.Vector3Field("", q.eulerAngles);
                var qr = Quaternion.Euler(eu);
                return $"{qr.x.ToString(ic)},{qr.y.ToString(ic)},{qr.z.ToString(ic)},{qr.w.ToString(ic)}";
            }
            if (t == typeof(Rect))
            {
                var f = SplitF(current, 4);
                var v = EditorGUILayout.RectField(new Rect(f[0], f[1], f[2], f[3]));
                return $"{v.x.ToString(ic)},{v.y.ToString(ic)},{v.width.ToString(ic)},{v.height.ToString(ic)}";
            }
            if (t == typeof(Bounds))
            {
                var f = SplitF(current, 6);
                var v = EditorGUILayout.BoundsField(new Bounds(new Vector3(f[0], f[1], f[2]), new Vector3(f[3], f[4], f[5])));
                return $"{v.center.x.ToString(ic)},{v.center.y.ToString(ic)},{v.center.z.ToString(ic)},{v.size.x.ToString(ic)},{v.size.y.ToString(ic)},{v.size.z.ToString(ic)}";
            }
            if (t == typeof(RectInt))
            {
                var f = SplitI(current, 4);
                var v = EditorGUILayout.RectIntField(new RectInt(f[0], f[1], f[2], f[3]));
                return $"{v.x},{v.y},{v.width},{v.height}";
            }
            if (t == typeof(BoundsInt))
            {
                var f = SplitI(current, 6);
                var v = EditorGUILayout.BoundsIntField(new BoundsInt(f[0], f[1], f[2], f[3], f[4], f[5]));
                return $"{v.position.x},{v.position.y},{v.position.z},{v.size.x},{v.size.y},{v.size.z}";
            }
            return EditorGUILayout.TextField(current ?? "");
        }
    }

    // ── GameObject+Component picker (reusable row pair) ───────────────────
    /// <summary>
    /// Draws the standard "GameObject / Component" two-row picker.
    /// Returns true if the target changed (caller should clear method + param caches).
    /// </summary>
    public static bool DrawTargetPicker(
        SerializedProperty targetProp,
        ref Dictionary<int, MethodInfo[]> methodCache,
        int cacheKey,
        System.Action onChanged = null)
    {
        var currentTarget = targetProp.objectReferenceValue;
        var currentGO     = currentTarget is Component c0 ? c0.gameObject : currentTarget as GameObject;
        bool changed      = false;

        // Row 1: GameObject
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("GameObject", GUILayout.Width(80));
        EditorGUI.BeginChangeCheck();
        var dropped = EditorGUILayout.ObjectField(currentGO, typeof(UnityEngine.Object), true);
        if (EditorGUI.EndChangeCheck())
        {
            var newGO       = dropped as GameObject ?? (dropped as Component)?.gameObject;
            var droppedComp = dropped as Component;
            if (newGO == null)
                targetProp.objectReferenceValue = null;
            else if (droppedComp != null)
                targetProp.objectReferenceValue = droppedComp;
            else
            {
                var comps = newGO.GetComponents<Component>();
                targetProp.objectReferenceValue =
                    comps.FirstOrDefault(c => !(c is Transform)) ?? comps.FirstOrDefault();
            }
            methodCache?.Remove(cacheKey);
            changed = true;
            onChanged?.Invoke();
        }
        EditorGUILayout.EndHorizontal();

        // Row 2: Component dropdown
        currentTarget = targetProp.objectReferenceValue;
        currentGO     = currentTarget is Component c1 ? c1.gameObject : currentTarget as GameObject;
        if (currentGO != null)
        {
            var allComps   = currentGO.GetComponents<Component>();
            bool goSel     = targetProp.objectReferenceValue is GameObject;
            var  seen      = new Dictionary<string, int>();
            var  labels    = new[] { "── GameObject ──" }.Concat(allComps.Select(c =>
            {
                string n = c.GetType().Name;
                if (!seen.ContainsKey(n)) { seen[n] = 0; return n; }
                seen[n]++; return $"{n} [{seen[n]}]";
            })).ToArray();

            int curIdx = goSel ? 0
                : (targetProp.objectReferenceValue is Component curC
                    ? Mathf.Max(1, Array.IndexOf(allComps, curC) + 1) : 1);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Component", GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(curIdx, labels);
            if (EditorGUI.EndChangeCheck())
            {
                targetProp.objectReferenceValue = newIdx == 0
                    ? (UnityEngine.Object)currentGO
                    : allComps[newIdx - 1];
                methodCache?.Remove(cacheKey);
                changed = true;
                onChanged?.Invoke();
            }
            EditorGUILayout.EndHorizontal();
        }

        return changed;
    }

    // ── Condition external-object picker (GameObject + Component rows) ────────
    /// Draws a two-row picker (GameObject + Component dropdown) for an external object reference
    /// used by ExternalObjectVsLiteral / EventFieldVsExternalObject conditions.
    /// Returns true if the selected object changed (caller should clear the field name).
    public static bool DrawConditionObjectPicker(SerializedProperty objRefProp)
    {
        var current   = objRefProp.objectReferenceValue;
        var currentGO = current is Component c0 ? c0.gameObject : current as GameObject;
        bool changed  = false;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Object", GUILayout.Width(80));
        EditorGUI.BeginChangeCheck();
        var dropped = EditorGUILayout.ObjectField(currentGO, typeof(UnityEngine.Object), true);
        if (EditorGUI.EndChangeCheck())
        {
            var newGO       = dropped as GameObject ?? (dropped as Component)?.gameObject;
            var droppedComp = dropped as Component;
            if (newGO == null)
                objRefProp.objectReferenceValue = null;
            else if (droppedComp != null)
                objRefProp.objectReferenceValue = droppedComp;
            else
            {
                var comps = newGO.GetComponents<Component>();
                objRefProp.objectReferenceValue =
                    comps.FirstOrDefault(c => !(c is Transform)) ?? (UnityEngine.Object)newGO;
            }
            changed = true;
        }
        EditorGUILayout.EndHorizontal();

        current   = objRefProp.objectReferenceValue;
        currentGO = current is Component c1 ? c1.gameObject : current as GameObject;
        if (currentGO != null)
        {
            var allComps = currentGO.GetComponents<Component>();
            bool goSel   = objRefProp.objectReferenceValue is GameObject;
            var  seen    = new Dictionary<string, int>();
            var  labels  = new[] { "── GameObject ──" }.Concat(allComps.Select(c =>
            {
                string n = c.GetType().Name;
                if (!seen.ContainsKey(n)) { seen[n] = 0; return n; }
                seen[n]++; return $"{n} [{seen[n]}]";
            })).ToArray();
            int curIdx = goSel ? 0
                : (objRefProp.objectReferenceValue is Component curC
                    ? Mathf.Max(1, Array.IndexOf(allComps, curC) + 1) : 1);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Component", GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(curIdx, labels);
            if (EditorGUI.EndChangeCheck())
            {
                objRefProp.objectReferenceValue = newIdx == 0
                    ? (UnityEngine.Object)currentGO
                    : allComps[newIdx - 1];
                changed = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        return changed;
    }

    // ── Object drag-and-drop field ────────────────────────────────────────────
    /// Draws an ObjectField for any UnityEngine.Object subtype using the SerializedProperty directly.
    /// Call this instead of DrawTypedField when IsObjectType(pType) is true.
    public static void DrawObjectField(
        string label, SerializedProperty objRefProp, Type pType, bool allowSceneObjects = true)
    {
        if (objRefProp == null)
        {
            EditorGUILayout.HelpBox("objectReference field missing — rebuild SmartBinding.", MessageType.Error);
            return;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (!string.IsNullOrEmpty(label))
                EditorGUILayout.LabelField(label, GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            var newObj = EditorGUILayout.ObjectField(objRefProp.objectReferenceValue, pType, allowSceneObjects);
            if (EditorGUI.EndChangeCheck())
                objRefProp.objectReferenceValue = newObj;
        }
    }

    // ── Private split helpers ─────────────────────────────────────────────
    private static float[] SplitF(string s, int n, float def = 0f)
    {
        var p = (s ?? "").Split(',');
        var r = new float[n];
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < n; i++)
        {
            if (i < p.Length) float.TryParse(p[i].Trim(), System.Globalization.NumberStyles.Float, ic, out r[i]);
            else r[i] = def;
        }
        return r;
    }

    private static int[] SplitI(string s, int n)
    {
        var p = (s ?? "").Split(',');
        var r = new int[n];
        for (int i = 0; i < n; i++)
            if (i < p.Length) int.TryParse(p[i].Trim(), out r[i]);
        return r;
    }
}
#endif
