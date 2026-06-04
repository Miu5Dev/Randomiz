// OutlineNormals.cs
// Automatic, global smooth-normal baker for the toon outline. On load it scans
// every renderer in the scene, finds those using a toon-outline shader
// (Custom/CelShading or Custom/PlayerProximity) and bakes averaged smooth normals
// into UV3 of their mesh. Works on ANY mesh - Unity primitives, imported models,
// procedural - because it runs at runtime, not through the import pipeline.
//
// Why it's needed: the inverted-hull outline extrudes each vertex along its
// normal. Meshes split vertices at UV / hard-edge seams (same position, different
// normals), so the hull tears open there -> gaps / wedges. Averaging the normals
// of co-located vertices and storing them in UV3 (which the outline reads) closes
// every gap.
//
// SETUP: none. It auto-runs before the first scene loads. To exclude a shader,
// edit s_OutlineShaderNames below.

using System.Collections.Generic;
using UnityEngine;

public static class OutlineNormals
{
    private static readonly string[] s_OutlineShaderNames =
    {
        "Custom/CelShading",
        "Custom/PlayerProximity",
    };

    // Meshes already processed this session (shared meshes are baked once and
    // cached, so 100 identical cubes share one baked mesh).
    private static readonly Dictionary<Mesh, Mesh> s_Baked = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Run()
    {
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var r in renderers)
            Process(r);
    }

    private static void Process(Renderer r)
    {
        if (!UsesOutlineShader(r)) return;

        // MeshFilter (static meshes)
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
            mf.sharedMesh = GetBaked(mf.sharedMesh);

        // SkinnedMeshRenderer
        if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            smr.sharedMesh = GetBaked(smr.sharedMesh);
    }

    private static bool UsesOutlineShader(Renderer r)
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null || mats[i].shader == null) continue;
            string name = mats[i].shader.name;
            for (int s = 0; s < s_OutlineShaderNames.Length; s++)
                if (name == s_OutlineShaderNames[s]) return true;
        }
        return false;
    }

    // Returns a clone of 'src' with smooth normals baked into UV3 (cached per mesh).
    private static Mesh GetBaked(Mesh src)
    {
        if (s_Baked.TryGetValue(src, out var cached)) return cached;

        Mesh clone = Object.Instantiate(src);
        clone.name = src.name + " (OutlineNormals)";
        BakeSmoothNormalsToUV3(clone);
        s_Baked[src] = clone;
        return clone;
    }

    private static void BakeSmoothNormalsToUV3(Mesh mesh)
    {
        Vector3[] verts   = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (verts == null || normals == null || verts.Length == 0 || normals.Length != verts.Length)
            return;

        int n = verts.Length;
        var groups = new Dictionary<Vector3Int, List<int>>(n);
        for (int i = 0; i < n; i++)
        {
            Vector3Int key = Snap(verts[i]);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<int>(4);
                groups[key] = list;
            }
            list.Add(i);
        }

        var smooth = new List<Vector3>(n);
        for (int i = 0; i < n; i++) smooth.Add(normals[i]);

        foreach (var kv in groups)
        {
            Vector3 sum = Vector3.zero;
            foreach (int idx in kv.Value) sum += normals[idx];
            Vector3 avg = sum.sqrMagnitude > 1e-8f ? sum.normalized : normals[kv.Value[0]];
            foreach (int idx in kv.Value) smooth[idx] = avg;
        }

        mesh.SetUVs(3, smooth);
    }

    private static Vector3Int Snap(Vector3 p)
    {
        const float g = 10000f; // 0.0001 unit grid merges float drift between verts
        return new Vector3Int(
            Mathf.RoundToInt(p.x * g),
            Mathf.RoundToInt(p.y * g),
            Mathf.RoundToInt(p.z * g));
    }
}
