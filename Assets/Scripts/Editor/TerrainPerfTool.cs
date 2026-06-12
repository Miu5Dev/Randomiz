#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspects and optimizes how Terrain details / trees / the heightmap surface are drawn,
/// since these are common silent FPS sinks. Works on the live API (TerrainData is a binary
/// asset, so reading it as text doesn't reveal the instancing flags — this does).
///
/// Key facts this tool acts on:
///   • Detail MESHES are GPU-instanced only when DetailPrototype.useInstancing = true,
///     which requires renderMode = VertexLit. Grass / GrassBillboard details are NOT
///     instanced — they rebuild waving-grass meshes on the CPU every frame (the slow path).
///   • Terrain.drawInstanced GPU-instances the heightmap surface (fewer draw calls / less
///     CPU per tile). With 8 tiles in this project, leaving it off is a measurable cost.
///   • Terrain.treeDistance / detailObjectDistance are cull ranges — the bigger the range
///     and density, the more is drawn. (This scene ships treeDistance = 5000, very high.)
///
/// Menu (Randomiz > Terrain):
///   • Report Performance Settings  — read-only audit (answers "are my details instanced?").
///   • Instance Detail Meshes       — SAFE: enables useInstancing on detail-mesh prototypes only.
///   • Enable Surface Instancing    — RISKY: terrain.drawInstanced. Only works if the terrain
///                                    shader has the required instancing pragmas. Custom/asset-store
///                                    shaders often lack them → flickering colors. Skipped by default.
///   • Apply Recommended Distances  — clamps detail/tree draw distances to saner values.
/// </summary>
public static class TerrainPerfTool
{
    // Recommended cull distances (tweak to taste). Tree distance especially: 5000 is huge.
    private const float RecDetailDistance     = 40f;
    private const float RecTreeDistance       = 200f;
    private const float RecTreeBillboardStart = 80f;

    // ─────────────────────────────────────────────────────────────────────────
    // REPORT (read-only)
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Randomiz/Terrain/Report Performance Settings")]
    private static void Report()
    {
        var terrains = GatherTerrains();
        if (terrains.Count == 0)
        {
            EditorUtility.DisplayDialog("Terrain Performance", "No Terrain found in the open scene(s).", "OK");
            return;
        }

        var sb = new StringBuilder();
        int instancedSurface = 0, meshDetailTotal = 0, instancedMeshDetail = 0, grassDetailTotal = 0;

        foreach (var terrain in terrains)
        {
            TerrainData data = terrain.terrainData;
            if (data == null) continue;

            if (terrain.drawInstanced) instancedSurface++;

            sb.AppendLine($"■ {terrain.name}");
            sb.AppendLine($"    surface drawInstanced : {terrain.drawInstanced}");
            sb.AppendLine($"    detail distance/density: {terrain.detailObjectDistance:0} / {terrain.detailObjectDensity:0.00}");
            sb.AppendLine($"    tree distance/billboard: {terrain.treeDistance:0} / {terrain.treeBillboardDistance:0}   (instances: {data.treeInstanceCount})");

            var protos = data.detailPrototypes;
            if (protos.Length == 0)
            {
                sb.AppendLine("    details               : (none painted)");
            }
            foreach (var p in protos)
            {
                bool isMesh = p.usePrototypeMesh;
                string what = isMesh
                    ? $"mesh '{(p.prototype != null ? p.prototype.name : "—")}'"
                    : $"texture '{(p.prototypeTexture != null ? p.prototypeTexture.name : "—")}'";

                if (isMesh) { meshDetailTotal++; if (p.useInstancing) instancedMeshDetail++; }
                else grassDetailTotal++;

                sb.AppendLine($"    detail [{what}] renderMode={p.renderMode}  useInstancing={p.useInstancing}");
            }
            sb.AppendLine();
        }

        string verdict =
            $"Terrains: {terrains.Count}   (surface instanced: {instancedSurface}/{terrains.Count})\n" +
            $"Mesh details: {instancedMeshDetail}/{meshDetailTotal} GPU-instanced\n" +
            $"Grass/texture details: {grassDetailTotal} (never instanced — CPU waving-grass)\n\n" +
            (instancedSurface < terrains.Count || instancedMeshDetail < meshDetailTotal
                ? "→ Run 'Enable GPU Instancing' for the cheap wins.\n"
                : "→ Surface + mesh details already instanced.\n") +
            (grassDetailTotal > 0
                ? "→ Grass-texture details can't be instanced; lower density/distance or convert to a mesh detail.\n"
                : "");

        Debug.Log("[TerrainPerfTool] Performance audit:\n\n" + sb + "\n" + verdict);
        EditorUtility.DisplayDialog("Terrain Performance", verdict + "\nFull per-terrain breakdown logged to the Console.", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OPTIMIZE — detail mesh instancing (SAFE)
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Randomiz/Terrain/Instance Detail Meshes")]
    private static void EnableDetailInstancing()
    {
        var terrains = GatherTerrains();
        if (terrains.Count == 0)
        {
            EditorUtility.DisplayDialog("Terrain Performance", "No Terrain found in the open scene(s).", "OK");
            return;
        }

        int detailFixed = 0, grassSkipped = 0;

        foreach (var terrain in terrains)
        {
            TerrainData data = terrain.terrainData;
            if (data == null) continue;

            var protos = data.detailPrototypes;
            bool changed = false;
            for (int i = 0; i < protos.Length; i++)
            {
                var p = protos[i];
                if (!p.usePrototypeMesh) { grassSkipped++; continue; } // grass/texture can't be instanced
                if (p.useInstancing && p.renderMode == DetailRenderMode.VertexLit) continue;

                p.useInstancing = true;
                p.renderMode    = DetailRenderMode.VertexLit;
                protos[i] = p;
                changed = true;
                detailFixed++;
            }

            if (changed)
            {
                Undo.RegisterCompleteObjectUndo(data, "Instance Terrain Detail Meshes");
                data.detailPrototypes = protos;
                EditorUtility.SetDirty(data);
            }
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Terrain Performance",
            $"Mesh detail prototypes set to instanced VertexLit: {detailFixed}.\n" +
            (grassSkipped > 0
                ? $"\n{grassSkipped} grass/texture detail(s) left as-is (can't be instanced).\n" +
                  "Lower their density/distance, or paint them with a mesh prototype instead."
                : "All detail prototypes are mesh type — all instanced."),
            "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OPTIMIZE — surface instancing (RISKY — requires shader support)
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Randomiz/Terrain/Enable Surface Instancing (Advanced)")]
    private static void EnableSurfaceInstancing()
    {
        if (!EditorUtility.DisplayDialog("Terrain Performance — WARNING",
                "terrain.drawInstanced batches the heightmap patches via GPU instancing.\n\n" +
                "REQUIREMENT: your terrain shader must declare the Unity terrain instancing pragmas " +
                "(#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap forcemaxcount:1).\n\n" +
                "If your terrain uses a custom or asset-store shader without these pragmas, " +
                "enabling this will cause FLICKERING / COLOR FLASHES.\n\n" +
                "Only proceed if you are using Unity's default terrain shader (Nature/Terrain or URP Lit terrain).",
                "Enable anyway", "Cancel"))
            return;

        var terrains = GatherTerrains();
        if (terrains.Count == 0)
        {
            EditorUtility.DisplayDialog("Terrain Performance", "No Terrain found in the open scene(s).", "OK");
            return;
        }

        int surfaceFixed = 0;
        foreach (var terrain in terrains)
        {
            if (terrain.drawInstanced) continue;
            Undo.RecordObject(terrain, "Enable Terrain Surface Instancing");
            terrain.drawInstanced = true;
            EditorUtility.SetDirty(terrain);
            surfaceFixed++;
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Terrain Performance",
            surfaceFixed > 0
                ? $"Surface instancing enabled on {surfaceFixed} terrain(s).\n\n" +
                  "If you see flickering or wrong colors, press Ctrl+Z immediately to undo."
                : "All selected terrains already had surface instancing enabled.",
            "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OPTIMIZE — distances
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Randomiz/Terrain/Apply Recommended Distances")]
    private static void ApplyDistances()
    {
        var terrains = GatherTerrains();
        if (terrains.Count == 0)
        {
            EditorUtility.DisplayDialog("Terrain Performance", "No Terrain found in the open scene(s).", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Terrain Performance",
                $"Clamp draw distances on {terrains.Count} terrain(s)?\n\n" +
                $"  detail distance  → {RecDetailDistance:0}\n" +
                $"  tree distance    → {RecTreeDistance:0}\n" +
                $"  tree billboard   → {RecTreeBillboardStart:0}\n\n" +
                "Lower = fewer details/trees drawn = more FPS, at the cost of view range. " +
                "Undoable; tweak the constants in TerrainPerfTool to taste.",
                "Apply", "Cancel"))
            return;

        foreach (var terrain in terrains)
        {
            Undo.RecordObject(terrain, "Apply Terrain Distances");
            terrain.detailObjectDistance  = RecDetailDistance;
            terrain.treeDistance          = RecTreeDistance;
            terrain.treeBillboardDistance = RecTreeBillboardStart;
            EditorUtility.SetDirty(terrain);
        }

        EditorUtility.DisplayDialog("Terrain Performance",
            $"Applied recommended distances to {terrains.Count} terrain(s).", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Selected terrains, or every Terrain in the open scenes if none is selected.</summary>
    private static List<Terrain> GatherTerrains()
    {
        var terrains = new List<Terrain>();
        foreach (var go in Selection.gameObjects)
        {
            var t = go.GetComponent<Terrain>();
            if (t != null) terrains.Add(t);
        }
        if (terrains.Count == 0)
            terrains.AddRange(Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None));
        return terrains;
    }
}
#endif
