// Place this file under any Editor/ folder so it isn't included in player builds.
using UnityEngine;
using UnityEditor;

/// <summary>Custom inspector for ChestBehaviour: location id, item pool, required items (with drag-and-drop) and live runtime state in Play mode.</summary>
[CustomEditor(typeof(ChestBehaviour))]
public class ChestBehaviourEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var chest = (ChestBehaviour)target;

        // ── ID
        EditorGUILayout.LabelField("📦  Chest", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("locationId"));

        // Warn if locationId is empty.
        if (string.IsNullOrEmpty(chest.locationId))
            EditorGUILayout.HelpBox("⚠ locationId is empty — this chest won't be registered in the state.", MessageType.Warning);

        EditorGUILayout.Space(4);

        // ── Pool
        EditorGUILayout.PropertyField(serializedObject.FindProperty("pool"));
        EditorGUILayout.Space(4);

        // ── Required items + drop area
        EditorGUILayout.LabelField("🔑  Items required to access", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("requiredItems"), includeChildren: true);
        DrawDropArea(chest);
        EditorGUILayout.Space(6);

        // ── Live state (only in Play mode)
        var poolRef = serializedObject.FindProperty("pool").objectReferenceValue as SOItemPool;
        if (Application.isPlaying && poolRef != null)
        {
            EditorGUILayout.LabelField("🎲  Runtime state", EditorStyles.boldLabel);
            if (poolRef.state != null)
            {
                var s = poolRef.state.GetChest(chest.locationId);
                if (s != null)
                {
                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = s.opened
                        ? new Color(0.6f, 0.6f, 0.6f)
                        : !string.IsNullOrEmpty(s.itemName)
                            ? new Color(0.5f, 1f, 0.5f)
                            : new Color(1f, 0.85f, 0.4f);

                    string stateLabel = s.opened                   ? "✓ Opened"
                                       : string.IsNullOrEmpty(s.itemName) ? "⏳ Unassigned"
                                                                          : $"🎁 {s.itemName}";

                    var item = poolRef.FindItem(s.itemName);
                    if (item is SOWeapon w) stateLabel += $" [Tier {w.tier}]";

                    EditorGUILayout.HelpBox(stateLabel, MessageType.None);
                    GUI.backgroundColor = prevBg;
                }
                else
                    EditorGUILayout.HelpBox("Not registered in the state.", MessageType.Warning);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDropArea(ChestBehaviour chest)
    {
        var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
        GUI.Box(rect, "⬇  Drag SOItems here to add them as required", EditorStyles.helpBox);

        var e = Event.current;
        if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
            || !rect.Contains(e.mousePosition)) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is SOItem item && !chest.requiredItems.Contains(item))
                {
                    Undo.RecordObject(chest, "Add Required Item");
                    chest.requiredItems.Add(item);
                    EditorUtility.SetDirty(chest);
                }
            }
        }
        e.Use();
    }
}
