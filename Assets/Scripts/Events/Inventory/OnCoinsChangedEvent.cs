/// <summary>
/// Raised whenever the player's coin total changes (pickup, purchase, restore).
/// The persistent coin HUD subscribes to this to update its label without polling.
/// </summary>
public class OnCoinsChangedEvent
{
    public int coins;
}
