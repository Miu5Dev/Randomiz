#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Gives trees working collisions by fitting a trunk CapsuleCollider (sized from the mesh)
/// and removing any MeshCollider.
///
/// Two entry points:
///   • Randomiz > Vegetation > Fix Terrain Tree Colliders
///       For trees PAINTED on a Terrain. Unity's terrain tree collision ("Enable Tree
///       Colliders" on the Terrain Collider) only works with a CapsuleCollider on the
///       prototype prefab — it silently ignores MeshColliders, which is why the trees
///       don't collide. This reads the selected (or every) Terrain's tree prototypes and
///       fixes each prototype prefab, then refreshes the terrain.
///   • Randomiz > Vegetation > Add Trunk Colliders to Selection
///       For trees placed as GameObjects / loose prefabs. Fixes the selected prefab(s)
///       (instances inherit it) or scene objects.
/// </summary>
public static class TreeColliderTool
{
    // Trunk radius as a fraction of the canopy's narrower horizontal extent. Tweak per taste;
    // the capsule is easy to fine-tune on the prefab afterwards.
    private const float TrunkRadiusFactor = 0.18f;
    private const float MinRadius         = 0.05f;

    [MenuItem("Randomiz/Vegetation/Add Trunk Colliders to Selection")]
    private static void AddTrunkColliders()
    {
        var objs = Selection.objects;
        if (objs == null || objs.Length == 0)
        {
            EditorUtility.DisplayDialog("Trunk Colliders",
                "Select one or more tree PREFABS (in the Project) — or tree GameObjects in the " +
                "scene — first, then run this again.", "OK");
            return;
        }

        int fixedCount = 0;
        foreach (var o in objs)
        {
            var go = o as GameObject;
            if (go == null) continue;

            // Prefer editing the prefab asset so every instance inherits the collider.
            string assetPath = AssetDatabase.GetAssetPath(go);
            if (string.IsNullOrEmpty(assetPath))
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (src != null) assetPath = AssetDatabase.GetAssetPath(src);
            }

            if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".prefab"))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
                if (ConfigureTrunkCollider(root))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                    Debug.Log($"[TreeColliderTool] Trunk collider on prefab: {assetPath}");
                    fixedCount++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            else
            {
                if (ConfigureTrunkCollider(go))
                {
                    EditorUtility.SetDirty(go);
                    Debug.Log($"[TreeColliderTool] Trunk collider on scene object: {go.name}");
                    fixedCount++;
                }
            }
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Trunk Colliders",
            fixedCount > 0
                ? $"Added/updated trunk colliders on {fixedCount} object(s).\nInstances inherit it automatically."
                : "Nothing changed — the selection had no meshes to size a collider from.",
            "OK");
    }

    [MenuItem("Randomiz/Vegetation/Fix Terrain Tree Colliders")]
    private static void FixTerrainTreeColliders()
    {
        // Selected terrains, or every Terrain in the open scenes if none is selected.
        var terrains = new List<Terrain>();
        foreach (var go in Selection.gameObjects)
        {
            var t = go.GetComponent<Terrain>();
            if (t != null) terrains.Add(t);
        }
        if (terrains.Count == 0)
            terrains.AddRange(Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None));

        if (terrains.Count == 0)
        {
            EditorUtility.DisplayDialog("Terrain Tree Colliders", "No Terrain found in the open scene(s).", "OK");
            return;
        }

        var donePrefabs = new HashSet<string>();
        int fixedProtos = 0;
        bool anyTreeColliderDisabled = false;

        foreach (var terrain in terrains)
        {
            TerrainData data = terrain.terrainData;
            if (data == null) continue;

            // Warn if tree colliders are turned off on this terrain's collider.
            var tc = terrain.GetComponent<TerrainCollider>();
            if (tc != null && !tc.enabled) anyTreeColliderDisabled = true;

            foreach (var proto in data.treePrototypes)
            {
                GameObject prefab = proto.prefab;
                if (prefab == null) continue;

                string path = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(path) || !donePrefabs.Add(path)) continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (ConfigureTrunkCollider(root))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[TreeColliderTool] Terrain tree prototype fixed (Mesh->Capsule): {path}");
                    fixedProtos++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            // Make the terrain re-read its prototypes so the new colliders take effect.
            data.RefreshPrototypes();
            EditorUtility.SetDirty(data);
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Terrain Tree Colliders",
            $"Fixed {fixedProtos} tree prototype prefab(s) across {terrains.Count} terrain(s).\n\n" +
            "MeshColliders were replaced with trunk capsules (Terrain trees only collide with " +
            "capsules)." +
            (anyTreeColliderDisabled
                ? "\n\n⚠ A Terrain Collider is disabled — enable it for tree collisions."
                : ""),
            "OK");
    }

    /// <summary>
    /// Removes MeshColliders and fits a single trunk CapsuleCollider on <paramref name="root"/>,
    /// sized from the combined bounds of every child mesh (in the root's local space).
    /// Returns false if there is no mesh to measure.
    /// </summary>
    private static bool ConfigureTrunkCollider(GameObject root)
    {
        // Size from the rendered mesh bounds — works even without a MeshFilter (LOD setups,
        // skinned meshes). localBounds is the mesh bounds in each renderer's local space.
        var renderers = root.GetComponentsInChildren<Renderer>(true);

        bool has = false;
        Bounds local = default;
        foreach (var r in renderers)
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            Bounds lb = r.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 cornerLS = lb.center + Vector3.Scale(lb.extents, Corner(i));
                Vector3 world    = r.transform.TransformPoint(cornerLS);
                Vector3 rootLS   = root.transform.InverseTransformPoint(world);
                if (!has) { local = new Bounds(rootLS, Vector3.zero); has = true; }
                else local.Encapsulate(rootLS);
            }
        }
        if (!has) return false;

        // Strip mesh colliders (heavy / unsuitable); we replace them with a trunk capsule.
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
            Object.DestroyImmediate(mc, true);

        var capsule = root.GetComponent<CapsuleCollider>();
        if (capsule == null) capsule = root.AddComponent<CapsuleCollider>();

        Vector3 size   = local.size;
        float   radius = Mathf.Max(MinRadius, Mathf.Min(size.x, size.z) * TrunkRadiusFactor);

        capsule.direction = 1;                                   // Y axis (upright trunk)
        capsule.center    = local.center;
        capsule.radius    = radius;
        capsule.height    = Mathf.Max(size.y, radius * 2f);
        capsule.isTrigger = false;
        return true;
    }

    private static Vector3 Corner(int i) => new Vector3(
        (i & 1) == 0 ? -1f : 1f,
        (i & 2) == 0 ? -1f : 1f,
        (i & 4) == 0 ? -1f : 1f);
}
#endif
