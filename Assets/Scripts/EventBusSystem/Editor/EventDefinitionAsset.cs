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
// DATA MODEL
// =========================================================

[Serializable]
public class EventParamDefinition
{
    public string paramName = "myParam";
    public EventParamType paramType = EventParamType.Float;
    // Used when paramType == Enum
    public string customEnumName = "";
}

public enum EventParamType
{
    Int,
    Float,
    Bool,
    String,
    Vector2,
    Vector3,
    Color,
    Enum
}

[Serializable]
public class CustomEnumDefinition
{
    public string enumName = "MyEnum";
    public List<string> values = new() { "ValueA", "ValueB", "ValueC" };
    [HideInInspector] public bool foldout = true;
}

// =========================================================
// SCRIPTABLE OBJECT
// =========================================================

[CreateAssetMenu(fileName = "NewEventDefinition", menuName = "EventBus/Event Definition")]
public class EventDefinitionAsset : ScriptableObject
{
    [Tooltip("Name of the generated class. Auto-prefixed with On and suffixed with Event.")]
    public string eventBaseName = "MyEvent";

    [Tooltip("Whether this is a button-style event (has a pressed bool field)")]
    public bool isButtonEvent = false;

    [Tooltip("List of parameters this event carries")]
    public List<EventParamDefinition> parameters = new();

    [Tooltip("Define new enums to generate alongside this event. Existing project enums are available via the Enum type.")]
    public List<CustomEnumDefinition> customEnums = new();

    [Tooltip("Namespace to wrap the generated code in. Leave blank for none.")]
    public string namespaceName = "";

    [HideInInspector] public string lastGeneratedPath = "";
    [HideInInspector] public string lastGeneratedCode = "";
}

// =========================================================
// PROJECT ENUM SCANNER
// Finds ALL user-defined enums via Reflection + source scan
// =========================================================
public static class ProjectEnumScanner
{
    private static string[] _cachedNames;
    private static double   _lastScan = -1;
    private const  double   COOLDOWN  = 8.0;

    private static readonly HashSet<string> _skipPrefixes = new()
    {
        "Unity", "UnityEngine", "UnityEditor", "System", "Microsoft",
        "Mono", "mscorlib", "netstandard", "nunit"
    };

    public static string[] GetAllEnumNames()
    {
        if (_cachedNames != null &&
            (EditorApplication.timeSinceStartup - _lastScan) < COOLDOWN)
            return _cachedNames;

        var names = new HashSet<string>();

        // 1) Reflection: compiled enums in user assemblies
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string asmName = asm.GetName().Name ?? "";
            if (_skipPrefixes.Any(p => asmName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                foreach (var t in asm.GetTypes())
                    if (t.IsEnum && !t.IsNested)
                        names.Add(t.Name);
            }
            catch { }
        }

        // 2) Source scan: catches Edit Mode enums not yet compiled
        var sourceRegex = new Regex(@"(?:public|internal)\s+enum\s+(\w+)", RegexOptions.Compiled);
        foreach (string file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
        {
            try
            {
                foreach (Match m in sourceRegex.Matches(File.ReadAllText(file)))
                    names.Add(m.Groups[1].Value);
            }
            catch { }
        }

        // Remove meta-enums from this system itself
        names.Remove(nameof(EventParamType));

        _cachedNames = names.OrderBy(n => n).ToArray();
        _lastScan    = EditorApplication.timeSinceStartup;
        return _cachedNames;
    }

    public static void Invalidate()
    {
        _cachedNames = null;
        _lastScan    = -1;
    }
}

// =========================================================
// CUSTOM EDITOR
// =========================================================

[CustomEditor(typeof(EventDefinitionAsset))]
public class EventDefinitionAssetEditor : Editor
{
    private bool _showPreview = false;
    private Vector2 _previewScroll;
    private bool _enumsFoldout = true;
    private bool _paramsFoldout = true;
    private readonly Dictionary<int, bool> _enumValueFoldouts = new();

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var asset = (EventDefinitionAsset)target;

        EditorGUILayout.Space(4);
        DrawHeader();
        EditorGUILayout.Space(6);
        DrawBasicSettings(asset);
        EditorGUILayout.Space(8);
        DrawNewEnums(asset);
        EditorGUILayout.Space(8);
        DrawParameters(asset);
        EditorGUILayout.Space(10);
        DrawPreviewAndGenerate(asset);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawHeader()
    {
        var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        EditorGUILayout.LabelField("Event Definition", style);
        EditorGUILayout.LabelField("Define an event and generate its C# class automatically.", EditorStyles.wordWrappedMiniLabel);
    }

    private void DrawBasicSettings(EventDefinitionAsset asset)
    {
        EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Event Name", GUILayout.Width(100));
            asset.eventBaseName = EditorGUILayout.TextField(asset.eventBaseName);
        }
        EditorGUILayout.LabelField($"  Generated class: On{asset.eventBaseName}Event", EditorStyles.miniLabel);

        EditorGUILayout.Space(4);
        asset.isButtonEvent = EditorGUILayout.Toggle("Button Event (pressed)", asset.isButtonEvent);
        if (asset.isButtonEvent)
            EditorGUILayout.LabelField("  Will include a public bool pressed field.", EditorStyles.miniLabel);

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Namespace", GUILayout.Width(100));
            asset.namespaceName = EditorGUILayout.TextField(asset.namespaceName);
        }
        if (string.IsNullOrEmpty(asset.namespaceName))
            EditorGUILayout.LabelField("  No namespace (global scope)", EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();
    }

    private void DrawNewEnums(EventDefinitionAsset asset)
    {
        _enumsFoldout = EditorGUILayout.Foldout(_enumsFoldout,
            $"New Enums to Generate ({asset.customEnums.Count})", true, EditorStyles.foldoutHeader);
        if (!_enumsFoldout) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(
            "Create new enums here — generated in the same file.\n" +
            "To use an existing project enum, pick Enum in the parameter type below.",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(4);

        for (int i = 0; i < asset.customEnums.Count; i++)
        {
            var enumDef = asset.customEnums[i];
            if (!_enumValueFoldouts.ContainsKey(i)) _enumValueFoldouts[i] = true;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            using (new EditorGUILayout.HorizontalScope())
            {
                _enumValueFoldouts[i] = EditorGUILayout.Foldout(_enumValueFoldouts[i], $"enum {enumDef.enumName}", true);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    asset.customEnums.RemoveAt(i);
                    EditorUtility.SetDirty(asset);
                    break;
                }
            }

            if (_enumValueFoldouts[i])
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Name", GUILayout.Width(60));
                    enumDef.enumName = EditorGUILayout.TextField(enumDef.enumName);
                }
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Values:", EditorStyles.miniLabel);

                for (int v = 0; v < enumDef.values.Count; v++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"  [{v}]", GUILayout.Width(36));
                        enumDef.values[v] = EditorGUILayout.TextField(enumDef.values[v]);
                        if (GUILayout.Button("-", GUILayout.Width(22)) && enumDef.values.Count > 1)
                        {
                            enumDef.values.RemoveAt(v);
                            EditorUtility.SetDirty(asset);
                            break;
                        }
                    }
                }
                if (GUILayout.Button("+ Add Value", GUILayout.Width(100)))
                    enumDef.values.Add($"Value{enumDef.values.Count}");

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (GUILayout.Button("+ Create New Enum"))
        {
            asset.customEnums.Add(new CustomEnumDefinition { enumName = $"MyEnum{asset.customEnums.Count + 1}" });
            EditorUtility.SetDirty(asset);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawParameters(EventDefinitionAsset asset)
    {
        _paramsFoldout = EditorGUILayout.Foldout(_paramsFoldout,
            $"Parameters ({asset.parameters.Count})", true, EditorStyles.foldoutHeader);
        if (!_paramsFoldout) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        if (asset.isButtonEvent)
            EditorGUILayout.LabelField("  pressed (bool) is always included automatically.", EditorStyles.miniLabel);
        EditorGUILayout.Space(2);

        // Build merged enum list: new enums first (may not be compiled yet), then project enums
        var newEnumNames  = asset.customEnums.Select(e => e.enumName).ToList();
        var projectEnums  = ProjectEnumScanner.GetAllEnumNames()
                                              .Where(n => !newEnumNames.Contains(n))
                                              .ToList();
        var allEnumNames  = newEnumNames.Concat(projectEnums).ToArray();

        for (int i = 0; i < asset.parameters.Count; i++)
        {
            var param = asset.parameters[i];

            EditorGUILayout.BeginVertical(GUI.skin.box);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Name", GUILayout.Width(40));
                param.paramName = EditorGUILayout.TextField(param.paramName, GUILayout.MinWidth(80));
                EditorGUILayout.LabelField("Type", GUILayout.Width(36));
                param.paramType = (EventParamType)EditorGUILayout.EnumPopup(param.paramType, GUILayout.Width(90));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    asset.parameters.RemoveAt(i);
                    EditorUtility.SetDirty(asset);
                    break;
                }
            }

            if (param.paramType == EventParamType.Enum)
            {
                if (allEnumNames.Length == 0)
                {
                    EditorGUILayout.HelpBox("No enums found in the project.", MessageType.Warning);
                }
                else
                {
                    int enumIdx = Mathf.Max(0, Array.IndexOf(allEnumNames, param.customEnumName));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("  Enum:", GUILayout.Width(52));
                        int newIdx = EditorGUILayout.Popup(enumIdx, allEnumNames);
                        param.customEnumName = allEnumNames[newIdx];
                    }

                    bool isNew = newEnumNames.Contains(param.customEnumName);
                    GUI.color = isNew ? new Color(1f, 0.85f, 0.4f) : new Color(0.6f, 1f, 0.7f);
                    EditorGUILayout.LabelField(
                        isNew ? "  New — will be generated in this file"
                              : "  Exists in project",
                        EditorStyles.miniLabel);
                    GUI.color = Color.white;
                }

                EditorGUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Rescan Enums", GUILayout.Width(110)))
                    {
                        ProjectEnumScanner.Invalidate();
                        Repaint();
                    }
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (GUILayout.Button("+ Add Parameter"))
        {
            asset.parameters.Add(new EventParamDefinition { paramName = $"param{asset.parameters.Count + 1}" });
            EditorUtility.SetDirty(asset);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawPreviewAndGenerate(EventDefinitionAsset asset)
    {
        string code = GenerateCode(asset);

        EditorGUILayout.LabelField("Code Generation", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        _showPreview = EditorGUILayout.Foldout(_showPreview, "Preview Generated Code", true);
        if (_showPreview)
        {
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.Height(220));
            EditorGUILayout.TextArea(code, EditorStyles.wordWrappedMiniLabel, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(4);
        if (!string.IsNullOrEmpty(asset.lastGeneratedPath))
            EditorGUILayout.LabelField($"  Last saved to: {asset.lastGeneratedPath}", EditorStyles.miniLabel);

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate & Save", GUILayout.Height(28)))
                GenerateAndSave(asset, code);

            if (!string.IsNullOrEmpty(asset.lastGeneratedPath))
            {
                if (GUILayout.Button("Ping File", GUILayout.Width(88), GUILayout.Height(28)))
                {
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(asset.lastGeneratedPath);
                    if (obj != null) { EditorGUIUtility.PingObject(obj); Selection.activeObject = obj; }
                }
            }
        }

        EditorGUILayout.EndVertical();
    }

    // =========================================================
    // CODE GENERATOR
    // =========================================================
    public static string GenerateCode(EventDefinitionAsset asset)
    {
        string className = $"On{asset.eventBaseName}Event";
        var sb = new System.Text.StringBuilder();

        bool needsUnityEngine = asset.parameters.Any(p =>
            p.paramType == EventParamType.Vector2 ||
            p.paramType == EventParamType.Vector3 ||
            p.paramType == EventParamType.Color);

        if (needsUnityEngine) sb.AppendLine("using UnityEngine;");
        sb.AppendLine();

        bool hasNs    = !string.IsNullOrWhiteSpace(asset.namespaceName);
        string indent = hasNs ? "    " : "";

        if (hasNs) { sb.AppendLine($"namespace {asset.namespaceName}"); sb.AppendLine("{"); }

        foreach (var enumDef in asset.customEnums)
        {
            sb.AppendLine($"{indent}public enum {enumDef.enumName}");
            sb.AppendLine($"{indent}{{");
            foreach (var v in enumDef.values)
                sb.AppendLine($"{indent}    {v},");
            sb.AppendLine($"{indent}}}");
            sb.AppendLine();
        }

        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// Auto-generated by EventDefinitionAsset.");
        sb.AppendLine($"{indent}/// Raise via: EventBus.Raise(new {className}() {{ ... }});");
        sb.AppendLine($"{indent}/// </summary>");
        sb.AppendLine($"{indent}public class {className}");
        sb.AppendLine($"{indent}{{");

        if (asset.isButtonEvent)
            sb.AppendLine($"{indent}    public bool pressed;");

        foreach (var param in asset.parameters)
            sb.AppendLine($"{indent}    public {GetCSharpTypeName(param)} {param.paramName};");

        sb.AppendLine($"{indent}}}");
        if (hasNs) sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetCSharpTypeName(EventParamDefinition param) => param.paramType switch
    {
        EventParamType.Int     => "int",
        EventParamType.Float   => "float",
        EventParamType.Bool    => "bool",
        EventParamType.String  => "string",
        EventParamType.Vector2 => "Vector2",
        EventParamType.Vector3 => "Vector3",
        EventParamType.Color   => "Color",
        EventParamType.Enum    => string.IsNullOrEmpty(param.customEnumName) ? "int" : param.customEnumName,
        _                      => "object"
    };

    private void GenerateAndSave(EventDefinitionAsset asset, string code)
    {
        var config       = EventBusListenerConfig.Instance;
        string folder    = config != null ? config.eventsFolder : "Scripts/Events";
        string fullFolder = Path.Combine(Application.dataPath, folder);
        string className  = $"On{asset.eventBaseName}Event";

        string savePath = EditorUtility.SaveFilePanel(
            "Save Event Script",
            Directory.Exists(fullFolder) ? fullFolder : Application.dataPath,
            $"{className}.cs", "cs");

        if (string.IsNullOrEmpty(savePath)) return;

        File.WriteAllText(savePath, code, System.Text.Encoding.UTF8);

        if (savePath.StartsWith(Application.dataPath))
        {
            string assetPath = "Assets" + savePath.Substring(Application.dataPath.Length);
            asset.lastGeneratedPath = assetPath;
            asset.lastGeneratedCode = code;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ProjectEnumScanner.Invalidate();
            EventBusListenerEditor.InvalidateCache();
            Debug.Log($"[EventDefinition] Generated: {assetPath}");
            EditorUtility.DisplayDialog("Event Generated",
                $"Saved to:\n{assetPath}\n\nRaise it:\nEventBus.Raise(new {className}() {{ ... }});", "OK");
        }
    }
}

// Invalidate enum cache whenever any .cs file changes in the project
public class ProjectEnumScannerWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(deleted).Concat(moved)
                    .Any(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            ProjectEnumScanner.Invalidate();
    }
}
#endif
