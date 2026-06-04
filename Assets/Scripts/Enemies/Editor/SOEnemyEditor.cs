using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for an SOEnemy asset. Shows a quick read-only summary plus a button
/// that opens the full structured Enemy Editor window for in-place editing —
/// much friendlier than hand-editing the embedded sub-assets.
/// </summary>
[CustomEditor(typeof(SOEnemy))]
public class SOEnemyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var enemy = (SOEnemy)target;

        EditorGUILayout.Space(2);
        if (GUILayout.Button("✎  Open in Enemy Editor", GUILayout.Height(30)))
            EnemyCreatorWindow.OpenFor(enemy);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Name", enemy.enemyName);
        EditorGUILayout.LabelField("Parts", enemy.parts != null ? enemy.parts.Count.ToString() : "0");

        if (enemy.parts != null)
        {
            EditorGUI.indentLevel++;
            foreach (var p in enemy.parts)
            {
                if (p == null) continue;
                int phases = p.phases != null ? p.phases.Count : 0;
                EditorGUILayout.LabelField($"• {p.partName}",
                    $"{p.maxHearts}♥  ·  {phases} phase(s)  ·  {(p.weapon != null ? p.weapon.name : "no weapon")}");
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Raw Data", EditorStyles.miniBoldLabel);
        DrawDefaultInspector();
    }
}
