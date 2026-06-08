/// <summary>
/// Raised when a boss is fully defeated (body HP reaches zero).
/// Subscribe to trigger cutscenes, unlock doors, or update the save tracker.
/// </summary>
public class OnBossDefeatedEvent
{
    /// <summary>Unique id that matches the BossTracker entry for this boss.</summary>
    public string bossId;
}
