using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared logic for turning an in-memory <see cref="SOEnemy"/> (with hide-flagged
/// sub-objects) into a saved asset + wired prefab. Used by both the Enemy Creator
/// window and the one-click enemy presets.
/// </summary>
public static class EnemyAssetBuilder
{
    public struct Result { public SOEnemy enemy; public GameObject prefab; public string assetPath; public string prefabPath; }

    public static Result Build(SOEnemy enemy, string saveFolder)
    {
        string folder = $"{saveFolder}/{enemy.enemyName}";
        if (!AssetDatabase.IsValidFolder(folder))
            CreateFoldersRecursive(folder);

        string assetPath  = $"{folder}/{enemy.enemyName}_Data.asset";
        string prefabPath = $"{folder}/{enemy.enemyName}.prefab";

        // 1) Main asset.
        enemy.hideFlags = HideFlags.None;
        AssetDatabase.CreateAsset(enemy, assetPath);

        // 2) Embed every in-memory SO referenced anywhere as a sub-asset.
        PersistSubAssets(enemy, enemy, new HashSet<ScriptableObject>());

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(assetPath);

        // 3) Prefab.
        GameObject prefab = BuildPrefab(enemy, prefabPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return new Result { enemy = enemy, prefab = prefab, assetPath = assetPath, prefabPath = prefabPath };
    }

    /// <summary>
    /// Recursively embeds in-memory ScriptableObjects (no asset path yet) as
    /// sub-assets of <paramref name="root"/>. Existing shared assets (weapons,
    /// reused pattern assets) are left as plain references.
    /// </summary>
    private static void PersistSubAssets(ScriptableObject so, Object root, HashSet<ScriptableObject> visited)
    {
        if (so == null || !visited.Add(so)) return;

        var sob = new SerializedObject(so);
        var it  = sob.GetIterator();
        while (it.NextVisible(true))
        {
            if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
            if (it.objectReferenceValue is ScriptableObject child)
            {
                bool isExternalAsset = !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(child));
                if (isExternalAsset) continue;           // shared asset → keep as reference

                child.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(child, root);
                PersistSubAssets(child, root, visited);
            }
        }
    }

    private static GameObject BuildPrefab(SOEnemy enemy, string prefabPath)
    {
        GameObject root = new GameObject(enemy.enemyName);
        try
        {
            if (enemy.parts.Count > 1)
            {
                root.AddComponent<BossGroup>();
                for (int i = 0; i < enemy.parts.Count; i++)
                {
                    var child = new GameObject(enemy.parts[i].partName);
                    child.transform.SetParent(root.transform);
                    ConfigurePart(child, enemy, i);
                }
            }
            else
            {
                ConfigurePart(root, enemy, 0);
            }

            return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void ConfigurePart(GameObject go, SOEnemy enemy, int partIndex)
    {
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.height = 2f;
        capsule.radius = 0.5f;
        capsule.center = new Vector3(0f, 1f, 0f);

        go.AddComponent<PhysicsController>();
        go.AddComponent<HealthSystem>();
        go.AddComponent<HitFlash>();    // red blink on damage

        var controller = go.AddComponent<EnemyController>();

        var so = new SerializedObject(controller);
        so.FindProperty("data").objectReferenceValue = enemy;
        so.FindProperty("partIndex").intValue = partIndex;
        so.ApplyModifiedProperties();
    }

    public static void CreateFoldersRecursive(string folder)
    {
        string[] parts = folder.Split('/');
        string acc = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{acc}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(acc, parts[i]);
            acc = next;
        }
    }
}
