using UnityEngine;

/// <summary>
/// Player inventory. Fixed-size array of <see cref="SOItem"/> slots:
///   - Slot 0 is reserved for the equipped sword (tier-based replacement).
///   - Slots 1..N hold other items (potions max 2 full + 1 empty bottle, weapons by type, etc.)
///
/// Self-subscribes to OnItemPickedUpEvent (for logging) and OnPotionConsumeEvent
/// (to swap consumed potions for the empty bottle). No inspector wiring needed.
///
/// Pickups are typically routed to <see cref="AddItem"/> from outside (e.g.
/// ChestBehaviour) — the picked-up event itself is informational.
/// </summary>
public class InventoryHandler : MonoBehaviour
{
    public static InventoryHandler Instance { get; private set; }

    [SerializeField] private SOItem[] invItems = new SOItem[13];
    [SerializeField] public SOItem defaultBottle; // Empty-bottle reference (shared with EquipHandler)

    [SerializeField] public int Coins = 0;

    public SOItem[] InvItems => invItems;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Subscribe<OnPotionConsumeEvent>(OnPotionConsume);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Unsubscribe<OnPotionConsumeEvent>(OnPotionConsume);
    }

    public SOItem GetItem(int index) => invItems[index];

    // ─── Event handlers ────────────────────────────────────────────────────

    private void OnItemPickedUp(OnItemPickedUpEvent e)
    {
        if (e.receiver != gameObject) return;
        // Logging / VFX hook. Actual storage is done by AddItem from the pickup source.
        if (e.item != null)
            Debug.Log($"[Inventory] {e.receiver.name} picked up {e.item.name}");
    }

    private void OnPotionConsume(OnPotionConsumeEvent e)
    {
        ConsumeItem(e.consumedPotionItem, e.emptyPotionItem);
    }

    // ─── Add / consume API ─────────────────────────────────────────────────

    /// <summary>
    /// Add an item to the inventory using type-specific rules. Returns the added item
    /// reference on success, or null if the item couldn't be added (no space / lower tier).
    /// </summary>
    public SOItem AddItem(SOItem item)
    {
        if (item == null) return null;

        // ─ Potions ─────────────────────────────────────────────────────────
        if (item is SOPotion)
        {
            // Prefer replacing an empty bottle slot first.
            int emptyIndex = FindDefaultBottleIndex();
            if (emptyIndex != -1)
            {
                invItems[emptyIndex] = item;
                Debug.Log($"[Inventory] Empty bottle replaced by {item.name} at slot {emptyIndex}");
                return item;
            }

            // Otherwise, allow up to 2 full potions before refusing.
            int fullPotions = CountFullPotions();
            if (fullPotions >= 2)
            {
                Debug.Log($"[Inventory] Already {fullPotions} full potions — refusing {item.name}");
                return null;
            }

            int freeSlot = GetFirstEmptySlot();
            if (freeSlot != -1)
            {
                invItems[freeSlot] = item;
                Debug.Log($"[Inventory] Added potion {item.name} at slot {freeSlot}");
                return item;
            }

            Debug.LogWarning("[Inventory] No room for potion.");
            return null;
        }

        // ─ Sword (always slot 0, tier replacement) ─────────────────────────
        if (item is SOSword newSword)
        {
            if (invItems[0] is SOSword existingSword)
            {
                if (newSword.tier > existingSword.tier)
                {
                    invItems[0] = newSword;
                    return newSword;
                }
                return null;
            }
            invItems[0] = newSword;
            return newSword;
        }

        // ─ Other weapons (tier replacement by exact type) ──────────────────
        if (item is SOWeapon newWeapon)
        {
            for (int i = 1; i < invItems.Length; i++)
            {
                if (invItems[i] != null && invItems[i].GetType() == newWeapon.GetType())
                {
                    if (newWeapon.tier > ((SOWeapon)invItems[i]).tier)
                    {
                        invItems[i] = newWeapon;
                        return newWeapon;
                    }
                    return null;
                }
            }
            // Fall through to generic add below if no matching weapon type.
        }

        // ─ Currency ────────────────────────────────────────────────────────
        if (item is SOMoney money)
        {
            Coins += money.MoneyAmmount;
            return money;
        }

        // ─ Generic items ───────────────────────────────────────────────────
        int firstEmpty = GetFirstEmptySlot();
        if (firstEmpty != -1)
        {
            invItems[firstEmpty] = item;
            return item;
        }

        Debug.LogWarning("[Inventory] Inventory full, cannot add item.");
        return null;
    }

    private void ConsumeItem(SOItem item, SOItem consumedItem)
    {
        int index = GetIndex(item);
        if (index != -1)
            invItems[index] = consumedItem;
    }

    private int GetIndex(SOItem item)
    {
        for (int i = 0; i < invItems.Length; i++)
            if (invItems[i] == item) return i;
        return -1;
    }

    /// <summary>
    /// Returns the highest tier among all weapons currently in the inventory
    /// (slot 0 sword takes precedence, then searches the rest).
    /// </summary>
    public int GetHighestWeaponTier()
    {
        if (invItems[0] is SOWeapon sword)
            return sword.tier;

        int max = 0;
        for (int i = 1; i < invItems.Length; i++)
            if (invItems[i] is SOWeapon w && w.tier > max)
                max = w.tier;
        return max;
    }

    // ─── Potion helpers ────────────────────────────────────────────────────

    private int FindDefaultBottleIndex()
    {
        for (int i = 1; i < invItems.Length; i++)
            if (invItems[i] != null && invItems[i] == defaultBottle)
                return i;
        return -1;
    }

    private int GetFirstEmptySlot()
    {
        for (int i = 1; i < invItems.Length; i++)
            if (invItems[i] == null) return i;
        return -1;
    }

    private int CountFullPotions()
    {
        int count = 0;
        for (int i = 1; i < invItems.Length; i++)
            if (invItems[i] is SOPotion && invItems[i] != defaultBottle)
                count++;
        return count;
    }
}
