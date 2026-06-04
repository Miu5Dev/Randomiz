// AnimatorControllerBuilder.cs
// Generates the player's AnimatorController from code: parameters, a 1D locomotion
// blend tree on the base layer, jump/roll/hit states, and a masked upper-body
// action layer (attack / use item) so actions blend on top of locomotion.
//
// Tools -> Animation -> Build Player Animator Controller.
//
// It looks for the imported animation clips by name under the FBX folder. Make
// sure every animation FBX is set to Humanoid and shares the T-Pose avatar first.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class AnimatorControllerBuilder
{
    private const string OutputPath = "Assets/Resources/Animation/PlayerController.controller";
    private const string MaskPath   = "Assets/Resources/Animation/UpperBody.mask";
    private const string ClipSearchFolder = "Assets/Resources/FBX/Player";

    [MenuItem("Tools/Animation/Build Player Animator Controller")]
    public static void Build()
    {
        EnsureFolder("Assets/Resources/Animation");

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(OutputPath);

        // ── Parameters ──────────────────────────────────────────────────────
        ctrl.AddParameter(AnimParams.SpeedName,         AnimatorControllerParameterType.Float);
        ctrl.AddParameter(AnimParams.VerticalSpeedName, AnimatorControllerParameterType.Float);
        ctrl.AddParameter(AnimParams.IsGroundedName,    AnimatorControllerParameterType.Bool);
        ctrl.AddParameter(AnimParams.IsMovingName,      AnimatorControllerParameterType.Bool);
        ctrl.AddParameter(AnimParams.IsDashingName,     AnimatorControllerParameterType.Bool);
        ctrl.AddParameter(AnimParams.IsWallhuggingName, AnimatorControllerParameterType.Bool);
        ctrl.AddParameter(AnimParams.IsLedgeGrabName,   AnimatorControllerParameterType.Bool);
        ctrl.AddParameter(AnimParams.ArmedName,         AnimatorControllerParameterType.Bool);
        ctrl.AddParameter(AnimParams.AttackName,        AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter(AnimParams.UseItemName,       AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter(AnimParams.HitName,           AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter(AnimParams.LandName,          AnimatorControllerParameterType.Trigger);

        // ── Clips. Match by the FBX FILE name (robust to Mixamo's internal clip
        //    name being "mixamo.com" / "Take 001"), not the clip's own name. ─────
        var idle    = FindClipInFile("Idle");
        var walk    = FindClipInFile("Walking");
        var run     = FindClipInFile("Running");
        var roll    = FindClipInFile("Roll");
        var slash   = FindClipInFile("Slash");
        var hit     = FindClipInFile("Hit");
        var draw    = FindClipInFile("Withdrawal Sword");

        // Climb / ledge set (file names may carry an "...@Name" export prefix).
        var hangIdle  = FindClipInFile("Hanging Idle");
        var climbUp   = FindClipInFile("Climbing");
        var shimmyL   = FindClipInFile("Left Shimmy");
        var shimmyR   = FindClipInFile("Right Shimmy");

        // ── BASE LAYER: locomotion blend tree ───────────────────────────────
        var baseSm = ctrl.layers[0].stateMachine;

        var locoState = baseSm.AddState("Locomotion");
        var tree = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = AnimParams.SpeedName,
            useAutomaticThresholds = false,
        };
        AssetDatabase.AddObjectToAsset(tree, ctrl);
        if (idle != null) tree.AddChild(idle, 0f);
        if (walk != null) tree.AddChild(walk, 0.5f);
        if (run  != null) tree.AddChild(run,  1f);
        locoState.motion = tree;
        baseSm.defaultState = locoState;

        // Roll (dash) state. Playback speed driven by RollSpeed so the whole clip
        // fits inside the dash duration (PlayerAnimator computes it).
        if (roll != null)
        {
            ctrl.AddParameter(AnimParams.RollSpeedName, AnimatorControllerParameterType.Float);
            var rollState = baseSm.AddState("Roll");
            rollState.motion = roll;
            rollState.speedParameterActive = true;
            rollState.speedParameter = AnimParams.RollSpeedName;

            var toRoll = baseSm.AddAnyStateTransition(rollState);
            toRoll.AddCondition(AnimatorConditionMode.If, 0, AnimParams.IsDashingName);
            toRoll.hasExitTime = false;
            toRoll.hasFixedDuration = true;
            toRoll.duration = 0.06f;
            toRoll.canTransitionToSelf = false;

            // Exit when the clip is essentially done, not when IsDashing drops - the
            // dash is shorter than the roll, so a flag-based exit cut it mid-gesture.
            var fromRoll = rollState.AddTransition(locoState);
            fromRoll.hasExitTime = true;
            fromRoll.exitTime = 0.9f;
            fromRoll.hasFixedDuration = true;
            fromRoll.duration = 0.12f;
        }

        // Hit (full-body) state - triggered, returns automatically.
        if (hit != null)
        {
            var hitState = baseSm.AddState("Hit");
            hitState.motion = hit;
            var toHit = baseSm.AddAnyStateTransition(hitState);
            toHit.AddCondition(AnimatorConditionMode.If, 0, AnimParams.HitName);
            toHit.duration = 0.05f;
            toHit.canTransitionToSelf = false;
            var fromHit = hitState.AddTransition(locoState);
            fromHit.hasExitTime = true;
            fromHit.exitTime = 0.8f;
            fromHit.duration = 0.15f;
        }

        // ── Ledge grab: hang idle + shimmy blend, driven by IsLedgeGrabbing ─────
        // While hanging, the planar input (Speed signed by shimmy direction) is not
        // available, so we use a simple Hang state holding the Hanging Idle. Shimmy
        // L/R can be added later via a sub-blend on a dedicated "ShimmyDir" param.
        if (hangIdle != null)
        {
            ctrl.AddParameter(AnimParams.ShimmySpeedName, AnimatorControllerParameterType.Float);

            AnimatorState hangState;
            if (shimmyL != null && shimmyR != null)
            {
                // 1D blend: left shimmy (-1) -> hang idle (0) -> right shimmy (+1).
                ctrl.AddParameter(AnimParams.ShimmyDirName, AnimatorControllerParameterType.Float);
                var hangTree = new BlendTree
                {
                    name = "Hang", blendType = BlendTreeType.Simple1D,
                    blendParameter = AnimParams.ShimmyDirName, useAutomaticThresholds = false,
                };
                AssetDatabase.AddObjectToAsset(hangTree, ctrl);
                hangTree.AddChild(shimmyL, -1f);
                hangTree.AddChild(hangIdle, 0f);
                hangTree.AddChild(shimmyR,  1f);
                hangState = baseSm.AddState("Hang");
                hangState.motion = hangTree;
            }
            else
            {
                hangState = baseSm.AddState("Hang");
                hangState.motion = hangIdle;
            }

            // Sync shimmy playback to actual drag speed (PlayerAnimator drives it).
            hangState.speedParameterActive = true;
            hangState.speedParameter = AnimParams.ShimmySpeedName;

            var toHang = baseSm.AddAnyStateTransition(hangState);
            toHang.AddCondition(AnimatorConditionMode.If, 0, AnimParams.IsLedgeGrabName);
            toHang.duration = 0.15f;
            toHang.canTransitionToSelf = false;

            ctrl.AddParameter(AnimParams.IsClimbingName, AnimatorControllerParameterType.Bool);

            // Climb up: fires the instant the climb script starts (IsClimbingLedge),
            // so the body lerp and the animation start together. Near-zero blend in
            // so there's no lag before the climb pose. Returns to locomotion when
            // the script finishes (IsClimbingLedge false).
            if (climbUp != null)
            {
                var climbState = baseSm.AddState("ClimbUp");
                climbState.motion = climbUp;

                var toClimb = baseSm.AddAnyStateTransition(climbState);
                toClimb.AddCondition(AnimatorConditionMode.If, 0, AnimParams.IsClimbingName);
                toClimb.hasExitTime = false;
                toClimb.hasFixedDuration = true;
                toClimb.duration = 0.02f;       // snap in - synced with the body lerp
                toClimb.canTransitionToSelf = false;

                // Exit at the end of the clip (animation-driven, plays out fully).
                var fromClimb = climbState.AddTransition(locoState);
                fromClimb.hasExitTime = true;
                fromClimb.exitTime = 0.95f;
                fromClimb.hasFixedDuration = true;
                fromClimb.duration = 0.12f;

                // Safety exit: if the script already finished the climb, leave even
                // if exitTime hasn't hit (prevents the clip looping/holding).
                var fromClimbFlag = climbState.AddTransition(locoState);
                fromClimbFlag.AddCondition(AnimatorConditionMode.IfNot, 0, AnimParams.IsClimbingName);
                fromClimbFlag.hasExitTime = false;
                fromClimbFlag.hasFixedDuration = true;
                fromClimbFlag.duration = 0.12f;
            }

            // Leave the hang back to locomotion if we let go without climbing.
            var fromHang = hangState.AddTransition(locoState);
            fromHang.AddCondition(AnimatorConditionMode.IfNot, 0, AnimParams.IsLedgeGrabName);
            fromHang.AddCondition(AnimatorConditionMode.IfNot, 0, AnimParams.IsClimbingName);
            fromHang.duration = 0.15f;
        }

        // ── ACTION LAYER: masked upper-body (attack / draw) ─────────────────
        var mask = BuildUpperBodyMask();

        ctrl.AddLayer("Action");
        var layers = ctrl.layers;
        var actionLayer = layers[1];
        // Weight 0 by default; PlayerAnimator raises it to 1 only while an action
        // state is playing. An Override layer at weight 1 with an empty "None" state
        // would force the masked bones to the bind pose (arms up) during locomotion.
        actionLayer.defaultWeight = 0f;
        actionLayer.avatarMask = mask;
        actionLayer.blendingMode = AnimatorLayerBlendingMode.Override;
        layers[1] = actionLayer;
        ctrl.layers = layers;

        var actSm = actionLayer.stateMachine;
        var empty = actSm.AddState("None");
        actSm.defaultState = empty;

        if (slash != null)
            AddActionState(actSm, empty, "Attack", slash, AnimParams.AttackName);
        if (draw != null)
            AddActionState(actSm, empty, "UseItem", draw, AnimParams.UseItemName);

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[AnimatorControllerBuilder] Built {OutputPath}. " +
                  "Assign it to the player model's Animator (Controller field) and " +
                  "its Avatar from the T-Pose.");
        Selection.activeObject = ctrl;
        EditorGUIUtility.PingObject(ctrl);
    }

    // One-shot action state: Any -> action (instant trigger) -> back to None at the
    // end of the clip. Settings tuned to fire immediately and finish reliably.
    private static void AddActionState(AnimatorStateMachine sm, AnimatorState none,
                                       string name, Motion clip, string triggerParam)
    {
        var state = sm.AddState(name);
        state.motion = clip;

        var toState = sm.AddAnyStateTransition(state);
        toState.AddCondition(AnimatorConditionMode.If, 0, triggerParam);
        toState.hasExitTime = false;        // fire the instant the trigger is set
        toState.hasFixedDuration = true;
        toState.duration = 0.06f;           // short cross-fade in
        toState.canTransitionToSelf = false;
        // Allow a new attack to interrupt the current one (responsive combos).
        toState.interruptionSource = TransitionInterruptionSource.Destination;

        var back = state.AddTransition(none);
        back.hasExitTime = true;
        back.exitTime = 0.85f;              // most of the clip plays, then release
        back.hasFixedDuration = true;
        back.duration = 0.1f;
    }

    // ── Upper-body mask: spine, chest, arms, head; legs/root left to locomotion.
    private static AvatarMask BuildUpperBodyMask()
    {
        var mask = new AvatarMask();
        // Humanoid body part toggles.
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root,         false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body,         true);  // spine/chest
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head,         true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg,      false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg,     false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm,      true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm,     true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers,  true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK,   false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK,  false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK,   false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK,  false);

        EnsureFolder("Assets/Resources/Animation");
        AssetDatabase.CreateAsset(mask, MaskPath);
        return mask;
    }

    // Subfolders to prefer FIRST when the same file name exists in several places
    // (e.g. "walking.fbx" in both Animations/ and Basic Locomotion Pack/).
    private static readonly string[] PreferFolders =
    {
        "Assets/Resources/FBX/Player/Animations",            // root anims win
        "Assets/Resources/FBX/Player/Animations/Basic Locomotion Pack",
    };

    // ── Find the AnimationClip inside the FBX whose FILE NAME matches, preferring
    //    an EXACT name in a preferred folder. Deterministic so duplicate file names
    //    across packs don't pick the wrong (e.g. strafing) clip. ─────────────────
    private static AnimationClip FindClipInFile(string fileName)
    {
        string[] modelGuids = AssetDatabase.FindAssets($"{fileName} t:Model", new[] { ClipSearchFolder });

        AnimationClip exactPreferred = null, exactAny = null, suffixAny = null;

        foreach (var guid in modelGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string baseName = System.IO.Path.GetFileNameWithoutExtension(path);

            bool exact  = baseName.Equals(fileName, System.StringComparison.OrdinalIgnoreCase);
            bool suffix = baseName.EndsWith("@" + fileName, System.StringComparison.OrdinalIgnoreCase);
            if (!exact && !suffix) continue;

            AnimationClip clip = LoadFirstClip(path);
            if (clip == null) continue;

            string dir = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            bool preferred = System.Array.Exists(PreferFolders,
                f => dir.Equals(f, System.StringComparison.OrdinalIgnoreCase));

            if (exact && preferred) { exactPreferred = clip; break; } // best possible
            if (exact && exactAny == null) exactAny = clip;
            if (suffix && suffixAny == null) suffixAny = clip;
        }

        var chosen = exactPreferred ?? exactAny ?? suffixAny;
        if (chosen == null)
            Debug.LogWarning($"[AnimatorControllerBuilder] No AnimationClip found in an FBX named " +
                             $"'{fileName}' under {ClipSearchFolder}.");
        return chosen;
    }

    private static AnimationClip LoadFirstClip(string path)
    {
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            if (obj is AnimationClip clip && !clip.name.StartsWith("__preview"))
                return clip;
        return null;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf   = System.IO.Path.GetFileName(folder);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
