
using UnityEngine;

/// <summary>
/// Fired when a chest is opened by the player.
/// Used by MinimapManager to track which chests are opened.
/// </summary>
public class OnChestOpenedEvent
{
    public ChestBehaviour chest;

    public OnChestOpenedEvent(ChestBehaviour chest)
    {
        this.chest = chest;
    }
}
