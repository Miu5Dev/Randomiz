using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Checks the RuntimeAnimatorController at startup for clips matching each expected
/// animation state and logs a warning for every missing one.
///
/// Attach this next to the Animator on the player model.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimationLogger : MonoBehaviour
{
    // Partial names used for case-insensitive substring matching against clip names.
    private static readonly string[] ExpectedClips =
    {
        "Idle",
        "Walk",
        "Run",
        "Jump",
        "Fall",
        "Land",
        "Roll",          // also covers Dash
        "Attack",
        "UseItem",
        "Hit",           // also covers HurtReact
        "Death",
        "Wallhug",
        "LedgeHang",
        "LedgeClimb",
        "StepUp",
        "TargetingIdle",
        "TargetingWalk",
        "SlingShot_Shoot",
        "GrappleThrow",
    };

    private void Awake()
    {
        var animator = GetComponent<Animator>();
        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("[AnimationLogger] No RuntimeAnimatorController assigned — skipping clip check.");
            return;
        }

        var clips = animator.runtimeAnimatorController.animationClips;
        var missing = new List<string>();
        int found = 0;

        foreach (string expected in ExpectedClips)
        {
            bool matched = false;
            foreach (var clip in clips)
            {
                if (clip != null &&
                    clip.name.IndexOf(expected, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matched = true;
                    break;
                }
            }

            if (matched)
            {
                found++;
            }
            else
            {
                missing.Add(expected);
                Debug.LogWarning(
                    $"[AnimationLogger] MISSING clip: '{expected}' — add a clip containing this name to the Animator Controller",
                    this);
            }
        }

        int total = ExpectedClips.Length;
        if (missing.Count > 0)
            Debug.LogWarning(
                $"[AnimationLogger] {found}/{total} animation clips found. Missing: {string.Join(", ", missing)}",
                this);
        else
            Debug.Log($"[AnimationLogger] All {total}/{total} animation clips found.", this);
    }
}
