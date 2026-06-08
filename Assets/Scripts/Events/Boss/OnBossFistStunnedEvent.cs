/// <summary>
/// Raised each time a boss fist enters or exits its Stunned state.
/// The HUD can subscribe to show/hide the "attack the fist!" prompt.
/// </summary>
public class OnBossFistStunnedEvent
{
    /// <summary>True when the fist has just become stunned; false when stun ends.</summary>
    public bool isStunned;

    /// <summary>The fist controller that changed state.</summary>
    public BossFistController fist;
}
