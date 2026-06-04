using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds a live Scene-view preview of a sword's hitbox so you can see exactly
/// where and how big it is while tuning <c>hitboxReach</c> / <c>hitboxSize</c>.
/// Select the SOSword asset, then pick (or just select in the Hierarchy) a scene
/// Transform to project the box from.
/// </summary>
[CustomEditor(typeof(SOSword))]
public class SOSwordEditor : Editor
{
    private Transform _previewTarget;

    private void OnEnable()  => SceneView.duringSceneGui += OnScene;
    private void OnDisable() => SceneView.duringSceneGui -= OnScene;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Hitbox Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Pick a scene object (or just select one in the Hierarchy) to draw the " +
            "weapon hitbox in the Scene view.", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        _previewTarget = (Transform)EditorGUILayout.ObjectField(
            "Preview From", _previewTarget, typeof(Transform), true);
        if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
    }

    private void OnScene(SceneView view)
    {
        var sword = (SOSword)target;
        if (sword == null) return;

        Transform origin = _previewTarget != null ? _previewTarget : Selection.activeTransform;
        if (origin == null) return;

        if (!string.IsNullOrEmpty(sword.hitboxOriginName))
        {
            Transform named = origin.Find(sword.hitboxOriginName);
            if (named != null) origin = named;
        }

        Vector3 center = origin.position + origin.forward * sword.hitboxReach;

        Handles.matrix = Matrix4x4.TRS(center, origin.rotation, Vector3.one);
        Handles.color  = new Color(1f, 0.2f, 0.2f, 0.9f);
        Handles.DrawWireCube(Vector3.zero, sword.hitboxSize);
        Handles.color  = new Color(1f, 0.2f, 0.2f, 0.15f);
        Handles.DrawWireCube(Vector3.zero, sword.hitboxSize * 0.99f);
        Handles.matrix = Matrix4x4.identity;

        Handles.Label(center, $"{sword.itemName} hitbox");
    }
}
