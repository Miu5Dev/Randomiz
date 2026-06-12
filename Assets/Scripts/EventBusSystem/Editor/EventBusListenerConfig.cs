#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// =========================================================
// CONFIG SCRIPTABLE OBJECT
// Stores the events folder path used by the custom editor.
// Place one instance in Assets/Resources/ (auto-created).
// =========================================================
/// <summary>ScriptableObject storing the events-folder path used by the EventBus inspector tooling; auto-created in Resources and accessed via <see cref="Instance"/>.</summary>
[CreateAssetMenu(fileName = "EventBusListenerConfig", menuName = "EventBus/Listener Config")]
public class EventBusListenerConfig : ScriptableObject
{
    [Tooltip("Path relative to Assets/ where your event scripts live.\nExample: Scripts/Events")]
    public string eventsFolder = "Scripts/Events";

    // ── Singleton accessor ─────────────────────────────────────────────────
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
                _instance = AssetDatabase.LoadAssetAtPath<EventBusListenerConfig>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
            return _instance ?? (_instance = CreateAndSaveDefault());
        }
    }

    public static void Invalidate() => _instance = null;

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
}

// ── Custom inspector ───────────────────────────────────────────────────────
[CustomEditor(typeof(EventBusListenerConfig))]
public class EventBusListenerConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var config     = (EventBusListenerConfig)target;
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

        EditorGUILayout.LabelField($" Assets/{folderProp.stringValue}", EditorStyles.miniLabel);
        serializedObject.ApplyModifiedProperties();
    }
}

// ── Asset post-processor: auto-refresh when config changes ─────────────────
public class EventBusListenerConfigWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (System.Linq.Enumerable.Concat(
                System.Linq.Enumerable.Concat(imported, deleted), moved)
            .Any(p => p.Contains("EventBusListenerConfig")))
        {
            EventBusListenerConfig.Invalidate();
            EventBusListenerEditor.InvalidateCache();
        }
    }
}
#endif
