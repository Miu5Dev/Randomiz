// Coloca este archivo en Assets/Editor/
using UnityEngine;
using UnityEditor;

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

        // Aviso si locationId está vacío
        if (string.IsNullOrEmpty(chest.locationId))
            EditorGUILayout.HelpBox("⚠ locationId vacío — el cofre no se registrará en el state.", MessageType.Warning);

        EditorGUILayout.Space(4);

        // ── Pool
        EditorGUILayout.PropertyField(serializedObject.FindProperty("pool"));
        EditorGUILayout.Space(4);

        // ── Required items con drop area
        EditorGUILayout.LabelField("🔑  Items Requeridos para Acceder", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("requiredItems"), includeChildren: true);
        DrawDropArea(chest);
        EditorGUILayout.Space(6);

        // ── Estado actual (solo en Play Mode)
        if (Application.isPlaying && chest.GetComponent<SOItemPool>() == null)
        {
            EditorGUILayout.LabelField("🎲  Estado en Runtime", EditorStyles.boldLabel);
            var pool = serializedObject.FindProperty("pool").objectReferenceValue as SOItemPool;
            if (pool?.state != null)
            {
                var s = pool.state.GetChest(chest.locationId);
                if (s != null)
                {
                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = s.opened
                        ? new Color(0.6f, 0.6f, 0.6f)   // gris = abierto
                        : !string.IsNullOrEmpty(s.itemName)
                            ? new Color(0.5f, 1f, 0.5f)  // verde = tiene item
                            : new Color(1f, 0.85f, 0.4f);// naranja = vacío

                    string stateLabel = s.opened          ? "✓ Abierto"
                                      : string.IsNullOrEmpty(s.itemName) ? "⏳ Sin asignar"
                                      : $"🎁 {s.itemName}";

                    var item = pool.FindItem(s.itemName);
                    if (item is SOWeapon w) stateLabel += $" [Tier {w.tier}]";

                    EditorGUILayout.HelpBox(stateLabel, MessageType.None);
                    GUI.backgroundColor = prevBg;
                }
                else
                    EditorGUILayout.HelpBox("No registrado en el state.", MessageType.Warning);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDropArea(ChestBehaviour chest)
    {
        var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
        GUI.Box(rect, "⬇  Arrastra SOItems aquí para añadir como requeridos", EditorStyles.helpBox);

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
