/// <summary>
/// Implemented by any boss a <see cref="BossArea"/> can start. Decouples the area
/// trigger from the concrete boss type, so a new boss only needs to implement this to
/// become area-spawnable. <see cref="BossArea"/> fetches it via GetComponent on the
/// activated/instantiated boss and calls <see cref="BeginEncounter"/>.
/// </summary>
public interface IBossEncounter
{
    /// <summary>Stable id recorded in <see cref="BossTracker"/> when the boss is defeated.</summary>
    string BossId { get; }

    /// <summary>Display name shown in the intro popup.</summary>
    string BossName { get; }

    /// <summary>Called once when the player enters the boss area, to start the fight.</summary>
    void BeginEncounter();
}
