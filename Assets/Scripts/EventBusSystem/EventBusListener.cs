using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
#endif

// =========================================================
// TYPE SERIALIZATION HELPER
// Converts between string storage and Unity/C# types for
// conditions and fixed-value parameters
// =========================================================
public static class TypeHelper
{
    private static readonly System.Globalization.CultureInfo IC =
        System.Globalization.CultureInfo.InvariantCulture;

    public static bool IsSupported(Type t) =>
        t == typeof(int)        || t == typeof(float)      || t == typeof(double)    ||
        t == typeof(long)       || t == typeof(bool)       || t == typeof(string)    ||
        t == typeof(Vector2)    || t == typeof(Vector3)    || t == typeof(Vector4)   ||
        t == typeof(Vector2Int) || t == typeof(Vector3Int) ||
        t == typeof(Color)      || t == typeof(Color32)    ||
        t == typeof(Quaternion) || t == typeof(Rect)       ||
        t == typeof(Bounds)     || t == typeof(RectInt)    ||
        t == typeof(BoundsInt)  || t.IsEnum;

    public static bool IsOrdered(Type t) =>
        t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long);

    public static object Parse(string s, Type t)
    {
        if (string.IsNullOrEmpty(s)) s = "";
        if (t == typeof(int))        return int.Parse(s, IC);
        if (t == typeof(float))      return float.Parse(s, IC);
        if (t == typeof(double))     return double.Parse(s, IC);
        if (t == typeof(long))       return long.Parse(s, IC);
        if (t == typeof(bool))       return bool.Parse(s);
        if (t == typeof(string))     return s;
        if (t.IsEnum)                return Enum.Parse(t, s);
        if (t == typeof(Vector2))    return ParseV2(s);
        if (t == typeof(Vector3))    return ParseV3(s);
        if (t == typeof(Vector4))    return ParseV4(s);
        if (t == typeof(Vector2Int)) return ParseV2Int(s);
        if (t == typeof(Vector3Int)) return ParseV3Int(s);
        if (t == typeof(Color))      return ParseColor(s);
        if (t == typeof(Color32))    { var c = ParseColor(s); return (Color32)c; }
        if (t == typeof(Quaternion)) return ParseQuat(s);
        if (t == typeof(Rect))       return ParseRect(s);
        if (t == typeof(Bounds))     return ParseBounds(s);
        if (t == typeof(RectInt))    return ParseRectInt(s);
        if (t == typeof(BoundsInt))  return ParseBoundsInt(s);
        throw new NotSupportedException($"TypeHelper: unsupported type {t.Name}");
    }

    public static string Serialize(object v, Type t)
    {
        if (v == null) return "";
        if (t == typeof(float))      return ((float)v).ToString(IC);
        if (t == typeof(double))     return ((double)v).ToString(IC);
        if (t == typeof(Vector2))    { var u=(Vector2)v;    return $"{u.x.ToString(IC)},{u.y.ToString(IC)}"; }
        if (t == typeof(Vector3))    { var u=(Vector3)v;    return $"{u.x.ToString(IC)},{u.y.ToString(IC)},{u.z.ToString(IC)}"; }
        if (t == typeof(Vector4))    { var u=(Vector4)v;    return $"{u.x.ToString(IC)},{u.y.ToString(IC)},{u.z.ToString(IC)},{u.w.ToString(IC)}"; }
        if (t == typeof(Vector2Int)) { var u=(Vector2Int)v; return $"{u.x},{u.y}"; }
        if (t == typeof(Vector3Int)) { var u=(Vector3Int)v; return $"{u.x},{u.y},{u.z}"; }
        if (t == typeof(Color))      { var u=(Color)v;      return $"{u.r.ToString(IC)},{u.g.ToString(IC)},{u.b.ToString(IC)},{u.a.ToString(IC)}"; }
        if (t == typeof(Color32))    { var u=(Color32)v;    return $"{u.r},{u.g},{u.b},{u.a}"; }
        if (t == typeof(Quaternion)) { var u=(Quaternion)v; return $"{u.x.ToString(IC)},{u.y.ToString(IC)},{u.z.ToString(IC)},{u.w.ToString(IC)}"; }
        if (t == typeof(Rect))       { var u=(Rect)v;       return $"{u.x.ToString(IC)},{u.y.ToString(IC)},{u.width.ToString(IC)},{u.height.ToString(IC)}"; }
        if (t == typeof(Bounds))     { var u=(Bounds)v;     return $"{u.center.x.ToString(IC)},{u.center.y.ToString(IC)},{u.center.z.ToString(IC)},{u.size.x.ToString(IC)},{u.size.y.ToString(IC)},{u.size.z.ToString(IC)}"; }
        if (t == typeof(RectInt))    { var u=(RectInt)v;    return $"{u.x},{u.y},{u.width},{u.height}"; }
        if (t == typeof(BoundsInt))  { var u=(BoundsInt)v;  return $"{u.x},{u.y},{u.z},{u.size.x},{u.size.y},{u.size.z}"; }
        return v.ToString();
    }

    private static float[] Floats(string s, int n)
    {
        var parts = s.Split(',');
        var r = new float[n];
        for (int i = 0; i < n && i < parts.Length; i++)
            float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, IC, out r[i]);
        return r;
    }
    private static int[] Ints(string s, int n)
    {
        var parts = s.Split(',');
        var r = new int[n];
        for (int i = 0; i < n && i < parts.Length; i++)
            int.TryParse(parts[i].Trim(), out r[i]);
        return r;
    }

    private static Vector2    ParseV2(string s)      { var f=Floats(s,2); return new Vector2(f[0],f[1]); }
    private static Vector3    ParseV3(string s)      { var f=Floats(s,3); return new Vector3(f[0],f[1],f[2]); }
    private static Vector4    ParseV4(string s)      { var f=Floats(s,4); return new Vector4(f[0],f[1],f[2],f[3]); }
    private static Vector2Int ParseV2Int(string s)   { var i=Ints(s,2);   return new Vector2Int(i[0],i[1]); }
    private static Vector3Int ParseV3Int(string s)   { var i=Ints(s,3);   return new Vector3Int(i[0],i[1],i[2]); }
    private static Color      ParseColor(string s)   { var f=Floats(s,4); return new Color(f[0],f[1],f[2], f.Length>3?f[3]:1f); }
    private static Quaternion ParseQuat(string s)    { var f=Floats(s,4); return new Quaternion(f[0],f[1],f[2],f[3]); }
    private static Rect       ParseRect(string s)    { var f=Floats(s,4); return new Rect(f[0],f[1],f[2],f[3]); }
    private static RectInt    ParseRectInt(string s) { var i=Ints(s,4);   return new RectInt(i[0],i[1],i[2],i[3]); }
    private static Bounds     ParseBounds(string s)  { var f=Floats(s,6); return new Bounds(new Vector3(f[0],f[1],f[2]),new Vector3(f[3],f[4],f[5])); }
    private static BoundsInt  ParseBoundsInt(string s){ var i=Ints(s,6);  return new BoundsInt(i[0],i[1],i[2],i[3],i[4],i[5]); }
}

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

    // Smart bindings: each one maps N event fields → a method with N params
    [HideInInspector] public List<SmartBinding> smartBindings = new();

    // =========================================================
    // RUNTIME
    // =========================================================
    private static Dictionary<string, Type> _typeCache;

    public static Type ResolveType(string typeName)
    {
        if (_typeCache == null)
        {
            _typeCache = new Dictionary<string, Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                try { foreach (var t in asm.GetTypes()) if (!_typeCache.ContainsKey(t.Name)) _typeCache[t.Name] = t; }
                catch { }
        }
        return _typeCache.TryGetValue(typeName, out var type) ? type : null;
    }

    public static MemberInfo GetBoolMember(Type t)
    {
        const string P = "pressed";
        return (MemberInfo)t.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(f => f.FieldType == typeof(bool) && f.Name.Equals(P, StringComparison.OrdinalIgnoreCase))
            ?? t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.PropertyType == typeof(bool) && p.Name.Equals(P, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ReadBool(MemberInfo m, object obj)
    {
        if (m is FieldInfo fi) return (bool)fi.GetValue(obj);
        if (m is PropertyInfo pi) return (bool)pi.GetValue(obj);
        return true;
    }

    private Delegate _subscribedDelegate;
    private MethodInfo _unsubscribeMethod;

    private void OnEnable()
    {
        Unsubscribe();
        Subscribe(selectedEventTypeName);
    }
    private void OnDisable() => Unsubscribe();

    private void Subscribe(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return;
        Type eventType = ResolveType(typeName);
        if (eventType == null) { Debug.LogWarning($"[EventBusListener] Type not found: '{typeName}'"); return; }

        MemberInfo boolMember = GetBoolMember(eventType);
        bool isButtonEvent = boolMember != null;

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

            // Phase 2: execute with chain logic
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

            if (!isButtonEvent) { onRaised?.Invoke(); return; }
            bool pressed = ReadBool(boolMember, evtObj);
            if (callOnBothStates) { onRaised?.Invoke(); return; }
            if (pressed) onRaised?.Invoke();
            else onReleased?.Invoke();
        };

        var wrapper = typeof(EventBusListener)
            .GetMethod(nameof(CreateTypedCallback), BindingFlags.NonPublic | BindingFlags.Static)
            .MakeGenericMethod(eventType);

        _subscribedDelegate = (Delegate)wrapper.Invoke(null, new object[] { callback });

        _unsubscribeMethod = typeof(EventBus)
            .GetMethod("Unsubscribe", BindingFlags.Public | BindingFlags.Static)
            .MakeGenericMethod(eventType);

        typeof(EventBus)
            .GetMethod("Subscribe", BindingFlags.Public | BindingFlags.Static)
            .MakeGenericMethod(eventType)
            .Invoke(null, new object[] { _subscribedDelegate });
    }

    private static Action<T> CreateTypedCallback<T>(Action<object> inner) => evt => inner(evt);

    private void Unsubscribe()
    {
        if (_subscribedDelegate == null || _unsubscribeMethod == null) return;
        _unsubscribeMethod.Invoke(null, new object[] { _subscribedDelegate });
        _subscribedDelegate = null;
        _unsubscribeMethod = null;
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

// =========================================================
// CONDITION
// =========================================================
public enum ConditionOperator { Equals, NotEquals, GreaterThan, LessThan, GreaterOrEqual, LessOrEqual }

public enum ConditionSource
{
    EventField,          // event field vs literal value
    ComponentField,      // event field vs component field
    ComponentVsLiteral   // component field vs literal (no event field)
}

[Serializable]
public class BindingCondition
{
    public bool enabled = false;
    public string fieldName = "";
    public ConditionOperator op = ConditionOperator.Equals;
    public ConditionSource source = ConditionSource.EventField;
    public string compareValue = "";
    public string componentFieldName = "";

    public Func<object, bool> Compile(Type eventType, UnityEngine.Object targetObject = null)
    {
        if (!enabled) return null;

        if (source == ConditionSource.ComponentVsLiteral)
        {
            if (targetObject == null || string.IsNullOrEmpty(componentFieldName)) return null;
            var goMember = (MemberInfo)typeof(GameObject).GetField(componentFieldName, BindingFlags.Public | BindingFlags.Instance)
                           ?? typeof(GameObject).GetProperty(componentFieldName, BindingFlags.Public | BindingFlags.Instance);
            MemberInfo compMember;
            if (goMember != null)
            {
                compMember = goMember;
                targetObject = targetObject is Component c2 ? (UnityEngine.Object)c2.gameObject : targetObject;
            }
            else
            {
                Type ct = targetObject.GetType();
                compMember = (MemberInfo)ct.GetField(componentFieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                             ?? ct.GetProperty(componentFieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (compMember == null) return null;
            Type compFieldType = compMember is FieldInfo cfi ? cfi.FieldType : ((PropertyInfo)compMember).PropertyType;
            object parsedLiteral;
            try { parsedLiteral = TypeHelper.Parse(compareValue, compFieldType); }
            catch { return null; }
            var capMem = compMember; var capTarget = targetObject;
            var capOp = op; var capLit = parsedLiteral; var capType = compFieldType;
            return _ => {
                object val = capMem is FieldInfo fi2 ? fi2.GetValue(capTarget) : ((PropertyInfo)capMem).GetValue(capTarget);
                return Evaluate(val, capLit, capOp, capType);
            };
        }

        if (string.IsNullOrEmpty(fieldName)) return null;
        var leftMember = (MemberInfo)eventType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)
                         ?? eventType.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (leftMember == null) return null;
        Type leftType = leftMember is FieldInfo lfi ? lfi.FieldType : ((PropertyInfo)leftMember).PropertyType;

        if (source == ConditionSource.EventField)
        {
            object parsedValue;
            try { parsedValue = TypeHelper.Parse(compareValue, leftType); }
            catch { return null; }
            var capLeft = leftMember; var capOp2 = op; var capVal = parsedValue; var capType2 = leftType;
            return evtObj => {
                object lval = capLeft is FieldInfo fi ? fi.GetValue(evtObj) : ((PropertyInfo)capLeft).GetValue(evtObj);
                return Evaluate(lval, capVal, capOp2, capType2);
            };
        }
        else // ComponentField
        {
            if (targetObject == null || string.IsNullOrEmpty(componentFieldName)) return null;
            Type ct2 = targetObject.GetType();
            var rightMember = (MemberInfo)ct2.GetField(componentFieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                              ?? ct2.GetProperty(componentFieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (rightMember == null) return null;
            var capLeft2 = leftMember; var capRight = rightMember;
            var capTarget2 = targetObject; var capOp3 = op; var capType3 = leftType;
            return evtObj => {
                object lval = capLeft2 is FieldInfo lf2 ? lf2.GetValue(evtObj) : ((PropertyInfo)capLeft2).GetValue(evtObj);
                object rval = capRight is FieldInfo rf ? rf.GetValue(capTarget2) : ((PropertyInfo)capRight).GetValue(capTarget2);
                return Evaluate(lval, rval, capOp3, capType3);
            };
        }
    }

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
            return op switch {
                ConditionOperator.Equals       => a == b,
                ConditionOperator.NotEquals    => a != b,
                ConditionOperator.GreaterThan  => a > b,
                ConditionOperator.LessThan     => a < b,
                ConditionOperator.GreaterOrEqual => a >= b,
                ConditionOperator.LessOrEqual  => a <= b,
                _ => false
            };
        }
        // Vector/struct equality (Equals + NotEquals only)
        if (val != null && reference != null)
        {
            bool eq = val.Equals(reference);
            return op == ConditionOperator.Equals ? eq : !eq;
        }
        return false;
    }
}

// =========================================================
// SMART BINDING
// =========================================================
[Serializable]
public enum ParamSourceMode { EventField, FixedValue, ComponentField, WholeEvent }

[Serializable]
public class ParamSource
{
    public ParamSourceMode mode = ParamSourceMode.EventField;
    public string eventFieldName = "";
    public string fixedValue = "";
    public string componentMember = "";
}

public enum BindingLogic { If, ElseIf, Else }

[Serializable]
public class SmartBinding
{
    public UnityEngine.Object targetObject;
    public string methodName = "";
    public List<ParamSource> paramSources = new();
    public List<BindingCondition> conditions = new();
    public BindingLogic logic = BindingLogic.If;

    public bool IsConfigured() =>
        targetObject != null && !string.IsNullOrEmpty(methodName);

    public (Func<object, bool> check, Action<object> execute) CompileSplit(Type eventType)
    {
        if (!IsConfigured()) return (null, null);

        Type targetType = targetObject.GetType();
        MethodInfo method = null;
        foreach (var m in targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            if (m.Name == methodName && m.GetParameters().Length == paramSources.Count) { method = m; break; }
        if (method == null) { Debug.LogWarning($"[SmartBinding] Method '{methodName}' not found on {targetType.Name}"); return (null, null); }

        var mParams = method.GetParameters();
        var resolvers = new Func<object, object>[paramSources.Count];

        for (int i = 0; i < paramSources.Count; i++)
        {
            var ps = paramSources[i];
            var pType = mParams[i].ParameterType;

            if (ps.mode == ParamSourceMode.WholeEvent)
            {
                // Pass the entire event object — no field lookup needed
                if (!pType.IsAssignableFrom(eventType))
                {
                    Debug.LogWarning($"[SmartBinding] WholeEvent: event type '{eventType.Name}' is not assignable to param type '{pType.Name}'");
                    return (null, null);
                }
                resolvers[i] = evtObj => evtObj;
            }
            else if (ps.mode == ParamSourceMode.EventField)
            {
                var evtMember = (MemberInfo)eventType.GetField(ps.eventFieldName, BindingFlags.Public | BindingFlags.Instance)
                                ?? eventType.GetProperty(ps.eventFieldName, BindingFlags.Public | BindingFlags.Instance);
                if (evtMember == null) { Debug.LogWarning($"[SmartBinding] Event field '{ps.eventFieldName}' not found"); return (null, null); }
                var cap = evtMember;
                resolvers[i] = evtObj => cap is FieldInfo fi ? fi.GetValue(evtObj) : ((PropertyInfo)cap).GetValue(evtObj);
            }
            else if (ps.mode == ParamSourceMode.FixedValue)
            {
                object parsed;
                try { parsed = TypeHelper.Parse(ps.fixedValue, pType); }
                catch { Debug.LogWarning($"[SmartBinding] Could not parse '{ps.fixedValue}' as {pType.Name}"); return (null, null); }
                var capParsed = parsed;
                resolvers[i] = _ => capParsed;
            }
            else // ComponentField
            {
                var capTarget = targetObject;
                var compMember = (MemberInfo)targetType.GetField(ps.componentMember, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                                 ?? targetType.GetProperty(ps.componentMember, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (compMember == null)
                {
                    compMember = (MemberInfo)typeof(GameObject).GetField(ps.componentMember, BindingFlags.Public | BindingFlags.Instance)
                                 ?? typeof(GameObject).GetProperty(ps.componentMember, BindingFlags.Public | BindingFlags.Instance);
                    if (compMember != null && capTarget is Component c3) capTarget = c3.gameObject;
                }
                if (compMember == null) { Debug.LogWarning($"[SmartBinding] Member '{ps.componentMember}' not found"); return (null, null); }
                var capMem = compMember; var capT = capTarget;
                resolvers[i] = _ => capMem is FieldInfo fi ? fi.GetValue(capT) : ((PropertyInfo)capMem).GetValue(capT);
            }
        }

        var capturedTarget = targetObject;
        var capturedMethod = method;
        var capturedResolvers = resolvers;
        var compiledConditions = conditions
            .Where(c => c.enabled)
            .Select(c => c.Compile(eventType, targetObject))
            .Where(fn => fn != null)
            .ToList();

        Func<object, bool> checkFn = evtObj =>
            compiledConditions.Count == 0 || compiledConditions.All(fn => fn(evtObj));

        Action<object> executeFn = evtObj => {
            var args = new object[capturedResolvers.Length];
            for (int i = 0; i < capturedResolvers.Length; i++)
                args[i] = capturedResolvers[i](evtObj);
            capturedMethod.Invoke(capturedTarget, args);
        };

        return (checkFn, executeFn);
    }

    public Action<object> Compile(Type eventType)
    {
        var (check, execute) = CompileSplit(eventType);
        if (check == null || execute == null) return null;
        return evtObj => { if (check(evtObj)) execute(evtObj); };
    }
}

// =========================================================
// CONFIG SCRIPTABLE OBJECT
// =========================================================
#if UNITY_EDITOR

[CreateAssetMenu(fileName = "EventBusListenerConfig", menuName = "EventBus/Listener Config")]
public class EventBusListenerConfig : ScriptableObject
{
    [Tooltip("Path relative to Assets/ where your event scripts live.\nExample: Scripts/Events")]
    public string eventsFolder = "Scripts/Events";

    private static EventBusListenerConfig _instance;
    public static EventBusListenerConfig Instance
    {
        get
        {
            if (_instance != null) return _instance;
            _instance = Resources.Load<EventBusListenerConfig>("EventBusListenerConfig");
            if (_instance != null) return _instance;
            var guids = AssetDatabase.FindAssets("t:EventBusListenerConfig");
            if (guids.Length > 0)
                _instance = AssetDatabase.LoadAssetAtPath<EventBusListenerConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            return _instance ?? (_instance = CreateAndSaveDefault());
        }
    }

    private static EventBusListenerConfig CreateAndSaveDefault()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        var asset = CreateInstance<EventBusListenerConfig>();
        AssetDatabase.CreateAsset(asset, "Assets/Resources/EventBusListenerConfig.asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return asset;
    }

    public static void Invalidate() => _instance = null;
}

[CustomEditor(typeof(EventBusListenerConfig))]
public class EventBusListenerConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var config = (EventBusListenerConfig)target;
        var folderProp = serializedObject.FindProperty("eventsFolder");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("EventBus Listener Config", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(folderProp.stringValue);
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string startPath = Path.Combine(Application.dataPath, folderProp.stringValue);
                if (!Directory.Exists(startPath)) startPath = Application.dataPath;
                string abs = EditorUtility.OpenFolderPanel("Select Events Folder", startPath, "");
                if (!string.IsNullOrEmpty(abs) && abs.StartsWith(Application.dataPath))
                {
                    folderProp.stringValue = abs.Substring(Application.dataPath.Length).TrimStart('/', '\\');
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssets();
                    EventBusListenerEditor.InvalidateCache();
                }
            }
        }
        EditorGUILayout.LabelField($"  Assets/{folderProp.stringValue}", EditorStyles.miniLabel);
        serializedObject.ApplyModifiedProperties();
    }
}

// =========================================================
// CUSTOM EDITOR
// =========================================================
[CustomEditor(typeof(EventBusListener))]
public class EventBusListenerEditor : Editor
{
    private static string _cachedFolder;
    private static string[] _cachedNames = Array.Empty<string>();
    private static double _lastScan = -1;
    private const double COOLDOWN = 5.0;
    private readonly Dictionary<int, MethodInfo[]> _methodCache = new();

    public static void InvalidateCache()
    {
        _cachedFolder = null;
        _cachedNames = Array.Empty<string>();
        _lastScan = -1;
    }

    // Unity base types to exclude from component member pickers
    private static readonly HashSet<Type> _unityBaseTypes = new()
    {
        typeof(UnityEngine.Object), typeof(Component),
        typeof(Behaviour), typeof(MonoBehaviour), typeof(Transform),
    };

    private static readonly string[] _goProps = { "activeSelf", "activeInHierarchy", "name", "tag" };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var listener = (EventBusListener)target;
        var selectedProp  = serializedObject.FindProperty("selectedEventTypeName");
        var raisedProp    = serializedObject.FindProperty("onRaised");
        var releasedProp  = serializedObject.FindProperty("onReleased");
        var bothProp      = serializedObject.FindProperty("callOnBothStates");
        var bindingsProp  = serializedObject.FindProperty("smartBindings");

        if (bindingsProp == null)
        {
            EditorGUILayout.HelpBox("Could not find 'smartBindings'. Recompile and reselect.", MessageType.Error);
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("EventBus Listener", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        var config = EventBusListenerConfig.Instance;
        string folder = config?.eventsFolder ?? "Scripts/Events";
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Config:", EditorStyles.miniLabel, GUILayout.Width(44));
            if (config != null && GUILayout.Button("EventBusListenerConfig", EditorStyles.linkLabel))
            { EditorGUIUtility.PingObject(config); Selection.activeObject = config; }
        }
        EditorGUILayout.Space(6);

        if (_cachedFolder != folder || (_cachedNames.Length == 0 && EditorApplication.timeSinceStartup - _lastScan > COOLDOWN))
            RefreshCache(folder);

        EditorGUILayout.LabelField("Listen To", EditorStyles.miniBoldLabel);
        if (_cachedNames.Length == 0)
            EditorGUILayout.HelpBox($"No OnXxxEvent classes found in Assets/{folder}", MessageType.Warning);
        else
        {
            int cur = Mathf.Max(0, Array.IndexOf(_cachedNames, selectedProp.stringValue));
            int next = EditorGUILayout.Popup(cur, _cachedNames);
            if (next != cur || string.IsNullOrEmpty(selectedProp.stringValue))
            {
                selectedProp.stringValue = _cachedNames[next];
                bindingsProp.ClearArray();
                _methodCache.Clear();
            }
        }
        EditorGUILayout.LabelField($"  {_cachedNames.Length} event(s) in Assets/{folder}", EditorStyles.miniLabel);
        EditorGUILayout.Space(8);

        string evtName = selectedProp.stringValue;
        Type evtType = EventBusListener.ResolveType(evtName);
        FieldInfo[] evtFields = evtType != null
            ? evtType.GetFields(BindingFlags.Public | BindingFlags.Instance)
            : Array.Empty<FieldInfo>();
        bool isButtonEvent = evtType != null && EventBusListener.GetBoolMember(evtType) != null;

        // ---- SMART BINDINGS ----
        EditorGUILayout.LabelField("Method Bindings", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Select a component and a method. Each parameter maps to an event field or fixed value.",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(4);

        for (int i = 0; i < bindingsProp.arraySize; i++)
        {
            var bp = bindingsProp.GetArrayElementAtIndex(i);
            if (bp == null) continue;
            var targetProp = bp.FindPropertyRelative("targetObject");
            var methodProp = bp.FindPropertyRelative("methodName");
            var fieldsProp = bp.FindPropertyRelative("paramSources");

            if (targetProp == null || methodProp == null || fieldsProp == null)
            {
                EditorGUILayout.HelpBox($"Binding {i+1} has invalid data — remove and re-add.", MessageType.Warning);
                if (GUILayout.Button("Remove")) { bindingsProp.DeleteArrayElementAtIndex(i); break; }
                continue;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header
            var logicProp = bp.FindPropertyRelative("logic");
            var logicVal  = logicProp != null ? (BindingLogic)logicProp.enumValueIndex : BindingLogic.If;
            Color hdrColor = logicVal switch {
                BindingLogic.If     => new Color(0.4f, 0.8f, 1f),
                BindingLogic.ElseIf => new Color(1f, 0.8f, 0.4f),
                BindingLogic.Else   => new Color(0.8f, 0.6f, 1f),
                _                   => Color.white
            };
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.color = hdrColor;
                if (logicProp != null)
                    EditorGUILayout.PropertyField(logicProp, GUIContent.none, GUILayout.Width(64));
                GUI.color = Color.white;
                EditorGUILayout.LabelField($"Binding {i + 1}", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("X", GUILayout.Width(22)))
                { bindingsProp.DeleteArrayElementAtIndex(i); _methodCache.Remove(i); break; }
            }

            bool showCondition = logicVal != BindingLogic.Else;

            // ── GameObject + Component picker (estilo UnityEvent) ─────────────────
            {
                var currentTarget = targetProp.objectReferenceValue;
                GameObject currentGO = currentTarget is Component c0 ? c0.gameObject : currentTarget as GameObject;

                // Fila 1: GameObject
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("GameObject", GUILayout.Width(80));
                EditorGUI.BeginChangeCheck();
                // Acepta tanto GameObject como Component directo
                var droppedObj = EditorGUILayout.ObjectField(currentGO, typeof(UnityEngine.Object), true);
                if (EditorGUI.EndChangeCheck())
                {
                    GameObject newGO = droppedObj as GameObject
                                    ?? (droppedObj as Component)?.gameObject;
                    Component droppedComp = droppedObj as Component;

                    if (newGO != currentGO || droppedComp != null)
                    {
                        if (newGO == null)
                        {
                            targetProp.objectReferenceValue = null;
                        }
                        else if (droppedComp != null)
                        {
                            // Arrastraron un componente directamente → usarlo tal cual
                            targetProp.objectReferenceValue = droppedComp;
                        }
                        else
                        {
                            // Arrastraron un GameObject → auto-select primer componente significativo
                            var comps = newGO.GetComponents<Component>();
                            var first = comps.FirstOrDefault(c => !(c is Transform)) ?? comps.FirstOrDefault();
                            targetProp.objectReferenceValue = first;
                        }
                        methodProp.stringValue = "";
                        fieldsProp.ClearArray();
                        _methodCache.Remove(i);
                    }
                }
                EditorGUILayout.EndHorizontal();

                // Fila 2: Component picker (solo si hay un GO asignado)
                if (currentGO != null)
                {
                    var allComps = currentGO.GetComponents<Component>();
                    string[] compNames = allComps.Select(c => c.GetType().Name).ToArray();
                    // Manejo de duplicados (ej: dos BoxCollider)
                    var seen = new Dictionary<string, int>();
                    string[] compLabels = allComps.Select(c => {
                        string n = c.GetType().Name;
                        if (!seen.ContainsKey(n)) { seen[n] = 0; return n; }
                        seen[n]++; return $"{n} [{seen[n]}]";
                    }).ToArray();

                    int curIdx = currentTarget is Component curComp
                        ? Mathf.Max(0, Array.IndexOf(allComps, curComp))
                        : 0;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Component", GUILayout.Width(80));
                    EditorGUI.BeginChangeCheck();
                    int newIdx = EditorGUILayout.Popup(curIdx, compLabels);
                    if (EditorGUI.EndChangeCheck())
                    {
                        targetProp.objectReferenceValue = allComps[newIdx];
                        methodProp.stringValue = "";
                        fieldsProp.ClearArray();
                        _methodCache.Remove(i);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            // Method picker
            var targetObj = targetProp.objectReferenceValue;
            if (targetObj != null)
            {
                if (!_methodCache.TryGetValue(i, out var methods))
                    _methodCache[i] = methods = GetEligibleMethods(targetObj.GetType(), evtFields, evtType);

                if (methods.Length == 0)
                    EditorGUILayout.HelpBox("No compatible methods found.", MessageType.Info);
                else
                {
                    string[] methodLabels = methods.Select(m => {
                        // Property setter → mostrar como "propName (type)"
                        if (m.IsSpecialName && m.Name.StartsWith("set_"))
                        {
                            var p = m.GetParameters()[0];
                            return $"{m.Name.Substring(4)} = ({p.ParameterType.Name})";
                        }
                        var ps = m.GetParameters();
                        string pStr = ps.Length == 0 ? "()" : "(" + string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}")) + ")";
                        return $"{m.Name}{pStr}";
                    }).ToArray();

                    int curMethod = Mathf.Max(0, Array.FindIndex(methods, m =>
                        m.Name == methodProp.stringValue &&
                        m.GetParameters().Length == (fieldsProp != null ? fieldsProp.arraySize : 0)));

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
                            var fp = fieldsProp.GetArrayElementAtIndex(p);
                            var modeSP = fp?.FindPropertyRelative("mode");
                            if (modeSP == null) continue;
                            // Auto-mode: whole event if param type matches event type
                            if (evtType != null && ps[p].ParameterType.IsAssignableFrom(evtType))
                            {
                                modeSP.enumValueIndex = (int)ParamSourceMode.WholeEvent;
                            }
                            else
                            {
                                modeSP.enumValueIndex = 0; // EventField default
                                var match = evtFields.FirstOrDefault(f => f.FieldType == ps[p].ParameterType && !IsAlreadyUsedPS(fieldsProp, f.Name, p));
                                fp.FindPropertyRelative("eventFieldName").stringValue = match?.Name ?? "";
                            }
                            fp.FindPropertyRelative("fixedValue").stringValue = "";
                            fp.FindPropertyRelative("componentMember").stringValue = "";
                        }
                        serializedObject.ApplyModifiedProperties();
                    }
                    EditorGUILayout.EndHorizontal();

                    // Per-parameter mapping
                    if (!string.IsNullOrEmpty(methodProp.stringValue))
                    {
                        var selMethod = methods[nextMethod];
                        var mps = selMethod.GetParameters();
                        for (int p = 0; p < mps.Length; p++)
                        {
                            while (fieldsProp.arraySize <= p)
                            {
                                fieldsProp.InsertArrayElementAtIndex(fieldsProp.arraySize);
                                serializedObject.ApplyModifiedProperties();
                            }
                            var fp = fieldsProp.GetArrayElementAtIndex(p);
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
                            var pType = mps[p].ParameterType;
                            EditorGUILayout.BeginVertical(GUI.skin.box);

                            var srcMode = (ParamSourceMode)modeProp.enumValueIndex;

                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUI.color = new Color(0.7f, 1f, 0.8f);
                                EditorGUILayout.LabelField($"{pType.Name} {mps[p].Name}", EditorStyles.miniBoldLabel, GUILayout.Width(140));
                                GUI.color = Color.white;
                                if (srcMode == ParamSourceMode.WholeEvent)
                                {
                                    // Locked — no dropdown, no extra config
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
                            if (srcMode == ParamSourceMode.WholeEvent)
                            {
                                // Nothing to configure — event object passed directly
                            }
                            else if (srcMode == ParamSourceMode.EventField)
                            {
                                var compat = evtFields.Where(f => IsCompatible(f.FieldType, pType)).ToArray();
                                if (compat.Length == 0) EditorGUILayout.HelpBox($"No event fields of type {pType.Name}", MessageType.Warning);
                                else
                                {
                                    string[] cL = compat.Select(f => $"{f.Name} ({f.FieldType.Name})").ToArray();
                                    string[] cN = compat.Select(f => f.Name).ToArray();
                                    int cur2 = Mathf.Max(0, Array.IndexOf(cN, evtFNProp.stringValue));
                                    using (new EditorGUILayout.HorizontalScope())
                                    {
                                        EditorGUILayout.LabelField("Event field", GUILayout.Width(80));
                                        evtFNProp.stringValue = cN[EditorGUILayout.Popup(cur2, cL)];
                                    }
                                }
                            }
                            else if (srcMode == ParamSourceMode.FixedValue)
                            {
                                fixedProp.stringValue = DrawTypedField("Value", fixedProp.stringValue, pType);
                            }
                            else // ComponentField
                            {
                                var tObj2 = bp.FindPropertyRelative("targetObject").objectReferenceValue;
                                if (tObj2 == null) EditorGUILayout.HelpBox("Assign component first.", MessageType.Warning);
                                else
                                {
                                    var compMems = GetComponentMembers(tObj2, pType)
                                        .Concat(GetGameObjectMembers().Where(m => { Type mt = m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType; return IsCompatible(mt, pType); }))
                                        .ToArray();
                                    if (compMems.Length == 0) EditorGUILayout.HelpBox($"No compatible members of type {pType.Name}.", MessageType.Warning);
                                    else
                                    {
                                        string[] cN = compMems.Select(m => m.Name).ToArray();
                                        string[] cL = compMems.Select(m => { Type mt = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType; string owner = m.DeclaringType == typeof(GameObject) ? "GO" : tObj2.GetType().Name; return $"{owner}.{m.Name} ({mt.Name})"; }).ToArray();
                                        int cur2 = Mathf.Max(0, Array.IndexOf(cN, compProp.stringValue));
                                        using (new EditorGUILayout.HorizontalScope()) { EditorGUILayout.LabelField("Member", GUILayout.Width(80)); compProp.stringValue = cN[EditorGUILayout.Popup(cur2, cL)]; }
                                    }
                                }
                            }
                            EditorGUILayout.EndVertical();
                        }
                    }
                }
            }

            // ---- CONDITIONS ----
            EditorGUILayout.Space(4);
            var condsProp = bp.FindPropertyRelative("conditions");

            if (!showCondition)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ToggleLeft("Condition (always fires — Else branch)", false, EditorStyles.miniBoldLabel);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Conditions (AND)", EditorStyles.miniBoldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("+ Add", GUILayout.Width(50)))
                    {
                        condsProp.InsertArrayElementAtIndex(condsProp.arraySize);
                        serializedObject.ApplyModifiedProperties();
                        var newCond = condsProp.GetArrayElementAtIndex(condsProp.arraySize - 1);
                        var enabledP = newCond?.FindPropertyRelative("enabled");
                        if (enabledP != null) enabledP.boolValue = true;
                    }
                }

                for (int ci = 0; ci < condsProp.arraySize; ci++)
                {
                    var condProp      = condsProp.GetArrayElementAtIndex(ci);
                    if (condProp == null) continue;
                    var condEnabled   = condProp.FindPropertyRelative("enabled");
                    var condField     = condProp.FindPropertyRelative("fieldName");
                    var condOp        = condProp.FindPropertyRelative("op");
                    var condSource    = condProp.FindPropertyRelative("source");
                    var condValue     = condProp.FindPropertyRelative("compareValue");
                    var condCompField = condProp.FindPropertyRelative("componentFieldName");
                    if (condEnabled == null) continue;

                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    bool removeThis = false;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        condEnabled.boolValue = EditorGUILayout.ToggleLeft($"Condition {ci + 1}", condEnabled.boolValue, EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("−", GUILayout.Width(20))) removeThis = true;
                    }

                    if (condEnabled.boolValue && evtFields.Length > 0)
                    {
                        EditorGUILayout.BeginVertical(GUI.skin.box);

                        string[] condFieldNames  = evtFields.Select(f => f.Name).ToArray();
                        string[] condFieldLabels = evtFields.Select(f => $"{f.Name} ({f.FieldType.Name})").ToArray();
                        int condFieldIdx = Mathf.Max(0, Array.IndexOf(condFieldNames, condField.stringValue));

                        var condSourceVal = (ConditionSource)condSource.enumValueIndex;

                        if (condSourceVal != ConditionSource.ComponentVsLiteral)
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField("if", GUILayout.Width(18));
                                int newIdx = EditorGUILayout.Popup(condFieldIdx, condFieldLabels, GUILayout.MinWidth(100));
                                condField.stringValue = condFieldNames[newIdx];
                                var selField = evtFields[newIdx];
                                bool isNum = IsNumericType(selField.FieldType);
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
                        var targetObj2 = bp.FindPropertyRelative("targetObject").objectReferenceValue;

                        if (condSourceVal == ConditionSource.EventField)
                        {
                            if (selectedCondField != null)
                                condValue.stringValue = DrawTypedField("Value", condValue.stringValue, selectedCondField.FieldType);
                            GUI.color = new Color(1f, 0.9f, 0.5f);
                            EditorGUILayout.LabelField($"  if ({condField.stringValue} {OpSymbol((ConditionOperator)condOp.enumValueIndex)} {condValue.stringValue})", EditorStyles.miniLabel);
                            GUI.color = Color.white;
                        }
                        else if (condSourceVal == ConditionSource.ComponentField)
                        {
                            if (targetObj2 == null) EditorGUILayout.HelpBox("Assign Component above first.", MessageType.Warning);
                            else
                            {
                                var cMems = GetComponentMembers(targetObj2, selectedCondField?.FieldType);
                                if (cMems.Length == 0) EditorGUILayout.HelpBox("No compatible fields.", MessageType.Info);
                                else
                                {
                                    string[] cN = cMems.Select(m => m.Name).ToArray();
                                    string[] cL = cMems.Select(m => { Type mt = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType; return $"{m.Name} ({mt.Name})"; }).ToArray();
                                    int curC = Mathf.Max(0, Array.IndexOf(cN, condCompField.stringValue));
                                    using (new EditorGUILayout.HorizontalScope()) { EditorGUILayout.LabelField("Component field", GUILayout.Width(110)); condCompField.stringValue = cN[EditorGUILayout.Popup(curC, cL)]; }
                                    GUI.color = new Color(1f, 0.9f, 0.5f);
                                    EditorGUILayout.LabelField($"  if ({condField.stringValue} {OpSymbol((ConditionOperator)condOp.enumValueIndex)} {targetObj2.GetType().Name}.{condCompField.stringValue})", EditorStyles.miniLabel);
                                    GUI.color = Color.white;
                                }
                            }
                        }
                        else // ComponentVsLiteral
                        {
                            if (targetObj2 == null) EditorGUILayout.HelpBox("Assign Component above first.", MessageType.Warning);
                            else
                            {
                                var goMems   = GetGameObjectMembers();
                                var compMems2 = GetComponentMembers(targetObj2, null);
                                var allMems  = compMems2.Concat(goMems).ToArray();
                                if (allMems.Length == 0) EditorGUILayout.HelpBox("No fields found.", MessageType.Info);
                                else
                                {
                                    string[] allN = allMems.Select(m => m.Name).ToArray();
                                    string[] allL = allMems.Select(m => { Type mt = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType; string owner = m.DeclaringType == typeof(GameObject) ? "GameObject" : targetObj2.GetType().Name; return $"{owner}.{m.Name} ({mt.Name})"; }).ToArray();
                                    int curC2 = Mathf.Max(0, Array.IndexOf(allN, condCompField.stringValue));
                                    using (new EditorGUILayout.HorizontalScope())
                                    {
                                        EditorGUILayout.LabelField("Field", GUILayout.Width(40));
                                        int newC2 = EditorGUILayout.Popup(curC2, allL);
                                        condCompField.stringValue = allN[newC2];
                                        var selMem = allMems[newC2];
                                        Type selType = selMem is FieldInfo sf ? sf.FieldType : ((PropertyInfo)selMem).PropertyType;
                                        bool numSel = IsNumericType(selType);
                                        if (!numSel && (ConditionOperator)condOp.enumValueIndex > ConditionOperator.NotEquals)
                                            condOp.enumValueIndex = 0;
                                        EditorGUILayout.PropertyField(condOp, GUIContent.none, GUILayout.Width(numSel ? 100 : 80));
                                    }
                                    var selMem2 = allMems[Mathf.Max(0, Array.IndexOf(allN, condCompField.stringValue))];
                                    Type selFieldType = selMem2 is FieldInfo sf2 ? sf2.FieldType : ((PropertyInfo)selMem2).PropertyType;
                                    condValue.stringValue = DrawTypedField("Value", condValue.stringValue, selFieldType);
                                    string own2 = selMem2.DeclaringType == typeof(GameObject) ? "GameObject" : targetObj2.GetType().Name;
                                    GUI.color = new Color(1f, 0.9f, 0.5f);
                                    EditorGUILayout.LabelField($"  if ({own2}.{condCompField.stringValue} {OpSymbol((ConditionOperator)condOp.enumValueIndex)} {condValue.stringValue})", EditorStyles.miniLabel);
                                    GUI.color = Color.white;
                                }
                            }
                        }

                        EditorGUILayout.EndVertical(); // inner condition box
                    }

                    EditorGUILayout.EndVertical(); // outer condition box
                    if (removeThis) { condsProp.DeleteArrayElementAtIndex(ci); break; }
                }
            }

            EditorGUILayout.EndVertical(); // binding box
            EditorGUILayout.Space(2);
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

        // ---- CLASSIC CALLBACKS ----
        EditorGUILayout.LabelField("Classic Callbacks (no params)", EditorStyles.boldLabel);
        if (isButtonEvent)
        {
            EditorGUILayout.PropertyField(bothProp, new GUIContent("Call On Both States"));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(raisedProp, new GUIContent(bothProp.boolValue ? "On Event (always)" : "On Pressed"));
            if (!bothProp.boolValue)
                EditorGUILayout.PropertyField(releasedProp, new GUIContent("On Released"));
        }
        else
            EditorGUILayout.PropertyField(raisedProp, new GUIContent("On Raised"));

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

    // ── Typed field drawer ───────────────────────────────────────────────────
    private static string DrawTypedField(string label, string current, Type t)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (!string.IsNullOrEmpty(label))
                EditorGUILayout.LabelField(label, GUILayout.Width(80));

            if (t == typeof(bool))
            {
                string[] opts = { "true", "false" };
                int cur = current == "false" ? 1 : 0;
                return opts[EditorGUILayout.Popup(cur, opts)];
            }
            if (t.IsEnum)
            {
                string[] vals = Enum.GetNames(t);
                int cur = Mathf.Max(0, Array.IndexOf(vals, current));
                return vals[EditorGUILayout.Popup(cur, vals)];
            }
            if (t == typeof(int))
            {
                int.TryParse(current, out int v);
                return EditorGUILayout.IntField(v).ToString();
            }
            if (t == typeof(float))
            {
                float.TryParse(current, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v);
                return EditorGUILayout.FloatField(v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (t == typeof(double))
            {
                double.TryParse(current, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v);
                return EditorGUILayout.DoubleField(v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (t == typeof(long))
            {
                long.TryParse(current, out long v);
                return EditorGUILayout.LongField(v).ToString();
            }
            if (t == typeof(Vector2))
            {
                var f = current?.Split(',') ?? Array.Empty<string>();
                float.TryParse(f.Length>0?f[0]:"0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
                float.TryParse(f.Length>1?f[1]:"0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
                var v = EditorGUILayout.Vector2Field("", new Vector2(x, y));
                return $"{v.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{v.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            }
            if (t == typeof(Vector3))
            {
                var f = current?.Split(',') ?? Array.Empty<string>();
                float.TryParse(f.Length>0?f[0]:"0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
                float.TryParse(f.Length>1?f[1]:"0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
                float.TryParse(f.Length>2?f[2]:"0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z);
                var v = EditorGUILayout.Vector3Field("", new Vector3(x, y, z));
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                return $"{v.x.ToString(ic)},{v.y.ToString(ic)},{v.z.ToString(ic)}";
            }
            if (t == typeof(Vector4) || t == typeof(Quaternion))
            {
                var f = current?.Split(',') ?? Array.Empty<string>();
                float.TryParse(f.Length>0?f[0]:"0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
                float.TryParse(f.Length>1?f[1]:"0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
                float.TryParse(f.Length>2?f[2]:"0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z);
                float.TryParse(f.Length>3?f[3]:"0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w);
                var v = EditorGUILayout.Vector4Field("", new Vector4(x, y, z, w));
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                return $"{v.x.ToString(ic)},{v.y.ToString(ic)},{v.z.ToString(ic)},{v.w.ToString(ic)}";
            }
            if (t == typeof(Vector2Int))
            {
                var f = current?.Split(',') ?? Array.Empty<string>();
                int.TryParse(f.Length>0?f[0]:"0", out int x); int.TryParse(f.Length>1?f[1]:"0", out int y);
                var v = EditorGUILayout.Vector2IntField("", new Vector2Int(x, y));
                return $"{v.x},{v.y}";
            }
            if (t == typeof(Vector3Int))
            {
                var f = current?.Split(',') ?? Array.Empty<string>();
                int.TryParse(f.Length>0?f[0]:"0", out int x); int.TryParse(f.Length>1?f[1]:"0", out int y); int.TryParse(f.Length>2?f[2]:"0", out int z);
                var v = EditorGUILayout.Vector3IntField("", new Vector3Int(x, y, z));
                return $"{v.x},{v.y},{v.z}";
            }
            if (t == typeof(Color) || t == typeof(Color32))
            {
                var f = current?.Split(',') ?? Array.Empty<string>();
                float.TryParse(f.Length>0?f[0]:"1", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r);
                float.TryParse(f.Length>1?f[1]:"1", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float g);
                float.TryParse(f.Length>2?f[2]:"1", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float b);
                float.TryParse(f.Length>3?f[3]:"1", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float a);
                var v = EditorGUILayout.ColorField(new Color(r, g, b, a));
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                return $"{v.r.ToString(ic)},{v.g.ToString(ic)},{v.b.ToString(ic)},{v.a.ToString(ic)}";
            }
            // Fallback: text field
            return EditorGUILayout.TextField(current ?? "");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private static MethodInfo[] GetEligibleMethods(Type type, FieldInfo[] eventFields, Type evtType = null)
    {
        // Métodos void normales — param compatible con event field O con el tipo del evento completo
        var regular = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.ReturnType == typeof(void))
            .Where(m => {
                var ps = m.GetParameters();
                if (ps.Length == 0) return true;
                return ps.All(p =>
                    (evtType != null && p.ParameterType.IsAssignableFrom(evtType)) ||  // whole event param
                    eventFields.Any(f => IsCompatible(f.FieldType, p.ParameterType)));  // field param
            });

        // Setters de propiedades compatibles con un event field
        var propSetters = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetSetMethod(false) != null)
            .Where(p => eventFields.Length == 0 || eventFields.Any(f => IsCompatible(f.FieldType, p.PropertyType)))
            .Select(p => p.GetSetMethod(false));

        return regular.Concat(propSetters)
            .OrderBy(m => m.GetParameters().Length == 0 ? 1 : 0)
            .ThenBy(m => m.Name)
            .ToArray();
    }

    private static bool IsCompatible(Type fieldType, Type paramType) =>
        paramType.IsAssignableFrom(fieldType) || fieldType == paramType;

    private static bool IsAlreadyUsedPS(SerializedProperty paramSources, string evtFieldName, int upTo)
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
        _lastScan = EditorApplication.timeSinceStartup;
        string fullPath = Path.Combine(Application.dataPath, folder);
        if (!Directory.Exists(fullPath)) { _cachedNames = Array.Empty<string>(); return; }
        var regex = new Regex(@"public\s+class\s+(On\w+Event)\b", RegexOptions.Compiled);
        var names = new List<string>();
        foreach (string file in Directory.GetFiles(fullPath, "*.cs", SearchOption.AllDirectories))
            foreach (Match m in regex.Matches(File.ReadAllText(file)))
                names.Add(m.Groups[1].Value);
        _cachedNames = names.Distinct().OrderBy(n => n).ToArray();
    }

    private static bool IsNumericType(Type t) =>
        t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long);

    private static MemberInfo[] GetComponentMembers(UnityEngine.Object targetObj, Type filterType)
    {
        return targetObj.GetType()
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !_unityBaseTypes.Contains(f.DeclaringType))
            .Cast<MemberInfo>()
            .Concat(targetObj.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && !_unityBaseTypes.Contains(p.DeclaringType)))
            .Where(m => {
                Type mt = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType;
                if (filterType == null) return true;
                return mt == filterType || (IsNumericType(mt) && IsNumericType(filterType));
            })
            .ToArray();
    }

    private static MemberInfo[] GetGameObjectMembers()
    {
        return typeof(GameObject)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && _goProps.Contains(p.Name))
            .Cast<MemberInfo>()
            .ToArray();
    }

    private static string OpSymbol(ConditionOperator op) => op switch {
        ConditionOperator.Equals       => "==",
        ConditionOperator.NotEquals    => "!=",
        ConditionOperator.GreaterThan  => ">",
        ConditionOperator.LessThan     => "<",
        ConditionOperator.GreaterOrEqual => ">=",
        ConditionOperator.LessOrEqual  => "<=",
        _ => "?"
    };
}

public class EventBusListenerConfigWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(deleted).Concat(moved).Any(p => p.Contains("EventBusListenerConfig")))
        {
            EventBusListenerConfig.Invalidate();
            EventBusListenerEditor.InvalidateCache();
        }
    }
}

#endif
