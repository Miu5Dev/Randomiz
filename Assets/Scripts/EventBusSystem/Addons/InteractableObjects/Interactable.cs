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
/// Interactable object. Call Use() to trigger all callbacks.
/// Supports typed parameter bindings (FixedValue and ComponentField)
/// using the same system as EventBusListener, without modifying it.
/// </summary>
public class Interactable : MonoBehaviour
{
    [SerializeField] private UnityEvent onUse;
    [HideInInspector] public List<SmartBinding> useBindings = new();

    public void Use()
    {
        bool chainFired = false;
        foreach (var binding in useBindings)
        {
            if (!binding.IsConfigured()) continue;
            try
            {
                switch (binding.logic)
                {
                    case BindingLogic.If:
                        chainFired = InteractableRunner.Check(binding);
                        if (chainFired) InteractableRunner.Execute(binding);
                        break;
                    case BindingLogic.ElseIf:
                        if (!chainFired && InteractableRunner.Check(binding))
                        { chainFired = true; InteractableRunner.Execute(binding); }
                        break;
                    case BindingLogic.Else:
                        if (!chainFired) { InteractableRunner.Execute(binding); chainFired = true; }
                        break;
                }
            }
            catch (Exception e) { Debug.LogError($"[Interactable] {e}"); }
        }
        onUse?.Invoke();
    }
}

/// <summary>
/// Executes a SmartBinding without an event payload.
/// Reads SmartBinding public fields directly — does not modify EventBusListener.
/// Only supports FixedValue and ComponentField as parameter sources.
/// </summary>
public static class InteractableRunner
{
    public static void Execute(SmartBinding binding)
    {
        if (!binding.IsConfigured()) return;

        var targetObject = binding.targetObject;
        Type targetType  = targetObject.GetType();

        MethodInfo method = null;
        foreach (var m in targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            if (m.Name == binding.methodName && m.GetParameters().Length == binding.paramSources.Count) { method = m; break; }
        if (method == null) { Debug.LogWarning($"[Interactable] Method '{binding.methodName}' not found on {targetType.Name}"); return; }

        var mParams = method.GetParameters();
        var args    = new object[binding.paramSources.Count];

        for (int i = 0; i < binding.paramSources.Count; i++)
        {
            var ps    = binding.paramSources[i];
            var pType = mParams[i].ParameterType;

            if (ps.mode == ParamSourceMode.FixedValue)
            {
                try { args[i] = TypeHelper.Parse(ps.fixedValue, pType); }
                catch { Debug.LogWarning($"[Interactable] Could not parse '{ps.fixedValue}' as {pType.Name}"); return; }
            }
            else if (ps.mode == ParamSourceMode.ComponentField)
            {
                var capTarget = targetObject;
                var mem = (MemberInfo)targetType.GetField(ps.componentMember, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                          ?? targetType.GetProperty(ps.componentMember, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (mem == null)
                {
                    mem = (MemberInfo)typeof(GameObject).GetField(ps.componentMember, BindingFlags.Public | BindingFlags.Instance)
                          ?? typeof(GameObject).GetProperty(ps.componentMember, BindingFlags.Public | BindingFlags.Instance);
                    if (mem != null && capTarget is Component c) capTarget = c.gameObject;
                }
                if (mem == null) { Debug.LogWarning($"[Interactable] Member '{ps.componentMember}' not found"); return; }
                args[i] = mem is FieldInfo fi ? fi.GetValue(capTarget) : ((PropertyInfo)mem).GetValue(capTarget);
            }
            else // EventField — no tiene sentido sin evento
            {
                Debug.LogWarning("[Interactable] EventField not supported. Use FixedValue or ComponentField.");
                return;
            }
        }

        method.Invoke(targetObject, args);
    }

    /// <summary>
    /// Evaluates all ComponentVsLiteral conditions on the binding.
    /// Returns true if all pass (or if there are no conditions).
    /// </summary>
    public static bool Check(SmartBinding binding)
    {
        if (binding.conditions == null || binding.conditions.Count == 0) return true;
        foreach (var cond in binding.conditions)
        {
            if (!cond.enabled) continue;
            // Only ComponentVsLiteral makes sense without an event payload
            if (cond.source != ConditionSource.ComponentVsLiteral) continue;
            var fn = cond.Compile(typeof(object), binding.targetObject);
            if (fn != null && !fn(null)) return false;
        }
        return true;
    }
}

// =========================================================
// CUSTOM EDITOR
// =========================================================
#if UNITY_EDITOR

[CustomEditor(typeof(Interactable))]
public class InteractableEditor : Editor
{
    private readonly Dictionary<int, MethodInfo[]> _methodCache = new();

    private static readonly HashSet<Type> _unityBaseTypes = new()
    {
        typeof(UnityEngine.Object), typeof(Component),
        typeof(Behaviour), typeof(MonoBehaviour), typeof(Transform),
    };
    private static readonly string[] _goProps    = { "activeSelf", "activeInHierarchy", "name", "tag" };
    private static readonly string[] _srcLabels  = { "Fixed Value", "Component Field" };
    private static readonly ParamSourceMode[] _srcModes = { ParamSourceMode.FixedValue, ParamSourceMode.ComponentField };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var bindingsProp = serializedObject.FindProperty("useBindings");
        var onUseProp    = serializedObject.FindProperty("onUse");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Interactable", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("Method Bindings", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Drag a GameObject or component, pick a method and configure its parameters.",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(4);

        for (int i = 0; i < bindingsProp.arraySize; i++)
        {
            var bp         = bindingsProp.GetArrayElementAtIndex(i);
            var targetProp = bp.FindPropertyRelative("targetObject");
            var methodProp = bp.FindPropertyRelative("methodName");
            var fieldsProp = bp.FindPropertyRelative("paramSources");
            if (targetProp == null || methodProp == null || fieldsProp == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            using (new EditorGUILayout.HorizontalScope())
            {
                // Logic badge (color-coded)
                var logicProp = bp.FindPropertyRelative("logic");
                var curLogic = logicProp != null ? (BindingLogic)logicProp.enumValueIndex : BindingLogic.If;
                GUI.color = curLogic switch {
                    BindingLogic.If     => new Color(0.5f, 0.9f, 1f),
                    BindingLogic.ElseIf => new Color(1f, 0.85f, 0.4f),
                    BindingLogic.Else   => new Color(0.8f, 0.8f, 0.8f),
                    _                   => Color.white
                };
                EditorGUILayout.LabelField($"Binding {i + 1}", EditorStyles.miniBoldLabel, GUILayout.Width(70));
                if (logicProp != null) logicProp.enumValueIndex = (int)(BindingLogic)EditorGUILayout.EnumPopup(curLogic, GUILayout.Width(70));
                GUI.color = Color.white;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("X", GUILayout.Width(22))) { bindingsProp.DeleteArrayElementAtIndex(i); _methodCache.Remove(i); break; }
            }

            // ── GameObject + Component picker ────────────────────────────────
            var currentTarget = targetProp.objectReferenceValue;
            GameObject currentGO = currentTarget is Component c0 ? c0.gameObject : currentTarget as GameObject;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("GameObject", GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            var droppedObj = EditorGUILayout.ObjectField(currentGO, typeof(UnityEngine.Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                GameObject newGO   = droppedObj as GameObject ?? (droppedObj as Component)?.gameObject;
                Component dropComp = droppedObj as Component;
                if (newGO != currentGO || dropComp != null)
                {
                    if (newGO == null) targetProp.objectReferenceValue = null;
                    else if (dropComp != null) targetProp.objectReferenceValue = dropComp;
                    else
                    {
                        var comps = newGO.GetComponents<Component>();
                        targetProp.objectReferenceValue = comps.FirstOrDefault(c => !(c is Transform)) ?? comps.FirstOrDefault();
                    }
                    methodProp.stringValue = ""; fieldsProp.ClearArray(); _methodCache.Remove(i);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (currentGO != null)
            {
                var allComps = currentGO.GetComponents<Component>();
                var seen = new Dictionary<string, int>();
                string[] compLabels = new[] { $"GameObject ({currentGO.name})" }
                    .Concat(allComps.Select(c => { string n = c.GetType().Name; if (!seen.ContainsKey(n)) { seen[n]=0; return n; } seen[n]++; return $"{n} [{seen[n]}]"; }))
                    .ToArray();
                int curIdx = currentTarget is GameObject ? 0
                           : currentTarget is Component curC ? Mathf.Max(1, Array.IndexOf(allComps, curC) + 1)
                           : 1;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Component", GUILayout.Width(80));
                EditorGUI.BeginChangeCheck();
                int newIdx = EditorGUILayout.Popup(curIdx, compLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    targetProp.objectReferenceValue = newIdx == 0
                        ? (UnityEngine.Object)currentGO
                        : allComps[newIdx - 1];
                    methodProp.stringValue = ""; fieldsProp.ClearArray(); _methodCache.Remove(i);
                }
                EditorGUILayout.EndHorizontal();
            }

            // ── Method picker ────────────────────────────────────────────────
            var targetObj = targetProp.objectReferenceValue;
            if (targetObj != null)
            {
                if (!_methodCache.TryGetValue(i, out var methods))
                    _methodCache[i] = methods = GetEligibleMethods(targetObj.GetType());

                if (methods.Length == 0)
                    EditorGUILayout.HelpBox("No compatible public methods found on this component.", MessageType.Info);
                else
                {
                    string[] methodLabels = methods.Select(m => {
                        if (m.IsSpecialName && m.Name.StartsWith("set_"))
                        {
                            var p = m.GetParameters()[0];
                            return $"{m.Name.Substring(4)} = ({p.ParameterType.Name})";
                        }
                        var ps = m.GetParameters();
                        string pStr = ps.Length == 0 ? "()" : "(" + string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}")) + ")";
                        return $"{m.Name}{pStr}";
                    }).ToArray();

                    // Match by name only — NOT by arraySize. If arraySize desynced, params would never show.
                    int curMethod = Mathf.Max(0, Array.FindIndex(methods, m => m.Name == methodProp.stringValue));

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Method", GUILayout.Width(80));
                    EditorGUI.BeginChangeCheck();
                    int nextMethod = EditorGUILayout.Popup(curMethod, methodLabels);
                    if (EditorGUI.EndChangeCheck() || string.IsNullOrEmpty(methodProp.stringValue))
                    {
                        var sel = methods[nextMethod];
                        methodProp.stringValue = sel.Name;
                        var ps = sel.GetParameters();
                        fieldsProp.ClearArray();
                        serializedObject.ApplyModifiedProperties();
                        for (int p = 0; p < ps.Length; p++)
                        {
                            fieldsProp.InsertArrayElementAtIndex(p);
                            serializedObject.ApplyModifiedProperties();
                            var fp = fieldsProp.GetArrayElementAtIndex(p);
                            if (fp != null)
                            {
                                var modeP2 = fp.FindPropertyRelative("mode");
                                if (modeP2 != null) modeP2.enumValueIndex = (int)ParamSourceMode.FixedValue;
                                var fixedP2 = fp.FindPropertyRelative("fixedValue");
                                if (fixedP2 != null) fixedP2.stringValue = "";
                                var evtP2 = fp.FindPropertyRelative("eventFieldName");
                                if (evtP2 != null) evtP2.stringValue = "";
                                var compP2 = fp.FindPropertyRelative("componentMember");
                                if (compP2 != null) compP2.stringValue = "";
                            }
                        }
                        serializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(target);
                    }
                    EditorGUILayout.EndHorizontal();

                    // Auto-sync paramSources count every frame — prevents desynced state
                    if (!string.IsNullOrEmpty(methodProp.stringValue))
                    {
                        int expectedCount = methods[nextMethod].GetParameters().Length;
                        bool synced = false;
                        while (fieldsProp.arraySize < expectedCount) { fieldsProp.InsertArrayElementAtIndex(fieldsProp.arraySize); synced = true; }
                        while (fieldsProp.arraySize > expectedCount) { fieldsProp.DeleteArrayElementAtIndex(fieldsProp.arraySize - 1); synced = true; }
                        if (synced) { serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                    }

                    // ── Per-parameter ────────────────────────────────────────
                    if (!string.IsNullOrEmpty(methodProp.stringValue))
                    {
                        var selMethod = methods[nextMethod];
                        var mps = selMethod.GetParameters();
                        for (int p = 0; p < mps.Length; p++)
                        {
                            while (fieldsProp.arraySize <= p) { fieldsProp.InsertArrayElementAtIndex(fieldsProp.arraySize); serializedObject.ApplyModifiedProperties(); }
                            var fp        = fieldsProp.GetArrayElementAtIndex(p);
                            var modeProp  = fp?.FindPropertyRelative("mode");
                            var fixedProp = fp?.FindPropertyRelative("fixedValue");
                            var compProp  = fp?.FindPropertyRelative("componentMember");
                            if (modeProp == null || fixedProp == null || compProp == null) continue;

                            var pType = mps[p].ParameterType;
                            EditorGUILayout.BeginVertical(GUI.skin.box);
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUI.color = new Color(0.7f, 1f, 0.8f);
                                EditorGUILayout.LabelField($"{pType.Name} {mps[p].Name}", EditorStyles.miniBoldLabel, GUILayout.Width(140));
                                GUI.color = Color.white;
                                EditorGUILayout.LabelField("source:", GUILayout.Width(46));
                                var curMode   = (ParamSourceMode)modeProp.enumValueIndex;
                                int srcCurIdx = Array.IndexOf(_srcModes, curMode); if (srcCurIdx < 0) srcCurIdx = 0;
                                EditorGUI.BeginChangeCheck();
                                int srcNewIdx = EditorGUILayout.Popup(srcCurIdx, _srcLabels);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    modeProp.enumValueIndex = (int)_srcModes[srcNewIdx];
                                    serializedObject.ApplyModifiedProperties();
                                    EditorUtility.SetDirty(target);
                                }
                            }

                            var mode = _srcModes[Mathf.Max(0, Array.IndexOf(_srcModes, (ParamSourceMode)modeProp.enumValueIndex))];
                            EditorGUI.BeginChangeCheck();
                            if (mode == ParamSourceMode.FixedValue)
                            {
                                string newFixed = DrawTypedField("Value", fixedProp.stringValue, pType);
                                if (newFixed != fixedProp.stringValue) fixedProp.stringValue = newFixed;
                            }
                            else
                            {
                                var tObj2 = bp.FindPropertyRelative("targetObject").objectReferenceValue;
                                if (tObj2 == null) EditorGUILayout.HelpBox("Assign a component first.", MessageType.Warning);
                                else
                                {
                                    var mems = GetComponentMembers(tObj2, pType).Concat(GetGameObjectMembers().Where(m => { Type mt = m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType; return mt == pType || mt.IsAssignableFrom(pType); })).ToArray();
                                    if (mems.Length == 0) EditorGUILayout.HelpBox($"No compatible members of type {pType.Name}.", MessageType.Warning);
                                    else
                                    {
                                        string[] cN = mems.Select(m => m.Name).ToArray();
                                        string[] cL = mems.Select(m => { Type mt = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType; string own = m.DeclaringType == typeof(GameObject) ? "GO" : tObj2.GetType().Name; return $"{own}.{m.Name} ({mt.Name})"; }).ToArray();
                                        int cur2 = Mathf.Max(0, Array.IndexOf(cN, compProp.stringValue));
                                        EditorGUILayout.BeginHorizontal();
                                        EditorGUILayout.LabelField("Member", GUILayout.Width(80));
                                        int newIdx2 = EditorGUILayout.Popup(cur2, cL);
                                        EditorGUILayout.EndHorizontal();
                                        if (newIdx2 != cur2) compProp.stringValue = cN[newIdx2];
                                    }
                                }
                            }
                            if (EditorGUI.EndChangeCheck())
                            {
                                serializedObject.ApplyModifiedProperties();
                                EditorUtility.SetDirty(target);
                            }
                            EditorGUILayout.EndVertical();
                        }
                    }
                }
            }
            // ── Conditions (ComponentVsLiteral only — no event payload) ────────────
            if (bp.FindPropertyRelative("targetObject").objectReferenceValue != null)
            {
                var condsProp = bp.FindPropertyRelative("conditions");
                if (condsProp != null)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Conditions (AND)", EditorStyles.miniBoldLabel);

                    int condToDelete = -1;
                    for (int c = 0; c < condsProp.arraySize; c++)
                    {
                        var cp       = condsProp.GetArrayElementAtIndex(c);
                        var enabledP = cp.FindPropertyRelative("enabled");
                        var leftP    = cp.FindPropertyRelative("componentFieldName");
                        var opP      = cp.FindPropertyRelative("op");
                        var rightP   = cp.FindPropertyRelative("compareValue");
                        var srcP     = cp.FindPropertyRelative("source");
                        if (srcP != null) srcP.enumValueIndex = (int)ConditionSource.ComponentVsLiteral;

                        EditorGUILayout.BeginVertical(GUI.skin.box);

                        // Row 1: toggle | label | [X]
                        EditorGUILayout.BeginHorizontal();
                        if (enabledP != null)
                        {
                            bool newEn = EditorGUILayout.Toggle(enabledP.boolValue, GUILayout.Width(16));
                            if (newEn != enabledP.boolValue) { enabledP.boolValue = newEn; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                        }
                        EditorGUILayout.LabelField($"Condition {c + 1}", EditorStyles.miniBoldLabel);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("X", GUILayout.Width(20))) condToDelete = c;
                        EditorGUILayout.EndHorizontal();

                        var tObj3 = bp.FindPropertyRelative("targetObject").objectReferenceValue;
                        if (tObj3 != null)
                        {
                            var allMems = tObj3.GetType()
                                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                                .Cast<MemberInfo>()
                                .Concat(tObj3.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p2 => p2.CanRead))
                                .Where(m => { Type mt = m is FieldInfo f3 ? f3.FieldType : ((PropertyInfo)m).PropertyType; return TypeHelper.IsSupported(mt); })
                                .ToArray();

                            if (allMems.Length > 0)
                            {
                                string[] mNames  = allMems.Select(m => m.Name).ToArray();
                                string[] mLabels = allMems.Select(m => { Type mt = m is FieldInfo f4 ? f4.FieldType : ((PropertyInfo)m).PropertyType; return $"{m.Name} ({mt.Name})"; }).ToArray();
                                int mCur = Mathf.Max(0, Array.IndexOf(mNames, leftP?.stringValue ?? ""));

                                // Row 2: Field picker (full width)
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField("Field", GUILayout.Width(36));
                                int mNew = EditorGUILayout.Popup(mCur, mLabels);
                                EditorGUILayout.EndHorizontal();
                                if (mNew != mCur && leftP != null)
                                { leftP.stringValue = mNames[mNew]; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }

                                // Resolve selected member after any apply
                                int resolvedIdx = Mathf.Max(0, Array.IndexOf(mNames, leftP?.stringValue ?? ""));
                                var selMem   = allMems[resolvedIdx];
                                Type selType = selMem is FieldInfo f5 ? f5.FieldType : ((PropertyInfo)selMem).PropertyType;

                                // Row 3: Op (own row)
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField("Op", GUILayout.Width(24));
                                if (opP != null)
                                {
                                    var newOp = (ConditionOperator)EditorGUILayout.EnumPopup((ConditionOperator)opP.enumValueIndex);
                                    if ((int)newOp != opP.enumValueIndex)
                                    { opP.enumValueIndex = (int)newOp; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                                }
                                EditorGUILayout.EndHorizontal();

                                // Row 4: Value (own row — always visible, full width)
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField("Value", GUILayout.Width(38));
                                if (rightP != null)
                                {
                                    string newVal = DrawConditionLiteralInline(rightP.stringValue, selType);
                                    if (newVal != rightP.stringValue)
                                    { rightP.stringValue = newVal; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                                }
                                EditorGUILayout.EndHorizontal();
                            }
                            else EditorGUILayout.LabelField("No supported members on component.", EditorStyles.miniLabel);
                        }

                        EditorGUILayout.EndVertical();
                        EditorGUILayout.Space(1);
                    }

                    if (condToDelete >= 0)
                    { condsProp.DeleteArrayElementAtIndex(condToDelete); serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }

                    if (GUILayout.Button("+ Add Condition", GUILayout.Height(20)))
                    {
                        condsProp.InsertArrayElementAtIndex(condsProp.arraySize);
                        serializedObject.ApplyModifiedProperties();
                        var nc  = condsProp.GetArrayElementAtIndex(condsProp.arraySize - 1);
                        var ep  = nc.FindPropertyRelative("enabled");         if (ep  != null) ep.boolValue  = true;
                        var sp2 = nc.FindPropertyRelative("source");          if (sp2 != null) sp2.enumValueIndex = (int)ConditionSource.ComponentVsLiteral;
                        var lv  = nc.FindPropertyRelative("compareValue");    if (lv  != null) lv.stringValue  = "";
                        var lft = nc.FindPropertyRelative("componentFieldName"); if (lft != null) lft.stringValue = "";
                        serializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(target);
                    }
                }
            }            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (GUILayout.Button("+ Add Method Binding"))
        {
            bindingsProp.InsertArrayElementAtIndex(bindingsProp.arraySize);
            serializedObject.ApplyModifiedProperties();
            var bp2 = bindingsProp.GetArrayElementAtIndex(bindingsProp.arraySize - 1);
            if (bp2 != null)
            {
                var tP = bp2.FindPropertyRelative("targetObject");
                if (tP != null) tP.objectReferenceValue = null;
                var mP = bp2.FindPropertyRelative("methodName");
                if (mP != null) mP.stringValue = "";
                bp2.FindPropertyRelative("paramSources")?.ClearArray();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Classic Callback (no params)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(onUseProp, new GUIContent("On Use"));
        serializedObject.ApplyModifiedProperties();
    }

    private static MethodInfo[] GetEligibleMethods(Type type)
    {
        var regular = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.ReturnType == typeof(void))
            .Where(m => { var ps = m.GetParameters(); return ps.Length == 0 || ps.All(p => TypeHelper.IsSupported(p.ParameterType)); });

        var propSetters = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetSetMethod(false) != null && TypeHelper.IsSupported(p.PropertyType))
            .Select(p => p.GetSetMethod(false));

        return regular.Concat(propSetters)
            .OrderBy(m => m.GetParameters().Length == 0 ? 1 : 0)
            .ThenBy(m => m.Name)
            .ToArray();
    }

    private static string DrawTypedField(string label, string current, Type t)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (!string.IsNullOrEmpty(label)) EditorGUILayout.LabelField(label, GUILayout.Width(80));
            if (t == typeof(bool))   { string[] o = {"true","false"}; return o[EditorGUILayout.Popup(current=="false"?1:0, o)]; }
            if (t.IsEnum)            { string[] v = Enum.GetNames(t); int c = Mathf.Max(0, Array.IndexOf(v, current)); return v[EditorGUILayout.Popup(c, v)]; }
            if (t == typeof(int))    { int.TryParse(current, out int v);   return EditorGUILayout.IntField(v).ToString(); }
            if (t == typeof(float))  { float.TryParse(current, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v); return EditorGUILayout.FloatField(v).ToString(System.Globalization.CultureInfo.InvariantCulture); }
            if (t == typeof(long))   { long.TryParse(current, out long v); return EditorGUILayout.LongField(v).ToString(); }
            if (t == typeof(Vector2)) { var f=Split(current,2); var v=EditorGUILayout.Vector2Field("",new Vector2(f[0],f[1])); var ic=System.Globalization.CultureInfo.InvariantCulture; return $"{v.x.ToString(ic)},{v.y.ToString(ic)}"; }
            if (t == typeof(Vector3)) { var f=Split(current,3); var v=EditorGUILayout.Vector3Field("",new Vector3(f[0],f[1],f[2])); var ic=System.Globalization.CultureInfo.InvariantCulture; return $"{v.x.ToString(ic)},{v.y.ToString(ic)},{v.z.ToString(ic)}"; }
            if (t == typeof(Color) || t == typeof(Color32)) { var f=Split(current,4,1f); var v=EditorGUILayout.ColorField(new Color(f[0],f[1],f[2],f[3])); var ic=System.Globalization.CultureInfo.InvariantCulture; return $"{v.r.ToString(ic)},{v.g.ToString(ic)},{v.b.ToString(ic)},{v.a.ToString(ic)}"; }
            return EditorGUILayout.TextField(current ?? "");
        }
    }

    private static float[] Split(string s, int n, float def = 0f)
    {
        var p = (s ?? "").Split(','); var r = new float[n];
        for (int i = 0; i < n; i++) { if (i < p.Length) float.TryParse(p[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r[i]); else r[i] = def; }
        return r;
    }

    private static MemberInfo[] GetComponentMembers(UnityEngine.Object obj, Type filter) =>
        obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !_unityBaseTypes.Contains(f.DeclaringType)).Cast<MemberInfo>()
        .Concat(obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && !_unityBaseTypes.Contains(p.DeclaringType)))
        .Where(m => { Type mt = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType; return filter == null || mt == filter || mt.IsAssignableFrom(filter); })
        .ToArray();

    private static MemberInfo[] GetGameObjectMembers() =>
        typeof(GameObject).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && _goProps.Contains(p.Name)).Cast<MemberInfo>().ToArray();

    private static string DrawConditionLiteralField(string current, Type t)
    {
        if (t == typeof(bool))   { string[] o = {"true","false"}; return o[EditorGUILayout.Popup(current=="false"?1:0, o, GUILayout.Width(70))]; }
        if (t.IsEnum)            { string[] v = Enum.GetNames(t); int c = Mathf.Max(0, Array.IndexOf(v, current)); return v[EditorGUILayout.Popup(c, v, GUILayout.Width(90))]; }
        if (t == typeof(int))    { int.TryParse(current, out int v);   return EditorGUILayout.IntField(v, GUILayout.Width(70)).ToString(); }
        if (t == typeof(float))  { float.TryParse(current, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v); return EditorGUILayout.FloatField(v, GUILayout.Width(70)).ToString(System.Globalization.CultureInfo.InvariantCulture); }
        return EditorGUILayout.TextField(current ?? "", GUILayout.Width(90));
    }

    // Inline version — no HorizontalScope, expands to fill remaining space in the row
    private static string DrawConditionLiteralInline(string current, Type t)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        if (t == typeof(bool))  { string[] o = {"true","false"}; return o[EditorGUILayout.Popup(current == "false" ? 1 : 0, o)]; }
        if (t.IsEnum)           { string[] v = Enum.GetNames(t); int ci = Mathf.Max(0, Array.IndexOf(v, current)); return v[EditorGUILayout.Popup(ci, v)]; }
        if (t == typeof(int))   { int.TryParse(current, out int v);   return EditorGUILayout.IntField(v).ToString(); }
        if (t == typeof(float)) { float.TryParse(current, System.Globalization.NumberStyles.Float, ic, out float v); return EditorGUILayout.FloatField(v).ToString(ic); }
        if (t == typeof(long))  { long.TryParse(current, out long v); return EditorGUILayout.LongField(v).ToString(); }
        return EditorGUILayout.TextField(current ?? "");
    }
}

#endif
