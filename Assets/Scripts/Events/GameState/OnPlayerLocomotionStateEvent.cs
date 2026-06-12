/// <summary>
/// Raised by PlayerMovement whenever a tracked locomotion flag transitions
/// (wallhug, ledge-grab, ledge-climb, near-wall, dashing, grounded). Lets
/// UI / AI / VFX react to state changes without polling PlayerMovement every frame.
///
/// Only emitted on actual transitions — if every flag is stable between
/// frames, no event fires.
/// </summary>
public class OnPlayerLocomotionStateEvent
{
    public bool isWallhugging;
    public bool isLedgeGrabbing;
    public bool isClimbingLedge;
    public bool nearWall;
    public bool isGrounded;
    public bool isDashing;
    public bool isTargeting;
}
