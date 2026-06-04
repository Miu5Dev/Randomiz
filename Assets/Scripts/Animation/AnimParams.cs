using UnityEngine;

/// <summary>
/// Central registry of Animator parameter names + cached hashes. Both the runtime
/// driver (PlayerAnimator) and the controller generator (AnimatorControllerBuilder)
/// reference these, so the parameter set never drifts between them.
///
/// Use the hash fields with Animator.SetFloat/SetBool/SetTrigger - hashes are
/// faster than the string overloads.
/// </summary>
public static class AnimParams
{
    // ── Names (used by the generator to create the parameters) ─────────────
    public const string SpeedName         = "Speed";
    public const string VerticalSpeedName = "VerticalSpeed";
    public const string IsGroundedName    = "IsGrounded";
    public const string IsMovingName      = "IsMoving";
    public const string IsDashingName     = "IsDashing";
    public const string IsWallhuggingName = "IsWallhugging";
    public const string IsLedgeGrabName   = "IsLedgeGrabbing";
    public const string IsClimbingName    = "IsClimbingLedge";
    public const string ArmedName         = "Armed";
    public const string AttackName        = "Attack";
    public const string UseItemName       = "UseItem";
    public const string HitName           = "Hit";
    public const string LandName          = "Land";
    public const string ShimmyDirName     = "ShimmyDir";
    public const string ShimmySpeedName   = "ShimmySpeed";
    public const string RollSpeedName     = "RollSpeed";

    // ── Cached hashes (used at runtime) ────────────────────────────────────
    public static readonly int Speed          = Animator.StringToHash(SpeedName);
    public static readonly int VerticalSpeed  = Animator.StringToHash(VerticalSpeedName);
    public static readonly int IsGrounded     = Animator.StringToHash(IsGroundedName);
    public static readonly int IsMoving       = Animator.StringToHash(IsMovingName);
    public static readonly int IsDashing      = Animator.StringToHash(IsDashingName);
    public static readonly int IsWallhugging  = Animator.StringToHash(IsWallhuggingName);
    public static readonly int IsLedgeGrabbing= Animator.StringToHash(IsLedgeGrabName);
    public static readonly int IsClimbingLedge= Animator.StringToHash(IsClimbingName);
    public static readonly int Armed          = Animator.StringToHash(ArmedName);
    public static readonly int Attack         = Animator.StringToHash(AttackName);
    public static readonly int UseItem        = Animator.StringToHash(UseItemName);
    public static readonly int Hit            = Animator.StringToHash(HitName);
    public static readonly int Land           = Animator.StringToHash(LandName);
    public static readonly int ShimmyDir      = Animator.StringToHash(ShimmyDirName);
    public static readonly int ShimmySpeed    = Animator.StringToHash(ShimmySpeedName);
    public static readonly int RollSpeed      = Animator.StringToHash(RollSpeedName);
}
