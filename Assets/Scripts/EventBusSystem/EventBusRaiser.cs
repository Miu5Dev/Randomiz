using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
#endif

// ─────────────────────────────────────────────────────────────────────────────
// RaiserParam — stores the serialized value for one event field
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>Serialized value for one event field used by <see cref="EventBusRaiser"/> (a literal value or an object reference).</summary>
[Serializable]
public class RaiserParam
{
    public string             fieldName = "";
    public string             value     = "";          // for value-type fields
    public UnityEngine.Object objectRef = null;        // for UnityEngine.Object fields
}

// ─────────────────────────────────────────────────────────────────────────────
// EventBusRaiser
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>Component that raises a chosen EventBus event (configured in the inspector) on selected lifecycle callbacks (Awake/Start/Enable/Disable).</summary>
public class EventBusRaiser : MonoBehaviour
{
    [HideInInspector] public string eventsFolder           = "Scripts/EventBusSystem/Events";
    [HideInInspector] public string selectedEventTypeName  = "";
    [HideInInspector] public List<RaiserParam> paramValues = new();

    [HideInInspector] public bool raiseOnAwake   = false;
    [HideInInspector] public bool raiseOnStart   = false;
    [HideInInspector] public bool raiseOnEnable  = false;
    [HideInInspector] public bool raiseOnDisable = false;

    // ── Lifecycle ────────────────────────────────────────────────────────────
    private void Awake()     { if (raiseOnAwake)   Raise(); }
    private void Start()     { if (raiseOnStart)   Raise(); }
    private void OnEnable()  { if (raiseOnEnable)  Raise(); }
    private void OnDisable() { if (raiseOnDisable) Raise(); }

    // ── Public raise ─────────────────────────────────────────────────────────
    public void Raise()
    {
        if (string.IsNullOrEmpty(selectedEventTypeName))
        { Debug.LogWarning("[EventBusRaiser] No event type selected.", this); return; }

        Type eventType = EventBusListener.ResolveType(selectedEventTypeName);
        if (eventType == null)
        { Debug.LogWarning($"[EventBusRaiser] Type '{selectedEventTypeName}' not found.", this); return; }

        try
        {
            object instance = Activator.CreateInstance(eventType);

            foreach (var param in paramValues)
            {
                if (string.IsNullOrEmpty(param.fieldName)) continue;
                var field = eventType.GetField(param.fieldName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (field == null)
                { Debug.LogWarning($"[EventBusRaiser] Field '{param.fieldName}' not found on '{eventType.Name}'.", this); continue; }
                try
                {
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                    {
                        // Object reference — assign directly (null is valid)
                        field.SetValue(instance, param.objectRef);
                    }
                    else
                    {
                        field.SetValue(instance, TypeHelper.Parse(param.value, field.FieldType));
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[EventBusRaiser] Cannot set '{param.fieldName}': {e.Message}", this); }
            }

            typeof(EventBus)
                .GetMethod("Raise", BindingFlags.Public | BindingFlags.Static)
                .MakeGenericMethod(eventType)
                .Invoke(null, new[] { instance });
        }
        catch (Exception e)
        { Debug.LogError($"[EventBusRaiser] Failed to raise '{selectedEventTypeName}': {e.Message}", this); }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Editor
// ─────────────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR

/// <summary>Custom inspector for <see cref="EventBusRaiser"/>: event picker and per-field value editor.</summary>
[CustomEditor(typeof(EventBusRaiser))]
public class EventBusRaiserEditor : Editor
{
    // Shared event scan cache
    private static string   _cachedFolder = null;
    private static string[] _cachedNames  = Array.Empty<string>();
    private static double   _lastScan     = -1;
    private const  double   COOLDOWN      = 5.0;

    private static readonly System.Globalization.CultureInfo IC =
        System.Globalization.CultureInfo.InvariantCulture;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var folderProp    = serializedObject.FindProperty("eventsFolder");
        var eventNameProp = serializedObject.FindProperty("selectedEventTypeName");
        var paramsProp    = serializedObject.FindProperty("paramValues");
        var onAwakeProp   = serializedObject.FindProperty("raiseOnAwake");
        var onStartProp   = serializedObject.FindProperty("raiseOnStart");
        var onEnableProp  = serializedObject.FindProperty("raiseOnEnable");
        var onDisableProp = serializedObject.FindProperty("raiseOnDisable");

        // ── Header ──────────────────────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("EventBus Raiser", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // ── Folder picker ────────────────────────────────────────────────────
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(folderProp.stringValue);
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string start = Path.Combine(Application.dataPath, folderProp.stringValue);
                if (!Directory.Exists(start)) start = Application.dataPath;
                string chosen = EditorUtility.OpenFolderPanel("Select Events Folder", start, "");
                if (!string.IsNullOrEmpty(chosen) && chosen.StartsWith(Application.dataPath))
                {
                    folderProp.stringValue = chosen.Substring(Application.dataPath.Length + 1).Replace('\\', '/');
                    serializedObject.ApplyModifiedProperties();
                    _cachedFolder = null; _lastScan = -1;
                }
            }
        }

        // ── Scan & event picker ───────────────────────────────────────────────
        ScanIfNeeded(folderProp.stringValue);

        if (_cachedNames.Length == 0)
        { EditorGUILayout.HelpBox("No events found in the folder.", MessageType.Warning); }
        else
        {
            EditorGUILayout.Space(4);
            int curIdx = Mathf.Max(0, Array.IndexOf(_cachedNames, eventNameProp.stringValue));
            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup("Event", curIdx, _cachedNames);
            if (EditorGUI.EndChangeCheck() || string.IsNullOrEmpty(eventNameProp.stringValue))
            {
                eventNameProp.stringValue = _cachedNames[newIdx];
                RebuildParams(paramsProp, _cachedNames[newIdx]);
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        // ── Parameter values ─────────────────────────────────────────────────
        Type evtType = EventBusListener.ResolveType(eventNameProp.stringValue);
        if (evtType != null)
        {
            var fields = evtType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            if (fields.Length == 0)
                EditorGUILayout.HelpBox("This event has no fields.", MessageType.Info);
            else
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Parameters", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                SyncParamList(paramsProp, fields);

                for (int i = 0; i < fields.Length; i++)
                {
                    if (i >= paramsProp.arraySize) break;
                    var elem       = paramsProp.GetArrayElementAtIndex(i);
                    var fieldNameP = elem?.FindPropertyRelative("fieldName");
                    var valueP     = elem?.FindPropertyRelative("value");
                    if (fieldNameP == null || valueP == null) continue;

                    fieldNameP.stringValue = fields[i].Name; // keep in sync

                    bool isObjType = typeof(UnityEngine.Object).IsAssignableFrom(fields[i].FieldType);
                    if (isObjType)
                    {
                        // Drag & drop object reference field
                        var objRefP = elem.FindPropertyRelative("objectRef");
                        if (objRefP != null)
                        {
                            EditorGUI.BeginChangeCheck();
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField(
                                    $"{fields[i].Name}  ({fields[i].FieldType.Name})",
                                    GUILayout.Width(200));
                                EditorGUILayout.ObjectField(objRefP, fields[i].FieldType, GUIContent.none);
                            }
                            if (EditorGUI.EndChangeCheck())
                            {
                                serializedObject.ApplyModifiedProperties();
                                EditorUtility.SetDirty(target);
                            }
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        string newVal = DrawTypedField(
                            $"{fields[i].Name}  ({fields[i].FieldType.Name})",
                            valueP.stringValue,
                            fields[i].FieldType);
                        if (EditorGUI.EndChangeCheck())
                        {
                            valueP.stringValue = newVal;
                            serializedObject.ApplyModifiedProperties();
                            EditorUtility.SetDirty(target);
                        }
                    }
                }

                EditorGUILayout.EndVertical();
            }
        }

        // ── Lifecycle toggles ────────────────────────────────────────────────
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Raise On", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawToggle(onAwakeProp,   "Awake");
        DrawToggle(onStartProp,   "Start");
        DrawToggle(onEnableProp,  "OnEnable");
        DrawToggle(onDisableProp, "OnDisable");
        EditorGUILayout.EndVertical();

        // ── Raise Now button ─────────────────────────────────────────────────
        EditorGUILayout.Space(10);
        bool canRaise = Application.isPlaying && !string.IsNullOrEmpty(eventNameProp.stringValue);
        EditorGUI.BeginDisabledGroup(!canRaise);
        if (GUILayout.Button("▶  Raise Now", GUILayout.Height(28)))
            ((EventBusRaiser)target).Raise();
        EditorGUI.EndDisabledGroup();
        if (!Application.isPlaying)
            EditorGUILayout.LabelField("(Play Mode only)", EditorStyles.centeredGreyMiniLabel);

        serializedObject.ApplyModifiedProperties();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void DrawToggle(SerializedProperty prop, string label)
    {
        using var h = new EditorGUILayout.HorizontalScope();
        EditorGUI.BeginChangeCheck();
        bool newVal = EditorGUILayout.Toggle(prop.boolValue, GUILayout.Width(18));
        EditorGUILayout.LabelField(label);
        if (EditorGUI.EndChangeCheck())
        {
            prop.boolValue = newVal;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
    }

    private void RebuildParams(SerializedProperty paramsProp, string typeName)
    {
        paramsProp.ClearArray();
        serializedObject.ApplyModifiedProperties();
        Type t = EventBusListener.ResolveType(typeName);
        if (t == null) return;
        var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < fields.Length; i++)
        {
            paramsProp.InsertArrayElementAtIndex(i);
            serializedObject.ApplyModifiedProperties();
            var elem = paramsProp.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("fieldName").stringValue = fields[i].Name;
            elem.FindPropertyRelative("value").stringValue     = "";
        }
        serializedObject.ApplyModifiedProperties();
    }

    private void SyncParamList(SerializedProperty paramsProp, FieldInfo[] fields)
    {
        while (paramsProp.arraySize < fields.Length)
        { paramsProp.InsertArrayElementAtIndex(paramsProp.arraySize); serializedObject.ApplyModifiedProperties(); }
        while (paramsProp.arraySize > fields.Length)
        { paramsProp.DeleteArrayElementAtIndex(paramsProp.arraySize - 1); serializedObject.ApplyModifiedProperties(); }
    }

    private static void ScanIfNeeded(string folder)
    {
        double now = EditorApplication.timeSinceStartup;
        if (_cachedNames.Length > 0 && _cachedFolder == folder && now - _lastScan < COOLDOWN) return;
        _cachedFolder = folder; _lastScan = now;
        if (string.IsNullOrEmpty(folder)) { _cachedNames = Array.Empty<string>(); return; }
        string fullPath = Path.Combine(Application.dataPath, folder);
        if (!Directory.Exists(fullPath)) { _cachedNames = Array.Empty<string>(); return; }
        var regex = new Regex(@"public\s+class\s+(On\w+Event)\b", RegexOptions.Compiled);
        var names = new List<string>();
        foreach (string file in Directory.GetFiles(fullPath, "*.cs", SearchOption.AllDirectories))
            foreach (Match m in regex.Matches(File.ReadAllText(file)))
                names.Add(m.Groups[1].Value);
        _cachedNames = names.Distinct().OrderBy(n => n).ToArray();
    }

    private static string DrawTypedField(string label, string current, Type t)
    {
        // NOTE: UnityEngine.Object types are handled separately via DrawObjectField
        // Simple scalar types — horizontal label + control
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(200));
            if (t == typeof(bool))   { string[] o = {"true","false"}; return o[EditorGUILayout.Popup(current=="false"?1:0, o)]; }
            if (t.IsEnum)            { var v = Enum.GetNames(t); int c = Mathf.Max(0, Array.IndexOf(v, current)); return v[EditorGUILayout.Popup(c, v)]; }
            if (t == typeof(int))    { int.TryParse(current, out int vi);   return EditorGUILayout.IntField(vi).ToString(); }
            if (t == typeof(float))  { float.TryParse(current,  System.Globalization.NumberStyles.Float, IC, out float vf);  return EditorGUILayout.FloatField(vf).ToString(IC); }
            if (t == typeof(double)) { double.TryParse(current, System.Globalization.NumberStyles.Float, IC, out double vd); return EditorGUILayout.DoubleField(vd).ToString(IC); }
            if (t == typeof(long))   { long.TryParse(current, out long vl); return EditorGUILayout.LongField(vl).ToString(); }
            if (t == typeof(string)) return EditorGUILayout.TextField(current ?? "");
            // Fallback for unknown types: end horizontal here and fall through
        }

        // Multi-component types — EditorGUILayout already draws its own label
        if (t == typeof(Vector2))    { var f=SF(current,2); var v=EditorGUILayout.Vector2Field(label,new Vector2(f[0],f[1])); return $"{v.x.ToString(IC)},{v.y.ToString(IC)}"; }
        if (t == typeof(Vector3))    { var f=SF(current,3); var v=EditorGUILayout.Vector3Field(label,new Vector3(f[0],f[1],f[2])); return $"{v.x.ToString(IC)},{v.y.ToString(IC)},{v.z.ToString(IC)}"; }
        if (t == typeof(Vector4))    { var f=SF(current,4); var v=EditorGUILayout.Vector4Field(label,new Vector4(f[0],f[1],f[2],f[3])); return $"{v.x.ToString(IC)},{v.y.ToString(IC)},{v.z.ToString(IC)},{v.w.ToString(IC)}"; }
        if (t == typeof(Vector2Int)) { var f=SI(current,2); var v=EditorGUILayout.Vector2IntField(label,new Vector2Int(f[0],f[1])); return $"{v.x},{v.y}"; }
        if (t == typeof(Vector3Int)) { var f=SI(current,3); var v=EditorGUILayout.Vector3IntField(label,new Vector3Int(f[0],f[1],f[2])); return $"{v.x},{v.y},{v.z}"; }
        if (t == typeof(Color) || t == typeof(Color32)) { var f=SF(current,4,1f); var v=EditorGUILayout.ColorField(label,new Color(f[0],f[1],f[2],f[3])); return $"{v.r.ToString(IC)},{v.g.ToString(IC)},{v.b.ToString(IC)},{v.a.ToString(IC)}"; }
        if (t == typeof(Quaternion)) { var f=SF(current,4); var q=new Quaternion(f[0],f[1],f[2],f[3]); var eu=EditorGUILayout.Vector3Field(label,q.eulerAngles); var qr=Quaternion.Euler(eu); return $"{qr.x.ToString(IC)},{qr.y.ToString(IC)},{qr.z.ToString(IC)},{qr.w.ToString(IC)}"; }
        if (t == typeof(Rect))    { var f=SF(current,4); var v=EditorGUILayout.RectField(label,new Rect(f[0],f[1],f[2],f[3])); return $"{v.x.ToString(IC)},{v.y.ToString(IC)},{v.width.ToString(IC)},{v.height.ToString(IC)}"; }
        if (t == typeof(Bounds))  { var f=SF(current,6); var v=EditorGUILayout.BoundsField(label,new Bounds(new Vector3(f[0],f[1],f[2]),new Vector3(f[3],f[4],f[5]))); return $"{v.center.x.ToString(IC)},{v.center.y.ToString(IC)},{v.center.z.ToString(IC)},{v.size.x.ToString(IC)},{v.size.y.ToString(IC)},{v.size.z.ToString(IC)}"; }
        if (t == typeof(RectInt)) { var f=SI(current,4); var v=EditorGUILayout.RectIntField(label,new RectInt(f[0],f[1],f[2],f[3])); return $"{v.x},{v.y},{v.width},{v.height}"; }
        if (t == typeof(BoundsInt)){ var f=SI(current,6); var v=EditorGUILayout.BoundsIntField(label,new BoundsInt(f[0],f[1],f[2],f[3],f[4],f[5])); return $"{v.position.x},{v.position.y},{v.position.z},{v.size.x},{v.size.y},{v.size.z}"; }

        // Unknown — text fallback
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(200));
            return EditorGUILayout.TextField(current ?? "");
        }
    }

    private static float[] SF(string s, int n, float def = 0f)
    {
        var p = (s ?? "").Split(','); var r = new float[n];
        for (int i = 0; i < n; i++) { if (i < p.Length) float.TryParse(p[i].Trim(), System.Globalization.NumberStyles.Float, IC, out r[i]); else r[i] = def; }
        return r;
    }
    private static int[] SI(string s, int n)
    {
        var p = (s ?? "").Split(','); var r = new int[n];
        for (int i = 0; i < n; i++) { if (i < p.Length) int.TryParse(p[i].Trim(), out r[i]); }
        return r;
    }
}

#endif
