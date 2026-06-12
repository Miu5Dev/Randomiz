using UnityEngine;

/// <summary>
/// Holds the 2 quickslot assignments (item1 / item2) and handles their input.
/// - Wheel closed: pressing item1/2 equips the assigned item.
///   Pressing it with that same item already equipped → unequips (back to sword).
/// - Wheel open: the wheel intercepts the input (cancels the event) and calls
///   AssignToSlot directly to update state here.
/// - Pressing interact with an equipped item that's not the sword (and not null) → unequip.
/// </summary>
public class QuickslotManager : MonoBehaviour
{
    public static QuickslotManager Instance { get; private set; }

    public SOItem Slot1 { get; private set; }
    public SOItem Slot2 { get; private set; }

    private bool wheelOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Duplicate component (e.g. a stray QuickslotManager on a child of the player).
            // Remove ONLY this component — destroying the host GameObject here would delete
            // the whole player when this singleton lives on the player root.
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnItemOneInputEvent>(OnItemOne);
        EventBus.Subscribe<OnItemTwoInputEvent>(OnItemTwo);
        // Priority 5: above PlayerMovement (0) so we can cancel the dash when the
        // dodge press is consumed to sheathe the sword; below Interactor (10) so
        // interacting with world objects still wins.
        EventBus.Subscribe<OnInteractDodgeInputEvent>(OnInteract, 5);
        EventBus.Subscribe<OnInventoryWheelStateEvent>(OnWheelState);
        // Negative priority so this runs AFTER InventoryHandler.OnPotionConsume,
        // which updates the inventory array in response to the same event.
        EventBus.Subscribe<OnPotionConsumeEvent>(OnPotionConsume, -10);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnItemOneInputEvent>(OnItemOne);
        EventBus.Unsubscribe<OnItemTwoInputEvent>(OnItemTwo);
        EventBus.Unsubscribe<OnInteractDodgeInputEvent>(OnInteract);
        EventBus.Unsubscribe<OnInventoryWheelStateEvent>(OnWheelState);
        EventBus.Unsubscribe<OnPotionConsumeEvent>(OnPotionConsume);
    }

    private void OnPotionConsume(OnPotionConsumeEvent e)
    {
        // At this point InventoryHandler has already swapped ONE instance of the
        // consumed potion for the empty bottle. Count remaining (matching) potions
        // in inventory and how many quickslots still reference it: replace only
        // (quickslots - remaining) of them.
        int remainingInInventory = CountInInventory(e.consumedPotionItem);
        int inQuickslots = (Slot1 == e.consumedPotionItem ? 1 : 0)
                         + (Slot2 == e.consumedPotionItem ? 1 : 0);
        int toReplace = inQuickslots - remainingInInventory;
        if (toReplace <= 0) return; // enough potions left — quickslots remain valid

        if (toReplace > 0 && Slot1 == e.consumedPotionItem)
        {
            Slot1 = e.emptyPotionItem;
            EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 1, item = Slot1 });
            toReplace--;
        }
        if (toReplace > 0 && Slot2 == e.consumedPotionItem)
        {
            Slot2 = e.emptyPotionItem;
            EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 2, item = Slot2 });
        }
    }

    private int CountInInventory(SOItem item)
    {
        if (item == null || InventoryHandler.Instance == null) return 0;
        SOItem[] inv = InventoryHandler.Instance.InvItems;
        if (inv == null) return 0;

        int count = 0;
        for (int i = 0; i < inv.Length; i++)
            if (inv[i] == item) count++;
        return count;
    }

    private void OnWheelState(OnInventoryWheelStateEvent e) => wheelOpen = e.open;

    /// <summary>
    /// Assigns an item to a quickslot (1 or 2). If the item is already in the OTHER
    /// slot AND the inventory has only one instance of it, a swap is performed
    /// (the destination slot's item moves to the source slot, avoiding impossible
    /// duplicates). If the inventory has ≥2 instances (typical for potions),
    /// duplication is allowed.
    /// Called directly from InventoryWheelUI (not via event, to avoid recursion).
    /// Emits OnQuickslotAssignedEvent for every slot that changes.
    /// </summary>
    public void AssignToSlot(int slotIndex, SOItem item)
    {
        if (slotIndex != 1 && slotIndex != 2) return;

        bool canDuplicate = HasMultipleInventoryInstances(item);

        if (slotIndex == 1)
        {
            if (item == Slot1) return; // no change

            // Swap only when we CAN'T duplicate AND the item already lives in the other slot.
            if (!canDuplicate && item != null && item == Slot2)
            {
                SOItem prevSlot1 = Slot1;
                Slot2 = prevSlot1;
                EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 2, item = Slot2 });
            }

            Slot1 = item;
            EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 1, item = Slot1 });
        }
        else // slotIndex == 2
        {
            if (item == Slot2) return;

            if (!canDuplicate && item != null && item == Slot1)
            {
                SOItem prevSlot2 = Slot2;
                Slot1 = prevSlot2;
                EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 1, item = Slot1 });
            }

            Slot2 = item;
            EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 2, item = Slot2 });
        }
    }

    /// <summary>
    /// True if the inventory holds at least 2 references to the same SOItem
    /// (e.g. two identical potions). False for null or non-duplicated items.
    /// </summary>
    private bool HasMultipleInventoryInstances(SOItem item)
    {
        if (item == null) return false;
        if (InventoryHandler.Instance == null) return false;

        SOItem[] inv = InventoryHandler.Instance.InvItems;
        if (inv == null) return false;

        int count = 0;
        for (int i = 0; i < inv.Length; i++)
        {
            if (inv[i] == item)
            {
                count++;
                if (count >= 2) return true;
            }
        }
        return false;
    }

    private void OnItemOne(OnItemOneInputEvent e)
    {
        if (wheelOpen || !e.pressed) return;
        HandleQuickslotPress(Slot1);
        // Cancel so no inspector-bound EventBusListener can re-equip on top of us
        // and undo the unequip we just performed.
        EventBus.Cancel<OnItemOneInputEvent>();
    }

    private void OnItemTwo(OnItemTwoInputEvent e)
    {
        if (wheelOpen || !e.pressed) return;
        HandleQuickslotPress(Slot2);
        EventBus.Cancel<OnItemTwoInputEvent>();
    }

    private void HandleQuickslotPress(SOItem item)
    {
        if (item == null || EquipHandler.Instance == null) return;

        if (EquipHandler.Instance.EquipedItem == item)
            EquipHandler.Instance.UnEquipItem();
        else
            EquipHandler.Instance.EquipItem(item);
    }

    private void OnInteract(OnInteractDodgeInputEvent e)
    {
        if (wheelOpen || !e.pressed) return;
        if (EquipHandler.Instance == null) return;

        SOItem equipped = EquipHandler.Instance.EquipedItem;
        if (equipped == null) return;

        // Any item: stationary + grounded → store (cancel dodge).
        //           moving → dodge normally with item still equipped.
        PlayerMovement pm = PlayerMovement.Instance;
        bool still = pm != null && pm.MoveInput.sqrMagnitude <= 0.04f && pm.IsGrounded;
        if (still)
        {
            EquipHandler.Instance.UnEquipItem();
            EventBus.Cancel<OnInteractDodgeInputEvent>();
        }
    }
}
