// AnimationClipImporter.cs
// Configures the import settings of the player's Mixamo animation clips so they:
//   * loop (Idle / Walking / Running),
//   * stay IN PLACE (root XZ baked into pose) - movement is driven by
//     PlayerMovement, not root motion, so the mesh must not travel on its own,
//   * don't SINK (root Y baked into pose / based on original) - removes the
//     accumulating vertical hip bob that pushed the character into the floor.
//
// Tools -> Animation -> Fix Player Animation Clips.  Re-run after re-importing.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Editor tool (Tools -> Animation -> Fix Player Animation Clips) that configures the player's Mixamo clips to loop where needed and stay in place, since movement is driven by code rather than root motion.</summary>
public static class AnimationClipImporter
{
    private const string AnimFolder = "Assets/Resources/FBX/Player/Animations";

    // Clips that should loop. Everything else (Slash, Hit, Roll, Climbing,
    // Withdrawal, Stand To Freehang, Freehang Drop) is one-shot.
    private static readonly HashSet<string> LoopingClips = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "Idle2", "Walking", "Running",
        "Hanging Idle", "Left Shimmy", "Right Shimmy",
        "left strafe walking", "right strafe walking",
    };

    // Clips whose POSITION is driven by a movement script (not the animation). Their
    // vertical root motion must be FLATTENED (not kept), otherwise the pose rises on
    // its own AND the script also moves the body -> it climbs twice / too high.
    private static readonly HashSet<string> ScriptDrivenClips = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "Climbing", "Roll",
    };

    [MenuItem("Tools/Animation/Fix Player Animation Clips")]
    public static void FixClips()
    {
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { AnimFolder });
        int fixedCount = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            // Match exact name, or the part after an exporter "...@Name" prefix.
            string shortName = fileName.Contains('@') ? fileName.Substring(fileName.IndexOf('@') + 1) : fileName;
            bool loop         = LoopingClips.Contains(fileName)      || LoopingClips.Contains(shortName);
            bool scriptDriven = ScriptDrivenClips.Contains(fileName) || ScriptDrivenClips.Contains(shortName);

            // Start from the default clip(s) Unity generated for this FBX.
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;

            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = loop;
                clips[i].loopPose = loop;
                clips[i].cycleOffset = 0f;

                clips[i].lockRootRotation   = true;   // bake rotation -> faces stay put
                clips[i].lockRootHeightY     = true;   // bake Y  -> no self-travel up/down
                clips[i].lockRootPositionXZ  = true;   // bake XZ -> in place

                clips[i].keepOriginalOrientation = true;
                clips[i].keepOriginalPositionXZ  = true;

                // Height base: normal clips keep their original Y (stable ground
                // contact, no sinking). SCRIPT-DRIVEN clips (Climbing, Roll) must NOT
                // keep the original Y - that would re-introduce the upward travel the
                // script already applies. Base their height on Feet so the pose is
                // truly flat and only the script moves the body.
                clips[i].keepOriginalPositionY = !scriptDriven;
                if (scriptDriven)
                    clips[i].heightFromFeet = true;
            }

            importer.clipAnimations = clips;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            fixedCount++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[AnimationClipImporter] Configured {fixedCount} animation FBX " +
                  $"(loop + in-place + no-sink). Looping: {string.Join(", ", LoopingClips)}.");
    }
}
