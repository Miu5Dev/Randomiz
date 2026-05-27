/// <summary>
/// Raised by PlayerMovement whenever a tracked locomotion flag transitions
/// (wallhug, ledge-grab, near-wall). Lets UI / AI / VFX react to state changes
/// without polling PlayerMovement every frame.
///
/// Only emitted on actual transitions — if all three booleans are stable between
/// frames, no event fires.
/// </summary>
public class OnPlayerLocomotionStateEvent
{
    public bool isWallhugging;
    public bool isLedgeGrabbing;
    public bool nearWall;
}
