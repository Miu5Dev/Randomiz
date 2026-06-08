/// <summary>
/// Raised when a boss encounter ends — because the boss was defeated, or because the
/// encounter was otherwise concluded. HUD elements can subscribe to hide
/// encounter-specific UI (boss bar, prompts).
/// </summary>
public class OnBossEncounterEndedEvent
{
    /// <summary>Unique id of the boss whose encounter ended.</summary>
    public string bossId;

    /// <summary>True when the encounter ended because the boss was defeated.</summary>
    public bool defeated;
}
