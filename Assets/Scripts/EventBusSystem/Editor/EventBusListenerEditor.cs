#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// =========================================================
// CUSTOM EDITOR — EventBusListener
// Full visual inspector: event picker, method bindings,
// per-param source mapping, condition editor.
// =========================================================
[CustomEditor(typeof(EventBusListener))]
public class EventBusListenerEditor : Editor
{
    private static string   _cachedFolder;
    private static string[] _cachedNames = Array.Empty<string>();
    private static double   _lastScan    = -1;
    private const  double   COOLDOWN     = 5.0;

    private readonly Dictionary<int, MethodInfo[]> _methodCache = new();

    // Unity base types to exclude from component member pickers
    private static readonly HashSet<Type> _unityBaseTypes = new()
    {
        typeof(UnityEngine.Object), typeof(Component),
        typeof(Behaviour), typeof(MonoBehaviour), typeof(Transform),
    };

    private static readonly string[] _goProps =
        { "activeSelf", "activeInHierarchy", "name", "tag", "layer", "isStatic" };

    // ── Public helpers ─────────────────────────────────────────────────────
    public static void InvalidateCache()
    {
        _cachedFolder = null;
        _cachedNames  = Array.Empty<string>();
        _lastScan     = -1;
    }

    // =========================================================
    // MAIN INSPECTOR
    // =========================================================
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var listener     = (EventBusListener)target;
        var selectedProp = serializedObject.FindProperty("selectedEventTypeName");
        var raisedProp   = serializedObject.FindProperty("onRaised");
        var releasedProp = serializedObject.FindProperty("onReleased");
        var bothProp       = serializedObject.FindProperty("callOnBothStates");
        var cancelProp     = serializedObject.FindProperty("cancelAfterHandle");
        var cancelCondProp = serializedObject.FindProperty("cancelOnlyIfBindingFired");
        var bindingsProp   = serializedObject.FindProperty("smartBindings");

        if (bindingsProp == null)
        {
            EditorGUILayout.HelpBox("Could not find 'smartBindings'. Recompile and reselect.", MessageType.Error);
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("EventBus Listener", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // Config link
        var config = EventBusListenerConfig.Instance;
        string folder = config?.eventsFolder ?? "Scripts/Events";
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Config:", EditorStyles.miniLabel, GUILayout.Width(44));
            if (config != null && GUILayout.Button("EventBusListenerConfig", EditorStyles.linkLabel))
            { EditorGUIUtility.PingObject(config); Selection.activeObject = config; }
        }
        EditorGUILayout.Space(6);

        // Refresh cache if needed
        if (_cachedFolder != folder || (_cachedNames.Length == 0 &&
            EditorApplication.timeSinceStartup - _lastScan > COOLDOWN))
            RefreshCache(folder);

        // ── Event type selector ──────────────────────────────────────────
        DrawEventTypeSelector(selectedProp, bindingsProp, folder);

        string evtName = selectedProp.stringValue;
        Type   evtType = EventBusListener.ResolveType(evtName);
        var    evtFields = evtType != null
            ? evtType.GetFields(BindingFlags.Public | BindingFlags.Instance)
            : Array.Empty<FieldInfo>();
        bool isButtonEvent = evtType != null && EventBusListener.GetBoolMember(evtType) != null;

        // ── Cancellation ─────────────────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Cancellation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(cancelProp, new GUIContent("Cancel After Handle",
            "Stops propagation to remaining listeners after this one fires."));
        if (cancelProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(cancelCondProp, new GUIContent("Only If Binding Fired",
                "Only cancel if at least one If/ElseIf binding actually executed."));
            GUI.color = new Color(1f, 0.7f, 0.4f);
            EditorGUILayout.LabelField(
                cancelCondProp.boolValue
                    ? "⚡ Will cancel only when a binding condition matches."
                    : "⚡ Will always cancel after handling this event.",
                EditorStyles.miniLabel);
            GUI.color = Color.white;
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space(6);
        
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Execution Order", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("priority"),
            new GUIContent("Priority",
                "Higher = executes first. Listeners with cancelAfterHandle=true and high priority " +
                "will cancel all lower-priority listeners."));
        
        // ── Smart bindings ───────────────────────────────────────────────
        EditorGUILayout.LabelField("Method Bindings", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Select a component and a method. Each parameter maps to an event field or fixed value.",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(4);

        for (int i = 0; i < bindingsProp.arraySize; i++)
        {
            if (DrawBinding(i, bindingsProp, evtFields, evtType)) break; // removed → restart loop
        }

        if (GUILayout.Button("+ Add Method Binding"))
        {
            bindingsProp.InsertArrayElementAtIndex(bindingsProp.arraySize);
            serializedObject.ApplyModifiedProperties();
            var bp2 = bindingsProp.GetArrayElementAtIndex(bindingsProp.arraySize - 1);
            if (bp2 != null)
            {
                bp2.FindPropertyRelative("targetObject").objectReferenceValue = null;
                bp2.FindPropertyRelative("methodName").stringValue = "";
                bp2.FindPropertyRelative("paramSources")?.ClearArray();
            }
        }

        EditorGUILayout.Space(8);

        // ── Classic callbacks ────────────────────────────────────────────
        DrawClassicCallbacks(raisedProp, releasedProp, bothProp, isButtonEvent);

        EditorGUILayout.Space(4);
        if (Application.isPlaying)
        {
            GUI.color = Color.cyan;
            EditorGUILayout.LabelField($"Subscribed to: {listener.selectedEventTypeName}", EditorStyles.miniLabel);
            GUI.color = Color.white;
        }

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Refresh Event List")) RefreshCache(folder);

        serializedObject.ApplyModifiedProperties();
    }

    // =========================================================
    // SECTION DRAWERS
    // =========================================================
    private void DrawEventTypeSelector(
        SerializedProperty selectedProp,
        SerializedProperty bindingsProp,
        string folder)
    {
        EditorGUILayout.LabelField("Listen To", EditorStyles.miniBoldLabel);
        if (_cachedNames.Length == 0)
        {
            EditorGUILayout.HelpBox($"No OnXxxEvent classes found in Assets/{folder}", MessageType.Warning);
            return;
        }

        int cur  = Mathf.Max(0, Array.IndexOf(_cachedNames, selectedProp.stringValue));
        int next = EditorGUILayout.Popup(cur, _cachedNames);
        if (next != cur || string.IsNullOrEmpty(selectedProp.stringValue))
        {
            selectedProp.stringValue = _cachedNames[next];
            bindingsProp.ClearArray();
            _methodCache.Clear();
        }
        EditorGUILayout.LabelField(
            $" {_cachedNames.Length} event(s) in Assets/{folder}", EditorStyles.miniLabel);
        EditorGUILayout.Space(8);
    }

    /// <returns>true if the binding was removed (caller must restart loop)</returns>
    private bool DrawBinding(
        int i, SerializedProperty bindingsProp,
        FieldInfo[] evtFields, Type evtType)
    {
        var bp         = bindingsProp.GetArrayElementAtIndex(i);
        if (bp == null) return false;

        var targetProp = bp.FindPropertyRelative("targetObject");
        var methodProp = bp.FindPropertyRelative("methodName");
        var fieldsProp = bp.FindPropertyRelative("paramSources");

        if (targetProp == null || methodProp == null || fieldsProp == null)
        {
            EditorGUILayout.HelpBox($"Binding {i + 1} has invalid data — remove and re-add.", MessageType.Warning);
            if (GUILayout.Button("Remove")) { bindingsProp.DeleteArrayElementAtIndex(i); return true; }
            return false;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Header with logic color
        var logicProp = bp.FindPropertyRelative("logic");
        var logicVal  = logicProp != null ? (BindingLogic)logicProp.enumValueIndex : BindingLogic.If;
        Color hdrColor = logicVal switch
        {
            BindingLogic.If     => new Color(0.4f, 0.8f, 1f),
            BindingLogic.ElseIf => new Color(1f,   0.8f, 0.4f),
            BindingLogic.Else   => new Color(0.8f, 0.6f, 1f),
            _                   => Color.white
        };

        bool removed = false;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.color = hdrColor;
            if (logicProp != null) EditorGUILayout.PropertyField(logicProp, GUIContent.none, GUILayout.Width(64));
            GUI.color = Color.white;
            EditorGUILayout.LabelField($"Binding {i + 1}", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                bindingsProp.DeleteArrayElementAtIndex(i);
                _methodCache.Remove(i);
                removed = true;
            }
        }

        if (!removed)
        {
            DrawTargetAndMethod(i, bp, targetProp, methodProp, fieldsProp, evtFields, evtType);
            DrawConditions(bp, evtFields, logicVal != BindingLogic.Else);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
        return removed;
    }

    private void DrawTargetAndMethod(
        int i, SerializedProperty bp,
        SerializedProperty targetProp, SerializedProperty methodProp,
        SerializedProperty fieldsProp,
        FieldInfo[] evtFields, Type evtType)
    {
        var currentTarget = targetProp.objectReferenceValue;
        var currentGO     = currentTarget is Component c0 ? c0.gameObject : currentTarget as GameObject;

        // Row 1: GameObject field
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("GameObject", GUILayout.Width(80));
        EditorGUI.BeginChangeCheck();
        var droppedObj = EditorGUILayout.ObjectField(currentGO, typeof(UnityEngine.Object), true);
        if (EditorGUI.EndChangeCheck())
        {
            var newGO        = droppedObj as GameObject ?? (droppedObj as Component)?.gameObject;
            var droppedComp  = droppedObj as Component;

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
            methodProp.stringValue = "";
            fieldsProp.ClearArray();
            _methodCache.Remove(i);
        }
        EditorGUILayout.EndHorizontal();

        if (currentGO == null) return;

        // Row 2: Component dropdown
        var allComps   = currentGO.GetComponents<Component>();
        bool goSelected = targetProp.objectReferenceValue is GameObject;
        var  seen      = new Dictionary<string, int>();
        var  compLabels = new[] { "── GameObject ──" }.Concat(allComps.Select(c =>
        {
            string n = c.GetType().Name;
            if (!seen.ContainsKey(n)) { seen[n] = 0; return n; }
            seen[n]++; return $"{n} [{seen[n]}]";
        })).ToArray();

        int curIdx = goSelected ? 0
            : (targetProp.objectReferenceValue is Component curComp
                ? Mathf.Max(1, Array.IndexOf(allComps, curComp) + 1) : 1);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Component", GUILayout.Width(80));
        EditorGUI.BeginChangeCheck();
        int newIdx = EditorGUILayout.Popup(curIdx, compLabels);
        if (EditorGUI.EndChangeCheck())
        {
            targetProp.objectReferenceValue = newIdx == 0
                ? (UnityEngine.Object)currentGO
                : allComps[newIdx - 1];
            methodProp.stringValue = "";
            fieldsProp.ClearArray();
            _methodCache.Remove(i);
        }
        EditorGUILayout.EndHorizontal();

        // Method picker
        var targetObj = targetProp.objectReferenceValue;
        if (targetObj == null) return;

        if (!_methodCache.TryGetValue(i, out var methods))
            _methodCache[i] = methods = EventBusEditorUtils.GetEligibleMethods(targetObj.GetType(), evtFields, evtType);

        if (methods.Length == 0) { EditorGUILayout.HelpBox("No compatible methods found.", MessageType.Info); return; }

        var methodLabels = methods.Select(m =>
        {
            if (m.IsSpecialName && m.Name.StartsWith("set_"))
            {
                var p = m.GetParameters()[0];
                return $"{m.Name.Substring(4)} = ({p.ParameterType.Name})";
            }
            var ps   = m.GetParameters();
            string pStr = ps.Length == 0 ? "()"
                : "(" + string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}")) + ")";
            return $"{m.Name}{pStr}";
        }).ToArray();

        int curMethod = Mathf.Max(0, Array.FindIndex(methods, m =>
            m.Name == methodProp.stringValue &&
            m.GetParameters().Length == (fieldsProp?.arraySize ?? 0)));

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Method", GUILayout.Width(80));
        EditorGUI.BeginChangeCheck();
        int nextMethod = EditorGUILayout.Popup(curMethod, methodLabels);
        if (EditorGUI.EndChangeCheck() || string.IsNullOrEmpty(methodProp.stringValue))
        {
            var selected = methods[nextMethod];
            methodProp.stringValue = selected.Name;
            var ps = selected.GetParameters();
            fieldsProp.ClearArray();
            serializedObject.ApplyModifiedProperties();

            for (int p = 0; p < ps.Length; p++)
            {
                fieldsProp.InsertArrayElementAtIndex(p);
                serializedObject.ApplyModifiedProperties();
                var fp      = fieldsProp.GetArrayElementAtIndex(p);
                var modeSP  = fp?.FindPropertyRelative("mode");
                if (modeSP == null) continue;

                if (evtType != null && ps[p].ParameterType.IsAssignableFrom(evtType))
                    modeSP.enumValueIndex = (int)ParamSourceMode.WholeEvent;
                else
                {
                    modeSP.enumValueIndex = 0;
                    var match = evtFields.FirstOrDefault(f =>
                        f.FieldType == ps[p].ParameterType && !IsAlreadyUsedPS(fieldsProp, f.Name, p));
                    fp.FindPropertyRelative("eventFieldName").stringValue = match?.Name ?? "";
                }
                fp.FindPropertyRelative("fixedValue").stringValue     = "";
                fp.FindPropertyRelative("componentMember").stringValue = "";
            }
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        // Per-parameter mapping
        if (!string.IsNullOrEmpty(methodProp.stringValue))
            DrawParameterMappings(bp, fieldsProp, methods[nextMethod], evtFields, evtType);
    }

    private void DrawParameterMappings(
        SerializedProperty bp, SerializedProperty fieldsProp,
        MethodInfo selMethod, FieldInfo[] evtFields, Type evtType)
    {
        var mps = selMethod.GetParameters();
        for (int p = 0; p < mps.Length; p++)
        {
            while (fieldsProp.arraySize <= p)
            {
                fieldsProp.InsertArrayElementAtIndex(fieldsProp.arraySize);
                serializedObject.ApplyModifiedProperties();
            }

            var fp        = fieldsProp.GetArrayElementAtIndex(p);
            if (fp == null) continue;
            var modeProp  = fp.FindPropertyRelative("mode");
            var evtFNProp = fp.FindPropertyRelative("eventFieldName");
            var fixedProp = fp.FindPropertyRelative("fixedValue");
            var compProp  = fp.FindPropertyRelative("componentMember");

            if (modeProp == null || evtFNProp == null || fixedProp == null || compProp == null)
            {
                EditorGUILayout.HelpBox("Param data outdated — reselect method.", MessageType.Warning);
                continue;
            }

            var pType   = mps[p].ParameterType;
            var srcMode = (ParamSourceMode)modeProp.enumValueIndex;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.color = new Color(0.7f, 1f, 0.8f);
                EditorGUILayout.LabelField($"{pType.Name} {mps[p].Name}", EditorStyles.miniBoldLabel, GUILayout.Width(140));
                GUI.color = Color.white;

                if (srcMode == ParamSourceMode.WholeEvent)
                {
                    GUI.color = new Color(1f, 0.85f, 0.4f);
                    EditorGUILayout.LabelField("● whole event", EditorStyles.miniLabel);
                    GUI.color = Color.white;
                }
                else
                {
                    EditorGUILayout.LabelField("source:", GUILayout.Width(46));
                    EditorGUILayout.PropertyField(modeProp, GUIContent.none);
                }
            }

            switch (srcMode)
            {
                case ParamSourceMode.WholeEvent: break;

                case ParamSourceMode.EventField:
                    DrawEventFieldParam(evtFNProp, evtFields, pType);
                    break;

                case ParamSourceMode.FixedValue:
                    if (EventBusEditorUtils.IsObjectType(pType))
                    {
                        GUI.color = new Color(1f, 0.7f, 0.4f);
                        EditorGUILayout.LabelField("Use 'Object Reference' or 'Caller Object' for Object types.", EditorStyles.miniLabel);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        fixedProp.stringValue = EventBusEditorUtils.DrawTypedField("Value", fixedProp.stringValue, pType);
                    }
                    break;

                case ParamSourceMode.Toggle:
                    DrawToggleParam(bp, compProp, pType);
                    break;

                case ParamSourceMode.ObjectReference:
                    EventBusEditorUtils.DrawObjectField("Object", fp.FindPropertyRelative("objectReference"), pType);
                    break;

                case ParamSourceMode.CallerObject:
                    GUI.color = new Color(0.5f, 1f, 0.7f);
                    EditorGUILayout.LabelField($"  ← runtime: caller.GetComponent<{pType.Name}>()", EditorStyles.miniLabel);
                    GUI.color = Color.white;
                    break;

                default: // ComponentField
                    DrawComponentFieldParam(bp, compProp, pType);
                    break;
            }

            EditorGUILayout.EndVertical();
        }
    }

    private void DrawEventFieldParam(
        SerializedProperty evtFNProp, FieldInfo[] evtFields, Type pType)
    {
        var compat = evtFields.Where(f => EventBusEditorUtils.IsCompatible(f.FieldType, pType)).ToArray();
        if (compat.Length == 0)
        {
            EditorGUILayout.HelpBox($"No event fields of type {pType.Name}", MessageType.Warning);
            return;
        }
        string[] cL = compat.Select(f => $"{f.Name} ({f.FieldType.Name})").ToArray();
        string[] cN = compat.Select(f => f.Name).ToArray();
        int cur = Mathf.Max(0, Array.IndexOf(cN, evtFNProp.stringValue));
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Event field", GUILayout.Width(80));
            evtFNProp.stringValue = cN[EditorGUILayout.Popup(cur, cL)];
        }
    }

    private void DrawToggleParam(SerializedProperty bp, SerializedProperty compProp, Type pType)
    {
        if (pType != typeof(bool))
        {
            EditorGUILayout.HelpBox("Toggle solo funciona con parámetros bool.", MessageType.Warning);
            return;
        }
        var tObj = bp.FindPropertyRelative("targetObject").objectReferenceValue;
        if (tObj == null) { EditorGUILayout.HelpBox("Assign component first.", MessageType.Warning); return; }

        var boolMems = EventBusEditorUtils.GetComponentMembers(tObj, typeof(bool))
            .Concat(EventBusEditorUtils.GetGameObjectMembers().Where(m =>
            {
                var mt = m is FieldInfo fb ? fb.FieldType : ((PropertyInfo)m).PropertyType;
                return mt == typeof(bool);
            })).ToArray();

        if (boolMems.Length == 0) { EditorGUILayout.HelpBox("No se encontraron miembros bool.", MessageType.Warning); return; }

        string[] tN = boolMems.Select(m => m.Name).ToArray();
        string[] tL = boolMems.Select(m =>
        {
            string owner = m.DeclaringType == typeof(GameObject) ? "GO" : tObj.GetType().Name;
            return $"!{owner}.{m.Name}";
        }).ToArray();
        int cur = Mathf.Max(0, Array.IndexOf(tN, compProp.stringValue));
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.color = new Color(1f, 0.85f, 0.5f);
            EditorGUILayout.LabelField("Toggle", GUILayout.Width(80));
            GUI.color = Color.white;
            compProp.stringValue = tN[EditorGUILayout.Popup(cur, tL)];
        }
        GUI.color = new Color(1f, 0.9f, 0.5f);
        EditorGUILayout.LabelField($" pasa: !{tObj.GetType().Name}.{compProp.stringValue}", EditorStyles.miniLabel);
        GUI.color = Color.white;
    }

    private void DrawComponentFieldParam(SerializedProperty bp, SerializedProperty compProp, Type pType)
    {
        var tObj2 = bp.FindPropertyRelative("targetObject").objectReferenceValue;
        if (tObj2 == null) { EditorGUILayout.HelpBox("Assign component first.", MessageType.Warning); return; }

        var compMems = EventBusEditorUtils.GetComponentMembers(tObj2, pType)
            .Concat(EventBusEditorUtils.GetGameObjectMembers().Where(m =>
            {
                var mt = m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType;
                return EventBusEditorUtils.IsCompatible(mt, pType);
            })).ToArray();

        if (compMems.Length == 0) { EditorGUILayout.HelpBox($"No compatible members of type {pType.Name}.", MessageType.Warning); return; }

        string[] cN = compMems.Select(m => m.Name).ToArray();
        string[] cL = compMems.Select(m =>
        {
            var mt    = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType;
            string ow = m.DeclaringType == typeof(GameObject) ? "GO" : tObj2.GetType().Name;
            return $"{ow}.{m.Name} ({mt.Name})";
        }).ToArray();
        int cur = Mathf.Max(0, Array.IndexOf(cN, compProp.stringValue));
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Member", GUILayout.Width(80));
            compProp.stringValue = cN[EditorGUILayout.Popup(cur, cL)];
        }
    }

    private void DrawConditions(SerializedProperty bp, FieldInfo[] evtFields, bool showCondition)
    {
        EditorGUILayout.Space(4);
        var condsProp = bp.FindPropertyRelative("conditions");

        if (!showCondition)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ToggleLeft("Condition (always fires — Else branch)", false, EditorStyles.miniBoldLabel);
            EditorGUI.EndDisabledGroup();
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Conditions (AND)", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add", GUILayout.Width(50)))
            {
                condsProp.InsertArrayElementAtIndex(condsProp.arraySize);
                serializedObject.ApplyModifiedProperties();
                var newCond   = condsProp.GetArrayElementAtIndex(condsProp.arraySize - 1);
                var enabledP  = newCond?.FindPropertyRelative("enabled");
                if (enabledP != null) enabledP.boolValue = true;
            }
        }

        for (int ci = 0; ci < condsProp.arraySize; ci++)
        {
            if (DrawCondition(ci, condsProp, bp, evtFields)) break;
        }
    }

    /// <returns>true if condition was removed</returns>
    private bool DrawCondition(
        int ci, SerializedProperty condsProp, SerializedProperty bp, FieldInfo[] evtFields)
    {
        var condProp      = condsProp.GetArrayElementAtIndex(ci);
        if (condProp == null) return false;

        var condEnabled   = condProp.FindPropertyRelative("enabled");
        var condField     = condProp.FindPropertyRelative("fieldName");
        var condOp        = condProp.FindPropertyRelative("op");
        var condSource    = condProp.FindPropertyRelative("source");
        var condValue     = condProp.FindPropertyRelative("compareValue");
        var condCompField = condProp.FindPropertyRelative("componentFieldName");
        if (condEnabled == null) return false;

        EditorGUILayout.BeginVertical(GUI.skin.box);
        bool removeThis = false;

        using (new EditorGUILayout.HorizontalScope())
        {
            condEnabled.boolValue = EditorGUILayout.ToggleLeft(
                $"Condition {ci + 1}", condEnabled.boolValue,
                EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("−", GUILayout.Width(20))) removeThis = true;
        }

        var condSourceVal = (ConditionSource)condSource.enumValueIndex;

        if (condEnabled.boolValue && evtFields.Length > 0)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);

            string[] condFieldNames  = evtFields.Select(f => f.Name).ToArray();
            string[] condFieldLabels = evtFields.Select(f => $"{f.Name} ({f.FieldType.Name})").ToArray();
            int condFieldIdx = Mathf.Max(0, Array.IndexOf(condFieldNames, condField.stringValue));
            var targetObj2   = bp.FindPropertyRelative("targetObject").objectReferenceValue;

            if (condSourceVal != ConditionSource.ComponentVsLiteral)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("if", GUILayout.Width(18));
                    int newIdx = EditorGUILayout.Popup(condFieldIdx, condFieldLabels, GUILayout.MinWidth(100));
                    condField.stringValue = condFieldNames[newIdx];
                    bool isNum = EventBusEditorUtils.IsNumeric(evtFields[newIdx].FieldType);
                    if (!isNum && (ConditionOperator)condOp.enumValueIndex > ConditionOperator.NotEquals)
                        condOp.enumValueIndex = 0;
                    EditorGUILayout.PropertyField(condOp, GUIContent.none, GUILayout.Width(isNum ? 100 : 80));
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Compare to", GUILayout.Width(80));
                EditorGUILayout.PropertyField(condSource, GUIContent.none);
            }

            var selectedCondField = evtFields.FirstOrDefault(f => f.Name == condField.stringValue);

            switch (condSourceVal)
            {
                case ConditionSource.EventField:
                    if (selectedCondField != null)
                        condValue.stringValue = EventBusEditorUtils.DrawTypedField("Value", condValue.stringValue, selectedCondField.FieldType);
                    GUI.color = new Color(1f, 0.9f, 0.5f);
                    EditorGUILayout.LabelField(
                        $" if ({condField.stringValue} {EventBusEditorUtils.OpSymbol((ConditionOperator)condOp.enumValueIndex)} {condValue.stringValue})",
                        EditorStyles.miniLabel);
                    GUI.color = Color.white;
                    break;

                case ConditionSource.ComponentField:
                    DrawConditionComponentField(condCompField, condOp, condField.stringValue, targetObj2, selectedCondField);
                    break;

                case ConditionSource.ComponentVsLiteral:
                    DrawConditionComponentVsLiteral(condCompField, condOp, condValue, targetObj2);
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndVertical();
        if (removeThis) { condsProp.DeleteArrayElementAtIndex(ci); return true; }
        return false;
    }

    private void DrawConditionComponentField(
        SerializedProperty condCompField, SerializedProperty condOp,
        string condFieldName, UnityEngine.Object targetObj2, FieldInfo selectedCondField)
    {
        if (targetObj2 == null) { EditorGUILayout.HelpBox("Assign Component above first.", MessageType.Warning); return; }
        var cMems = EventBusEditorUtils.GetComponentMembers(targetObj2, selectedCondField?.FieldType);
        if (cMems.Length == 0) { EditorGUILayout.HelpBox("No compatible fields.", MessageType.Info); return; }

        string[] cN = cMems.Select(m => m.Name).ToArray();
        string[] cL = cMems.Select(m =>
        {
            var mt = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType;
            return $"{m.Name} ({mt.Name})";
        }).ToArray();
        int curC = Mathf.Max(0, Array.IndexOf(cN, condCompField.stringValue));
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Component field", GUILayout.Width(110));
            condCompField.stringValue = cN[EditorGUILayout.Popup(curC, cL)];
        }
        GUI.color = new Color(1f, 0.9f, 0.5f);
        EditorGUILayout.LabelField(
            $" if ({condFieldName} {EventBusEditorUtils.OpSymbol((ConditionOperator)condOp.enumValueIndex)} {targetObj2.GetType().Name}.{condCompField.stringValue})",
            EditorStyles.miniLabel);
        GUI.color = Color.white;
    }

    private void DrawConditionComponentVsLiteral(
        SerializedProperty condCompField, SerializedProperty condOp,
        SerializedProperty condValue, UnityEngine.Object targetObj2)
    {
        if (targetObj2 == null) { EditorGUILayout.HelpBox("Assign Component above first.", MessageType.Warning); return; }

        var allMems = EventBusEditorUtils.GetComponentMembers(targetObj2, null)
            .Concat(EventBusEditorUtils.GetGameObjectMembers()).ToArray();
        if (allMems.Length == 0) { EditorGUILayout.HelpBox("No fields found.", MessageType.Info); return; }

        string[] allN = allMems.Select(m => m.Name).ToArray();
        string[] allL = allMems.Select(m =>
        {
            var mt    = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType;
            string ow = m.DeclaringType == typeof(GameObject) ? "GameObject" : targetObj2.GetType().Name;
            return $"{ow}.{m.Name} ({mt.Name})";
        }).ToArray();

        int curC2 = Mathf.Max(0, Array.IndexOf(allN, condCompField.stringValue));
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Field", GUILayout.Width(40));
            int newC2 = EditorGUILayout.Popup(curC2, allL);
            condCompField.stringValue = allN[newC2];
            var selMem    = allMems[newC2];
            Type selType  = selMem is FieldInfo sf ? sf.FieldType : ((PropertyInfo)selMem).PropertyType;
            bool numSel   = EventBusEditorUtils.IsNumeric(selType);
            if (!numSel && (ConditionOperator)condOp.enumValueIndex > ConditionOperator.NotEquals)
                condOp.enumValueIndex = 0;
            EditorGUILayout.PropertyField(condOp, GUIContent.none, GUILayout.Width(numSel ? 100 : 80));
        }

        var selMem2     = allMems[Mathf.Max(0, Array.IndexOf(allN, condCompField.stringValue))];
        Type selFieldType = selMem2 is FieldInfo sf2 ? sf2.FieldType : ((PropertyInfo)selMem2).PropertyType;
        condValue.stringValue = EventBusEditorUtils.DrawTypedField("Value", condValue.stringValue, selFieldType);

        string own2 = selMem2.DeclaringType == typeof(GameObject) ? "GameObject" : targetObj2.GetType().Name;
        GUI.color = new Color(1f, 0.9f, 0.5f);
        EditorGUILayout.LabelField(
            $" if ({own2}.{condCompField.stringValue} {EventBusEditorUtils.OpSymbol((ConditionOperator)condOp.enumValueIndex)} {condValue.stringValue})",
            EditorStyles.miniLabel);
        GUI.color = Color.white;
    }

    private static void DrawClassicCallbacks(
        SerializedProperty raisedProp, SerializedProperty releasedProp,
        SerializedProperty bothProp, bool isButtonEvent)
    {
        EditorGUILayout.LabelField("Classic Callbacks (no params)", EditorStyles.boldLabel);
        if (isButtonEvent)
        {
            EditorGUILayout.PropertyField(bothProp, new GUIContent("Call On Both States"));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(raisedProp,
                new GUIContent(bothProp.boolValue ? "On Event (always)" : "On Pressed"));
            if (!bothProp.boolValue)
                EditorGUILayout.PropertyField(releasedProp, new GUIContent("On Released"));
        }
        else
        {
            EditorGUILayout.PropertyField(raisedProp, new GUIContent("On Raised"));
        }
    }

    // =========================================================
    // TYPED FIELD DRAWER
    // =========================================================

    // =========================================================
    // HELPERS
    // =========================================================


    private static bool IsAlreadyUsedPS(
        SerializedProperty paramSources, string evtFieldName, int upTo)
    {
        for (int i = 0; i < upTo && i < paramSources.arraySize; i++)
        {
            var el = paramSources.GetArrayElementAtIndex(i);
            if (el.FindPropertyRelative("eventFieldName").stringValue == evtFieldName) return true;
        }
        return false;
    }

    private static void RefreshCache(string folder)
    {
        _cachedFolder = folder;
        _lastScan     = EditorApplication.timeSinceStartup;
        string fullPath = Path.Combine(Application.dataPath, folder);
        if (!Directory.Exists(fullPath)) { _cachedNames = Array.Empty<string>(); return; }

        var regex = new Regex(@"public\s+class\s+(On\w+Event)\b", RegexOptions.Compiled);
        var names = new List<string>();
        foreach (string file in Directory.GetFiles(fullPath, "*.cs", SearchOption.AllDirectories))
            foreach (Match m in regex.Matches(File.ReadAllText(file)))
                names.Add(m.Groups[1].Value);
        _cachedNames = names.Distinct().OrderBy(n => n).ToArray();
    }


}
#endif
