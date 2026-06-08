/// <summary>
/// Raised each time the crystal boss enters or exits its weakened (vulnerable) window.
/// The HUD can subscribe to show/hide an "attack the crystal!" prompt — this mirrors
/// <see cref="OnBossFistStunnedEvent"/> used by the fist boss.
/// </summary>
public class OnBossWeakenedEvent
{
    /// <summary>True when the boss just became weakened; false when the window ends.</summary>
    public bool weakened;
}
