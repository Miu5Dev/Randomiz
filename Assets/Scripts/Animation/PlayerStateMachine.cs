using System;
using UnityEngine;

/// <summary>
/// Single source of truth for the player's animation/gameplay state. Each frame it
/// derives one <see cref="PlayerAnimState"/> from PlayerMovement using a strict
/// priority order, and exposes it. The Animator driver, the IK modules and the
/// action handler all read THIS instead of polling a dozen independent flags - so
/// they can never disagree (the cause of "attack during ledge grab" and the
/// targeting rotation bug).
///
/// It does NOT move the player; PlayerMovement still owns physics. It only observes
/// and classifies, and gates which actions may start (CanAct).
///
/// Put this on the player root (next to PlayerMovement). Optional but recommended.
/// </summary>
[DefaultExecutionOrder(-50)]   // runs before PlayerAnimator so the state is fresh
public class PlayerStateMachine : MonoBehaviour
{
    public static PlayerStateMachine Instance { get; private set; }

    public PlayerAnimState Current  { get; private set; } = PlayerAnimState.Locomotion;
    public PlayerAnimState Previous { get; private set; } = PlayerAnimState.Locomotion;
    public float TimeInState { get; private set; }

    /// <summary>Fired when the state changes (old, new).</summary>
    public event Action<PlayerAnimState, PlayerAnimState> OnStateChanged;

    public bool CanAct => PlayerStateRules.CanAct(Current);

    private PlayerMovement _pm;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        _pm = GetComponent<PlayerMovement>();
        if (_pm == null) _pm = PlayerMovement.Instance;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (_pm == null) { _pm = PlayerMovement.Instance; if (_pm == null) return; }

        PlayerAnimState next = Evaluate();
        if (next != Current)
        {
            Previous = Current;
            Current = next;
            TimeInState = 0f;
            OnStateChanged?.Invoke(Previous, Current);
        }
        else
        {
            TimeInState += Time.deltaTime;
        }
    }

    /// <summary>
    /// Classify the current player condition into ONE state, highest priority first.
    /// Order matters: reactions/traversal override locomotion.
    /// </summary>
    private PlayerAnimState Evaluate()
    {
        // Reactions (top priority)
        if (_pm.IsKnockedBack)     return PlayerAnimState.HitStun;
        if (_pm.IsDashing)         return PlayerAnimState.Dash;

        // Traversal
        if (_pm.IsClimbingLedge)   return PlayerAnimState.Climb;
        if (_pm.IsLedgeGrabbing)   return PlayerAnimState.LedgeHang;
        if (_pm.IsWallhugging)     return PlayerAnimState.Wallhug;
        if (_pm.IsSteppingUp)      return PlayerAnimState.StepUp;

        // Airborne — use the real ground check, NOT velocity.y (which is the small
        // grounded gravity -2 while standing, and would falsely read as airborne and
        // block attacks). Only jumping or genuinely off the ground counts.
        if (_pm.isJumping || !_pm.IsGrounded)
            return PlayerAnimState.Airborne;

        // Grounded locomotion
        if (_pm.IsTargeting)       return PlayerAnimState.Targeting;
        return PlayerAnimState.Locomotion;
    }
}
