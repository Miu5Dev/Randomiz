// Assets/Editor/ColliderSimplifier.cs
using UnityEditor;
using UnityEngine;
using System.IO;

public class ColliderSimplifier : EditorWindow {

    [MenuItem("Tools/Optimize & Reassign Mesh Colliders")]
    static void Optimize() {
        string folder = "Assets/GeneratedColliders";
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        foreach (var go in Selection.gameObjects) {
            foreach (var mc in go.GetComponentsInChildren<MeshCollider>()) {
                if (mc.sharedMesh == null) continue;

                // Clonar el mesh original
                Mesh optimized = Object.Instantiate(mc.sharedMesh);
                optimized.name = go.name + "_col";
                optimized.Optimize();
                optimized.RecalculateBounds();

                // Guardar como asset y REASIGNAR al collider
                string path = $"{folder}/{optimized.name}.asset";
                AssetDatabase.CreateAsset(optimized, path);
                mc.sharedMesh = optimized; // <-- esto es lo que falta en el optimizer
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Colliders optimizados y reasignados.");
    }
}