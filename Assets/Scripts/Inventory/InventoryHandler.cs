using UnityEngine;
using System.IO;

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
    [SerializeField] private SOItemPool itemPool;

    [SerializeField] public int Coins = 0;

    // ─── Save/load ─────────────────────────────────────────────────────────────

    [System.Serializable]
    private class SaveData
    {
        public string[] items;
        public int coins;
        public string equippedItemName;
        public string quickslot1Name;
        public string quickslot2Name;
    }

    private static string SavePath =>
        Path.Combine(Application.persistentDataPath, "inventory.json");

    private bool _isLoading;
    private bool _initialized;

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
        EventBus.Subscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Subscribe<OnQuickslotAssignedEvent>(OnQuickslotAssigned);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Unsubscribe<OnPotionConsumeEvent>(OnPotionConsume);
        EventBus.Unsubscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Unsubscribe<OnQuickslotAssignedEvent>(OnQuickslotAssigned);
    }

    private void Start()
    {
        if (File.Exists(SavePath)) Load();
        _initialized = true;
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
        Save();
    }

    private void OnItemEquip(OnItemEquipEvent e) => Save();
    private void OnQuickslotAssigned(OnQuickslotAssignedEvent e) => Save();

    // ─── Add / consume API ─────────────────────────────────────────────────

    /// <summary>
    /// Add an item to the inventory using type-specific rules. Returns the added item
    /// reference on success, or null if the item couldn't be added (no space / lower tier).
    /// </summary>
    public SOItem AddItem(SOItem item)
    {
        SOItem result = AddItemInternal(item);
        if (result != null) Save();
        return result;
    }

    private SOItem AddItemInternal(SOItem item)
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

    // ─── Save / load / delete ──────────────────────────────────────────────────

    public void Save()
    {
        if (_isLoading || !_initialized) return;

        var data = new SaveData
        {
            items = new string[invItems.Length],
            coins = Coins,
            equippedItemName = EquipHandler.Instance?.EquipedItem?.itemName,
            quickslot1Name   = QuickslotManager.Instance?.Slot1?.itemName,
            quickslot2Name   = QuickslotManager.Instance?.Slot2?.itemName,
        };
        for (int i = 0; i < invItems.Length; i++)
            data.items[i] = invItems[i]?.itemName;

#if UNITY_EDITOR
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
#else
        File.WriteAllText(SavePath, JsonUtility.ToJson(data));
#endif
    }

    private void Load()
    {
        _isLoading = true;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            if (data == null) return;

            // Restore inventory slots.
            for (int i = 0; i < invItems.Length && i < data.items.Length; i++)
                invItems[i] = string.IsNullOrEmpty(data.items[i])
                    ? null
                    : itemPool?.FindItem(data.items[i]);
            Coins = data.coins;

            // Restore equipped item.
            if (!string.IsNullOrEmpty(data.equippedItemName) && EquipHandler.Instance != null)
            {
                SOItem equipped = itemPool?.FindItem(data.equippedItemName);
                if (equipped != null) EquipHandler.Instance.EquipItem(equipped);
            }

            // Restore quickslot assignments.
            if (QuickslotManager.Instance != null)
            {
                if (!string.IsNullOrEmpty(data.quickslot1Name))
                {
                    SOItem s1 = itemPool?.FindItem(data.quickslot1Name);
                    if (s1 != null) QuickslotManager.Instance.AssignToSlot(1, s1);
                }
                if (!string.IsNullOrEmpty(data.quickslot2Name))
                {
                    SOItem s2 = itemPool?.FindItem(data.quickslot2Name);
                    if (s2 != null) QuickslotManager.Instance.AssignToSlot(2, s2);
                }
            }

            Debug.Log("[Inventory] Save loaded ✓");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Inventory] Load failed: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    public static void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
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
