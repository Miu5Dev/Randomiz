/// <summary>
/// Raised by a <see cref="BossArea"/> when the player first enters and the boss
/// encounter begins. <see cref="BossIntroPopupUI"/> subscribes to show the centered
/// boss-name banner; other systems can react (lock doors, start music, etc.).
/// </summary>
public class OnBossEncounterStartedEvent
{
    /// <summary>Display name shown in the intro popup.</summary>
    public string bossName;

    /// <summary>Unique id that matches the BossTracker entry for this boss.</summary>
    public string bossId;
}
