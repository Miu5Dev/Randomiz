/// <summary>
/// Toggles the player movement subsystem on or off.
/// PlayerMovement honors this — when disabled, it zeroes move/dash input and refuses
/// to process new input events until re-enabled. Use this for cutscenes, dialog,
/// menus or any context where the player should freeze in place.
/// Note: this is independent from the InventoryWheel input cancellation; either
/// mechanism can pause the player.
/// </summary>
public class OnSetMovementEnabledEvent
{
    public bool enabled;
}
