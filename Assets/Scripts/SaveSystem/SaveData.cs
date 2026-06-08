using System;
using System.Collections.Generic;

/// <summary>
/// Serializable snapshot of a single save slot. Everything needed to restore a run:
/// the randomizer seed, player transform/health, the full inventory snapshot,
/// world progress (opened chests, defeated bosses, shop purchases) and the active
/// checkpoint to spawn at.
///
/// All fields use plain serializable types (strings, ints, floats, lists of
/// serializable structs) so they round-trip cleanly through UnityEngine.JsonUtility.
/// </summary>
[Serializable]
public class SaveData
{
    // ─── Run identity ────────────────────────────────────────────────────────
    public int seed;

    // ─── Player state ────────────────────────────────────────────────────────
    public float playerPosX;
    public float playerPosY;
    public float playerPosZ;
    public float playerHealth;

    // ─── Inventory ───────────────────────────────────────────────────────────
    // One entry per inventory slot; null/empty string means an empty slot.
    public List<string> inventoryItemNames = new();
    public string equippedItemName;
    public string quickslot1Name;
    public string quickslot2Name;
    public int coins;

    // ─── World progress ──────────────────────────────────────────────────────
    public List<string> openedChestIds = new();
    public List<string> defeatedBossIds = new();
    public List<ShopPurchase> shopPurchases = new();

    // ─── Key inventory ───────────────────────────────────────────────────────
    // Key IDs the player is currently holding (not consumed yet).
    public List<string> heldKeyIds = new();

    // ─── Checkpoint ──────────────────────────────────────────────────────────
    public string checkpointId;
    public float checkpointX;
    public float checkpointY;
    public float checkpointZ;

    // ─── Metadata (for slot listing) ─────────────────────────────────────────
    public long timestampTicks;
}

/// <summary>
/// A single item purchased from a shop, identified by the shop it came from.
/// Used so a shop can hide items the player already bought after a reload.
/// </summary>
[Serializable]
public class ShopPurchase
{
    public string shopId;
    public string itemName;

    public ShopPurchase() { }

    public ShopPurchase(string shopId, string itemName)
    {
        this.shopId = shopId;
        this.itemName = itemName;
    }
}

/// <summary>
/// Lightweight metadata describing a save slot, used to populate a load/continue
/// menu without deserializing the entire <see cref="SaveData"/> for display.
/// </summary>
[Serializable]
public class SaveSlotInfo
{
    public int slotIndex;
    public bool exists;
    public long timestampTicks;
    public int seed;

    // Display-friendly "level" info. We don't have an explicit player level,
    // so we surface the highest weapon tier reached and coin count instead.
    public int highestWeaponTier;
    public int coins;

    /// <summary>Convenience accessor returning the slot's save time as DateTime.</summary>
    public DateTime Timestamp => new DateTime(timestampTicks);

    public static SaveSlotInfo Empty(int slot) => new SaveSlotInfo
    {
        slotIndex = slot,
        exists = false,
        timestampTicks = 0,
        seed = -1,
        highestWeaponTier = 0,
        coins = 0,
    };
}
