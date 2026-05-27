/// <summary>
/// Raised when a quickslot (1 or 2) is assigned a new item — including null to
/// clear it. Subscribers (HUD, etc.) refresh their visuals on this event.
/// </summary>
public class OnQuickslotAssignedEvent
{
    public int slotIndex; // 1 or 2
    public SOItem item;   // null = empty quickslot
}
