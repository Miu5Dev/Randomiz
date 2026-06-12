#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Cuts the shadow-caster count, which profiling showed to be the dominant frame cost here:
/// the main thread was draw-call bound (~7.3 ms main vs a GPU that finished earlier) with ~315
/// shadow casters re-culled and re-drawn across 2 cascades. Small decorative props (lanterns,
/// fences, fountains, pillars) cast shadows nobody notices, yet each one is an extra draw in the
/// shadow pass — pure CPU waste.
///
/// Menu (Randomiz > Shadows):
///   • Report Casters In Open Scene        — read-only audit: how many renderers cast shadows and
///                                           how many fall below the size threshold.
///   • Disable Small Casters In Open Scene — sets ShadowCastingMode.Off on scene renderers below
///                                           SmallBoundsThreshold. Undoable; affects placed instances only.
///   • Disable Small Casters In Prefabs…   — same rule, but rewrites the PREFAB ASSETS under
///                                           PrefabFolder, so it also covers props spawned at runtime.
///                                           Asks for confirmation first.
///
/// Tune SmallBoundsThreshold to taste: it is the world-space diagonal of a renderer's bounds.
/// Trees and buildings stay above it and keep their shadows; small props fall below and lose them.
/// </summary>
public static class ShadowCasterOptimizer
{
    // Renderers whose bounds diagonal (world units) is below this stop casting shadows.
    private const float SmallBoundsThreshold = 2.5f;

    // Folder scanned by the prefab pass. Scoped to world props so the player/enemies are untouched.
    private const string PrefabFolder = "Assets/Resources/Prefabs/World";

    // ─────────────────────────────────────────────────────────────────────────
    // REPORT (read-only)
    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Randomiz/Shadows/Report Casters In Open Scene")]
    private static void ReportScene()
    {
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int casters = 0, small = 0;
        foreach (var r in renderers)
        {
            if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
            casters++;
            if (Diagonal(r) < SmallBoundsThreshold) small++;
        }

        float pct = casters > 0 ? 100f * small / casters : 0f;
        string msg =
            $"Renderers in scene: {renderers.Length}\n" +
            $"Casting shadows: {casters}\n" +
            $"…below {SmallBoundsThreshold} units (would be disabled): {small}\n\n" +
            $"Potential caster reduction: {pct:0}%";
        Debug.Log("[ShadowCasterOptimizer] " + msg.Replace("\n", " | "));
        EditorUtility.DisplayDialog("Shadow Casters — Report", msg, "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCENE PASS (placed instances in the open scene)
    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Randomiz/Shadows/Disable Small Casters In Open Scene")]
    private static void DisableInScene()
    {
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int changed = 0;
        foreach (var r in renderers)
        {
            if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
            if (Diagonal(r) >= SmallBoundsThreshold) continue;

            Undo.RecordObject(r, "Disable Small Shadow Casters");
            r.shadowCastingMode = ShadowCastingMode.Off;
            EditorUtility.SetDirty(r);
            changed++;
        }

        EditorUtility.DisplayDialog("Shadow Casters",
            $"Disabled shadow casting on {changed} small renderer(s) in the open scene.\n" +
            "Edit > Undo reverts it. Save the scene to persist.", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PREFAB PASS (rewrites assets — also covers props spawned at runtime)
    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Randomiz/Shadows/Disable Small Casters In Prefabs…")]
    private static void DisableInPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Shadow Casters", $"No prefabs found under {PrefabFolder}.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Shadow Casters — Prefabs",
                $"Scan {guids.Length} prefab(s) under {PrefabFolder} and disable shadow casting on " +
                $"renderers below {SmallBoundsThreshold} units?\n\nThis rewrites the prefab assets on disk.",
                "Do it", "Cancel"))
            return;

        int prefabsTouched = 0, renderersChanged = 0;
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                EditorUtility.DisplayProgressBar("Shadow Casters", path, (float)i / guids.Length);

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
                    if (Diagonal(r) >= SmallBoundsThreshold) continue;

                    r.shadowCastingMode = ShadowCastingMode.Off;
                    renderersChanged++;
                    dirty = true;
                }
                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabsTouched++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
        }

        EditorUtility.DisplayDialog("Shadow Casters",
            $"Rewrote {prefabsTouched} prefab(s); disabled casting on {renderersChanged} renderer(s).", "OK");
    }

    // World-space diagonal of a renderer's bounds — a cheap proxy for "how big is this thing".
    // Falls back to scaled local bounds when world bounds are unset (can happen in prefab-edit context).
    private static float Diagonal(Renderer r)
    {
        Vector3 size = r.bounds.size;
        if (size.sqrMagnitude < 1e-6f)
            size = Vector3.Scale(r.localBounds.size, r.transform.lossyScale);
        return size.magnitude;
    }
}
#endif
