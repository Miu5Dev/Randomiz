using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Lives in Editor/ — can freely access EventBusEditorUtils and all editor APIs.
/// <summary>Custom inspector for Interactable; builds method bindings whose parameters come from fixed values, component fields or the caller (no event payload).</summary>
[CustomEditor(typeof(Interactable))]
public class InteractableEditor : Editor
{
    private Dictionary<int, MethodInfo[]> _methodCache = new();

    // Only FixedValue and ComponentField make sense without an event payload
    private static readonly string[] _srcLabels = {
        "Fixed Value",
        "Component Field",
        "Toggle",
        "Object Reference",
        "Caller Object",   // el GameObject caller directo
        "Caller Root"      // el root principal del caller
    };

    private static readonly ParamSourceMode[] _srcModes = {
        ParamSourceMode.FixedValue,
        ParamSourceMode.ComponentField,
        ParamSourceMode.Toggle,
        ParamSourceMode.ObjectReference,
        ParamSourceMode.CallerObject,
        ParamSourceMode.CallerRoot
    };
    
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
            if (DrawBinding(i, bindingsProp)) break;
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
        EditorGUILayout.LabelField("Classic Callback (no params)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(onUseProp, new GUIContent("On Use"));
        serializedObject.ApplyModifiedProperties();
    }

    // ── Single binding row ────────────────────────────────────────────────
    /// <returns>true if binding was removed (restart loop)</returns>
    private bool DrawBinding(int i, SerializedProperty bindingsProp)
    {
        var bp         = bindingsProp.GetArrayElementAtIndex(i);
        var targetProp = bp.FindPropertyRelative("targetObject");
        var methodProp = bp.FindPropertyRelative("methodName");
        var fieldsProp = bp.FindPropertyRelative("paramSources");
        if (targetProp == null || methodProp == null || fieldsProp == null) return false;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Header
        var logicProp = bp.FindPropertyRelative("logic");
        var curLogic  = logicProp != null ? (BindingLogic)logicProp.enumValueIndex : BindingLogic.If;
        bool removed  = false;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.color = curLogic switch {
                BindingLogic.If     => new Color(0.5f, 0.9f, 1f),
                BindingLogic.ElseIf => new Color(1f, 0.85f, 0.4f),
                BindingLogic.Else   => new Color(0.8f, 0.8f, 0.8f),
                _                   => Color.white
            };
            EditorGUILayout.LabelField($"Binding {i + 1}", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            if (logicProp != null)
                logicProp.enumValueIndex = (int)(BindingLogic)EditorGUILayout.EnumPopup(curLogic, GUILayout.Width(70));
            GUI.color = Color.white;
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
            // Target picker — reused from shared utils
            bool targetChanged = EventBusEditorUtils.DrawTargetPicker(
                targetProp, ref _methodCache, i,
                () => { methodProp.stringValue = ""; fieldsProp.ClearArray(); });
            if (targetChanged) serializedObject.ApplyModifiedProperties();

            DrawMethodAndParams(i, bp, targetProp, methodProp, fieldsProp);
            if (curLogic != BindingLogic.Else)
                DrawConditions(bp, targetProp);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
        return removed;
    }

    // ── Method + per-param rows ───────────────────────────────────────────
    private void DrawMethodAndParams(
        int i, SerializedProperty bp,
        SerializedProperty targetProp, SerializedProperty methodProp,
        SerializedProperty fieldsProp)
    {
        var targetObj = targetProp.objectReferenceValue;
        if (targetObj == null) return;

        if (!_methodCache.TryGetValue(i, out var methods))
            _methodCache[i] = methods = EventBusEditorUtils.GetEligibleMethods(targetObj.GetType());

        if (methods.Length == 0) { EditorGUILayout.HelpBox("No compatible public methods found.", MessageType.Info); return; }

        string[] methodLabels = methods.Select(m =>
        {
            if (m.IsSpecialName && m.Name.StartsWith("set_"))
            {
                var p = m.GetParameters()[0];
                return $"{m.Name.Substring(4)} = ({p.ParameterType.Name})";
            }
            var ps   = m.GetParameters();
            string s = ps.Length == 0 ? "()" : "(" + string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}")) + ")";
            return $"{m.Name}{s}";
        }).ToArray();

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
                var fp    = fieldsProp.GetArrayElementAtIndex(p);
                if (fp != null)
                {
                    var modeP2 = fp.FindPropertyRelative("mode");
                    if (modeP2 != null) modeP2.enumValueIndex = (int)ParamSourceMode.FixedValue;
                    fp.FindPropertyRelative("fixedValue").stringValue      = "";
                    fp.FindPropertyRelative("eventFieldName").stringValue   = "";
                    fp.FindPropertyRelative("componentMember").stringValue  = "";
                }
            }
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
        EditorGUILayout.EndHorizontal();

        if (string.IsNullOrEmpty(methodProp.stringValue)) return;

        // Auto-sync param count
        var selMethod     = methods[nextMethod];
        int expectedCount = selMethod.GetParameters().Length;
        bool synced       = false;
        while (fieldsProp.arraySize < expectedCount) { fieldsProp.InsertArrayElementAtIndex(fieldsProp.arraySize); synced = true; }
        while (fieldsProp.arraySize > expectedCount) { fieldsProp.DeleteArrayElementAtIndex(fieldsProp.arraySize - 1); synced = true; }
        if (synced) { serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }

        // Per-param
        var mps = selMethod.GetParameters();
        for (int p = 0; p < mps.Length; p++)
        {
            while (fieldsProp.arraySize <= p) { fieldsProp.InsertArrayElementAtIndex(fieldsProp.arraySize); serializedObject.ApplyModifiedProperties(); }
            var fp       = fieldsProp.GetArrayElementAtIndex(p);
            var modeProp = fp?.FindPropertyRelative("mode");
            var fixedP   = fp?.FindPropertyRelative("fixedValue");
            var compP    = fp?.FindPropertyRelative("componentMember");
            if (modeProp == null || fixedP == null || compP == null) continue;

            var   pType   = mps[p].ParameterType;
            var   curMode = (ParamSourceMode)modeProp.enumValueIndex;
            int   srcIdx  = Array.IndexOf(_srcModes, curMode);
            if (srcIdx < 0) srcIdx = 0;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.color = new Color(0.7f, 1f, 0.8f);
                EditorGUILayout.LabelField($"{pType.Name} {mps[p].Name}", EditorStyles.miniBoldLabel, GUILayout.Width(140));
                GUI.color = Color.white;
                EditorGUILayout.LabelField("source:", GUILayout.Width(46));
                EditorGUI.BeginChangeCheck();
                int newSrcIdx = EditorGUILayout.Popup(srcIdx, _srcLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    modeProp.enumValueIndex = (int)_srcModes[newSrcIdx];
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                    curMode = _srcModes[newSrcIdx];
                    srcIdx  = newSrcIdx;
                }
            }

            EditorGUI.BeginChangeCheck();
            switch (_srcModes[srcIdx])
            {
                case ParamSourceMode.FixedValue:
                    fixedP.stringValue = EventBusEditorUtils.DrawTypedField("Value", fixedP.stringValue, pType);
                    break;

                case ParamSourceMode.ComponentField:
                {
                    var tObj2 = bp.FindPropertyRelative("targetObject").objectReferenceValue;
                    if (tObj2 == null) { EditorGUILayout.HelpBox("Assign component first.", MessageType.Warning); break; }
                    var mems = EventBusEditorUtils.GetComponentMembers(tObj2, pType)
                        .Concat(EventBusEditorUtils.GetGameObjectMembers().Where(m =>
                        {
                            var mt = m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType;
                            return EventBusEditorUtils.IsCompatible(mt, pType);
                        })).ToArray();
                    if (mems.Length == 0) { EditorGUILayout.HelpBox($"No compatible members of type {pType.Name}.", MessageType.Warning); break; }
                    string[] cN = mems.Select(m => m.Name).ToArray();
                    string[] cL = mems.Select(m => {
                        var mt  = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType;
                        string o = m.DeclaringType == typeof(GameObject) ? "GO" : tObj2.GetType().Name;
                        return $"{o}.{m.Name} ({mt.Name})";
                    }).ToArray();
                    int cur2 = Mathf.Max(0, Array.IndexOf(cN, compP.stringValue));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Member", GUILayout.Width(80));
                        int nIdx = EditorGUILayout.Popup(cur2, cL);
                        if (nIdx != cur2) compP.stringValue = cN[nIdx];
                    }
                    break;
                }

                case ParamSourceMode.Toggle:
                {
                    if (pType != typeof(bool)) { EditorGUILayout.HelpBox("Toggle only works with bool parameters.", MessageType.Warning); break; }
                    var tObj3 = bp.FindPropertyRelative("targetObject").objectReferenceValue;
                    if (tObj3 == null) { EditorGUILayout.HelpBox("Assign component first.", MessageType.Warning); break; }
                    var boolMems = EventBusEditorUtils.GetComponentMembers(tObj3, typeof(bool))
                        .Concat(EventBusEditorUtils.GetGameObjectMembers().Where(m =>
                        {
                            var mt = m is FieldInfo fb ? fb.FieldType : ((PropertyInfo)m).PropertyType;
                            return mt == typeof(bool);
                        })).ToArray();
                    if (boolMems.Length == 0) { EditorGUILayout.HelpBox("No se encontraron miembros bool.", MessageType.Warning); break; }
                    string[] tN = boolMems.Select(m => m.Name).ToArray();
                    string[] tL = boolMems.Select(m => {
                        string own = m.DeclaringType == typeof(GameObject) ? "GO" : tObj3.GetType().Name;
                        return $"!{own}.{m.Name}";
                    }).ToArray();
                    int curT = Mathf.Max(0, Array.IndexOf(tN, compP.stringValue));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.color = new Color(1f, 0.85f, 0.5f);
                        EditorGUILayout.LabelField("Toggle", GUILayout.Width(80));
                        GUI.color = Color.white;
                        compP.stringValue = tN[EditorGUILayout.Popup(curT, tL)];
                    }
                    GUI.color = new Color(1f, 0.9f, 0.5f);
                    EditorGUILayout.LabelField($" pasa: !{tObj3.GetType().Name}.{compP.stringValue}", EditorStyles.miniLabel);
                    GUI.color = Color.white;
                    break;
                }

                case ParamSourceMode.ObjectReference:
                {
                    EventBusEditorUtils.DrawObjectField("Object", fp.FindPropertyRelative("objectReference"), pType);
                    break;
                }

                case ParamSourceMode.CallerObject:
                {
                    GUI.color = new Color(0.5f, 1f, 0.7f);
                    EditorGUILayout.LabelField($"  ← runtime: caller.GetComponent<{pType.Name}>()", EditorStyles.miniLabel);
                    GUI.color = Color.white;
                    break;
                }
                
                case ParamSourceMode.CallerRoot:
                {
                    GUI.color = new Color(0.4f, 0.8f, 1f); // azul distinto al verde de CallerObject
                    EditorGUILayout.LabelField(
                        $"  ← runtime: caller.transform.root.GetComponent<{pType.Name}>()",
                        EditorStyles.miniLabel);
                    GUI.color = Color.white;
                    break;
                }
            }

            if (EditorGUI.EndChangeCheck()) { serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
            EditorGUILayout.EndVertical();
        }
    }

    // ── Conditions (ComponentVsLiteral only — no event payload) ──────────
    private void DrawConditions(SerializedProperty bp, SerializedProperty targetProp)
    {
        var tObj = targetProp.objectReferenceValue;
        if (tObj == null) return;

        var condsProp = bp.FindPropertyRelative("conditions");
        if (condsProp == null) return;

        EditorGUILayout.Space(2);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Conditions (AND)", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add", GUILayout.Width(50)))
            {
                condsProp.InsertArrayElementAtIndex(condsProp.arraySize);
                serializedObject.ApplyModifiedProperties();
                var nc  = condsProp.GetArrayElementAtIndex(condsProp.arraySize - 1);
                var ep  = nc.FindPropertyRelative("enabled");   if (ep  != null) ep.boolValue    = true;
                var sp2 = nc.FindPropertyRelative("source");    if (sp2 != null) sp2.enumValueIndex = (int)ConditionSource.ComponentVsLiteral;
                var lv  = nc.FindPropertyRelative("compareValue"); if (lv != null) lv.stringValue = "";
                var lft = nc.FindPropertyRelative("componentFieldName"); if (lft != null) lft.stringValue = "";
                serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target);
            }
        }

        int condToDelete = -1;
        for (int ci = 0; ci < condsProp.arraySize; ci++)
        {
            var cp      = condsProp.GetArrayElementAtIndex(ci);
            var enableP      = cp.FindPropertyRelative("enabled");
            var leftP        = cp.FindPropertyRelative("componentFieldName");
            var opP          = cp.FindPropertyRelative("op");
            var rightP       = cp.FindPropertyRelative("compareValue");
            var srcP         = cp.FindPropertyRelative("source");
            var compareObjP      = cp.FindPropertyRelative("compareObject");
            var compareObjFieldP = cp.FindPropertyRelative("compareObjectField");

            EditorGUILayout.BeginVertical(GUI.skin.box);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (enableP != null)
                {
                    bool newEn = EditorGUILayout.Toggle(enableP.boolValue, GUILayout.Width(16));
                    if (newEn != enableP.boolValue) { enableP.boolValue = newEn; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                }
                EditorGUILayout.LabelField($"Condition {ci + 1}", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("X", GUILayout.Width(20))) condToDelete = ci;
            }

            // Source picker: ComponentVsLiteral or ExternalObjectVsLiteral
            var curSrc = (srcP != null) ? (ConditionSource)srcP.enumValueIndex : ConditionSource.ComponentVsLiteral;
            if (curSrc != ConditionSource.ComponentVsLiteral && curSrc != ConditionSource.ExternalObjectVsLiteral)
                curSrc = ConditionSource.ComponentVsLiteral;
            if (srcP != null) srcP.enumValueIndex = (int)curSrc;

            string[] condSrcLabels = { "Component field", "External Object field" };
            int condSrcIdx = curSrc == ConditionSource.ExternalObjectVsLiteral ? 1 : 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Source", GUILayout.Width(50));
                int newCondSrcIdx = EditorGUILayout.Popup(condSrcIdx, condSrcLabels);
                if (newCondSrcIdx != condSrcIdx)
                {
                    curSrc = newCondSrcIdx == 1 ? ConditionSource.ExternalObjectVsLiteral : ConditionSource.ComponentVsLiteral;
                    if (srcP != null) srcP.enumValueIndex = (int)curSrc;
                    serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target);
                }
            }

            if (curSrc == ConditionSource.ComponentVsLiteral)
            {
                // Member picker — all supported fields on the binding's component
                var allMems = tObj.GetType()
                    .GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Cast<MemberInfo>()
                    .Concat(tObj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead))
                    .Where(m => { var mt = m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType; return TypeHelper.IsSupported(mt); })
                    .ToArray();

                if (allMems.Length > 0)
                {
                    string[] mN = allMems.Select(m => m.Name).ToArray();
                    string[] mL = allMems.Select(m => { var mt = m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType; return $"{m.Name} ({mt.Name})"; }).ToArray();
                    int mCur    = Mathf.Max(0, Array.IndexOf(mN, leftP?.stringValue ?? ""));

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Field", GUILayout.Width(36));
                        int mNew = EditorGUILayout.Popup(mCur, mL);
                        if (mNew != mCur && leftP != null) { leftP.stringValue = mN[mNew]; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                    }

                    int   resolvedIdx = Mathf.Max(0, Array.IndexOf(mN, leftP?.stringValue ?? ""));
                    var   selMem      = allMems[resolvedIdx];
                    Type  selType     = selMem is FieldInfo f5 ? f5.FieldType : ((PropertyInfo)selMem).PropertyType;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Op", GUILayout.Width(24));
                        if (opP != null)
                        {
                            var newOp = (ConditionOperator)EditorGUILayout.EnumPopup((ConditionOperator)opP.enumValueIndex);
                            if ((int)newOp != opP.enumValueIndex) { opP.enumValueIndex = (int)newOp; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Value", GUILayout.Width(38));
                        if (rightP != null)
                        {
                            string newVal = EventBusEditorUtils.DrawTypedField("", rightP.stringValue, selType);
                            if (newVal != rightP.stringValue) { rightP.stringValue = newVal; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                        }
                    }

                    GUI.color = new Color(1f, 0.9f, 0.5f);
                    EditorGUILayout.LabelField(
                        $" if ({leftP?.stringValue} {EventBusEditorUtils.OpSymbol((ConditionOperator)(opP?.enumValueIndex ?? 0))} {rightP?.stringValue})",
                        EditorStyles.miniLabel);
                    GUI.color = Color.white;
                }
                else
                {
                    EditorGUILayout.LabelField("No supported members on component.", EditorStyles.miniLabel);
                }
            }
            else // ExternalObjectVsLiteral
            {
                bool extChanged = EventBusEditorUtils.DrawConditionObjectPicker(compareObjP);
                if (extChanged) { if (compareObjFieldP != null) compareObjFieldP.stringValue = ""; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }

                var extObj = compareObjP?.objectReferenceValue;
                if (extObj == null)
                {
                    EditorGUILayout.LabelField("Assign an external object above.", EditorStyles.miniLabel);
                }
                else
                {
                    var extMems = EventBusEditorUtils.GetComponentMembers(extObj, null)
                        .Concat(EventBusEditorUtils.GetGameObjectMembers()).ToArray();

                    if (extMems.Length > 0)
                    {
                        string[] eN = extMems.Select(m => m.Name).ToArray();
                        string[] eL = extMems.Select(m =>
                        {
                            var mt   = m is FieldInfo fe ? fe.FieldType : ((PropertyInfo)m).PropertyType;
                            string o = m.DeclaringType == typeof(GameObject) ? "GO" : extObj.GetType().Name;
                            return $"{o}.{m.Name} ({mt.Name})";
                        }).ToArray();
                        int eCur = Mathf.Max(0, Array.IndexOf(eN, compareObjFieldP?.stringValue ?? ""));

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("Field", GUILayout.Width(36));
                            int eNew = EditorGUILayout.Popup(eCur, eL);
                            if (eNew != eCur && compareObjFieldP != null) { compareObjFieldP.stringValue = eN[eNew]; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                        }

                        int   eResolvedIdx = Mathf.Max(0, Array.IndexOf(eN, compareObjFieldP?.stringValue ?? ""));
                        var   eSelMem      = extMems[eResolvedIdx];
                        Type  eSelType     = eSelMem is FieldInfo esf ? esf.FieldType : ((PropertyInfo)eSelMem).PropertyType;

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("Op", GUILayout.Width(24));
                            if (opP != null)
                            {
                                var newOp = (ConditionOperator)EditorGUILayout.EnumPopup((ConditionOperator)opP.enumValueIndex);
                                if ((int)newOp != opP.enumValueIndex) { opP.enumValueIndex = (int)newOp; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                            }
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("Value", GUILayout.Width(38));
                            if (rightP != null)
                            {
                                string newVal = EventBusEditorUtils.DrawTypedField("", rightP.stringValue, eSelType);
                                if (newVal != rightP.stringValue) { rightP.stringValue = newVal; serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(target); }
                            }
                        }

                        string eOwn = eSelMem.DeclaringType == typeof(GameObject) ? "GO" : extObj.GetType().Name;
                        GUI.color = new Color(1f, 0.9f, 0.5f);
                        EditorGUILayout.LabelField(
                            $" if ({eOwn}.{compareObjFieldP?.stringValue} {EventBusEditorUtils.OpSymbol((ConditionOperator)(opP?.enumValueIndex ?? 0))} {rightP?.stringValue})",
                            EditorStyles.miniLabel);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        EditorGUILayout.LabelField("No supported members on external object.", EditorStyles.miniLabel);
                    }
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(1);
        }

        if (condToDelete >= 0)
        {
            condsProp.DeleteArrayElementAtIndex(condToDelete);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
    }
}
