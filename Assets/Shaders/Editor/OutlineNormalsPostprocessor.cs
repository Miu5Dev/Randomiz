// OutlineNormalsPostprocessor.cs
// Automatically bakes averaged "smooth normals" into UV channel 3 (TEXCOORD3)
// of every imported model, so the toon outline pass produces a seamless
// silhouette on hard-edge / low-poly meshes WITHOUT any manual baking step.
//
// Why UV3 and not TANGENT: writing to TANGENT would clobber the data normal
// maps need. UV3 is almost always free, so this is non-destructive.
//
// The shaders (CelShading / PlayerProximity) read TEXCOORD3 for the outline
// normal and fall back to the regular NORMAL when UV3 is empty.
//
// To EXCLUDE a model (e.g. it already looks right, or you want raw normals),
// add the asset label "NoOutlineBake" in the Inspector.

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class OutlineNormalsPostprocessor : AssetPostprocessor
{
    // Bump this to force a re-import/re-bake of all models when the logic changes.
    public override uint GetVersion() => 2;

    private void OnPostprocessModel(GameObject root)
    {
        // Respect an opt-out label on the model asset.
        foreach (var label in AssetDatabase.GetLabels(assetImporter as Object))
            if (label == "NoOutlineBake") return;

        var meshes = new HashSet<Mesh>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) meshes.Add(mf.sharedMesh);
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null) meshes.Add(smr.sharedMesh);

        foreach (var mesh in meshes)
            BakeSmoothNormalsToUV3(mesh);
    }

    // Averages normals of vertices sharing a position and stores the result in UV3.
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

        // UV3 = index 3. Store xyz of the smooth normal (w unused).
        mesh.SetUVs(3, smooth);
    }

    private static Vector3Int Snap(Vector3 p)
    {
        const float g = 10000f; // 0.0001 unit grid to merge float drift
        return new Vector3Int(
            Mathf.RoundToInt(p.x * g),
            Mathf.RoundToInt(p.y * g),
            Mathf.RoundToInt(p.z * g));
    }
}
