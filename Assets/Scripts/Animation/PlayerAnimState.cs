/// <summary>
/// The single authoritative gameplay/animation state of the player. Derived each
/// frame by PlayerStateMachine from PlayerMovement, with a strict priority order.
/// Everything else (the Animator, the IK modules, action triggers) reacts to THIS,
/// instead of reading a dozen independent flags that can disagree.
///
/// Priority (highest first) is encoded by the order here and applied in
/// PlayerStateMachine.Evaluate(): a higher state wins when several conditions are
/// true at once (e.g. you can't be "Attacking" while "Climbing").
/// </summary>
public enum PlayerAnimState
{
    // Locomotion family (lowest priority)
    Locomotion = 0,   // grounded idle/walk/run, free camera
    Targeting,        // grounded, locked-on: strafe + face target
    Airborne,         // jumping / falling

    // Traversal (override locomotion)
    StepUp,           // small auto-step (procedural hop)
    Wallhug,          // flat against a wall (procedural)
    LedgeHang,        // hanging from a ledge (procedural)
    Climb,            // climbing up from a ledge

    // Reactions / actions (highest priority)
    HitStun,          // knocked back / hurt
    Dash,             // dodge roll

    None,
}

/// <summary>
/// Which player inputs an action is allowed to start. The state machine asks the
/// current state for this before letting an attack / item-use / etc. begin, so e.g.
/// pressing attack while hanging from a ledge does nothing.
/// </summary>
public static class PlayerStateRules
{
    /// <summary>Can the player START an attack / use-item action in this state?</summary>
    public static bool CanAct(PlayerAnimState s)
    {
        switch (s)
        {
            case PlayerAnimState.Locomotion:
            case PlayerAnimState.Targeting:
                return true;          // only free, grounded states allow attacking
            default:
                return false;         // dash, airborne, wallhug, ledge, climb, hit, stepup
        }
    }

    /// <summary>Is this a procedural state (driven by IK / code, not a base clip)?</summary>
    public static bool IsProcedural(PlayerAnimState s)
        => s == PlayerAnimState.Wallhug
        || s == PlayerAnimState.LedgeHang
        || s == PlayerAnimState.StepUp;
}
