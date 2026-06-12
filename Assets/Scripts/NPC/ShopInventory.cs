using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-NPC shop stock. The shop's slots are first-class randomizer locations
/// (<c>shop_{npcId}_{i}</c>): the <see cref="RandomizerSystem"/> distributes the run's
/// items across chests AND shop slots from a single pool, so any item — a progression
/// sword included — appears in exactly one place, never duplicated. Empty slots are
/// filled with non-coin filler. This component just READS what the randomizer assigned.
///
/// Progression weapons are delivered IN ORDER: whatever tier a weapon slot advertises,
/// the player always receives the next tier they still need for that family. So the
/// player can buy ANY weapon slot and always gets the correct sequence — the stock
/// re-resolves whenever their weapon tier changes (see <see cref="EnsureGenerated"/>).
///
/// Sold items are tracked by <see cref="SaveManager"/> (slot-scoped, in SaveData),
/// NOT by a per-NPC file — so a new game resets every shop, each save slot keeps its
/// own purchases, and loading restores exactly what was bought at the save point.
///
/// Prices are the item's <see cref="SOItem.baseValue"/> scaled by the NPC's
/// personality multiplier.
///
/// Wiring requirement: <see cref="NPCData.shopItemPool"/> must be the SAME
/// <see cref="SOItemPool"/> asset the chests and RandomizerSystem use (one shared run
/// state) — otherwise the slot assignments won't resolve.
/// </summary>
[RequireComponent(typeof(NPCController))]
public class ShopInventory : MonoBehaviour
{
    /// <summary>Prefix for the randomizer location id of each shop stock slot.</summary>
    public const string LocationPrefix = "shop_";

    [SerializeField] private NPCData data;

    private readonly List<SOItem> _stock = new();
    private bool _generated;
    private int  _lastGenMaxTier = -1;
    private NPCPersonality? _resolvedPersonality;

    /// <summary>Stable id used to scope this shop's purchases in the save data.</summary>
    private string ShopId => data != null ? data.npcId : null;

    /// <summary>Items the randomizer placed in this shop (sold ones remain listed but flagged).</summary>
    public IReadOnlyList<SOItem> Stock => _stock;

    private void Awake() => ResolveData();

    /// <summary>Resolves the NPC data lazily (from the sibling controller when unassigned).</summary>
    private NPCData ResolveData()
    {
        if (data == null) data = GetComponent<NPCController>()?.Data;
        return data;
    }

    /// <summary>
    /// Appends this shop's slot location ids to <paramref name="into"/>. Called by the
    /// randomizer during generation to register shop slots as placement locations.
    /// </summary>
    public void CollectSlotLocationIds(List<string> into)
    {
        var d = ResolveData();
        if (d == null || string.IsNullOrEmpty(d.npcId)) return;

        int n = Mathf.Max(0, d.shopInventorySize);
        for (int i = 0; i < n; i++)
            into.Add(SlotId(d.npcId, i));
    }

    private static string SlotId(string npcId, int index) => $"{LocationPrefix}{npcId}_{index}";

    /// <summary>
    /// Reads the randomizer-assigned items. Re-resolves the stock whenever the player's
    /// weapon tier changes, so weapon slots always advertise the next tier the player
    /// needs. Safe to call repeatedly (sold state lives in <see cref="SaveManager"/>).
    /// </summary>
    public void EnsureGenerated()
    {
        int curMax = InventoryHandler.Instance != null
            ? InventoryHandler.Instance.GetHighestWeaponTier()
            : 0;

        if (_generated && curMax == _lastGenMaxTier) return;

        Generate();
        _generated      = true;
        _lastGenMaxTier = curMax;
    }

    /// <summary>
    /// Returns the concrete personality for this shop this run. If the NPC data is set
    /// to <see cref="NPCPersonality.RandomPerRun"/>, one of the three concrete
    /// personalities is picked deterministically from the randomizer seed and the npcId,
    /// using <see cref="System.Random"/> so the game's Unity RNG sequence is untouched.
    /// The result is cached for the lifetime of this component instance.
    /// </summary>
    public NPCPersonality ResolvedPersonality
    {
        get
        {
            if (_resolvedPersonality.HasValue) return _resolvedPersonality.Value;

            var d = ResolveData();
            if (d == null || d.personality != NPCPersonality.RandomPerRun)
            {
                _resolvedPersonality = d?.personality ?? NPCPersonality.FairMerchant;
                return _resolvedPersonality.Value;
            }

            // Derive a deterministic personality from run seed + npcId hash so the same
            // seed always produces the same merchant temperament, but different NPCs differ.
            int runSeed  = d.shopItemPool?.state?.currentSeed ?? 0;
            int combined = (runSeed * 397) ^ (!string.IsNullOrEmpty(d.npcId) ? d.npcId.GetHashCode() : 0);
            int pick     = new System.Random(combined).Next(0, 3); // 0=Fair, 1=Undervalues, 2=Overcharges
            _resolvedPersonality = (NPCPersonality)pick;
            return _resolvedPersonality.Value;
        }
    }

    private void Generate()
    {
        // Re-resolve personality so RandomPerRun reflects the current run seed.
        _resolvedPersonality = null;
        _stock.Clear();
        var d = ResolveData();
        if (d == null || d.shopItemPool == null) return;

        var pool = d.shopItemPool;
        if (pool.state == null) return;

        // Read each slot the randomizer assigned to this shop. Skip empties, coins
        // (never sellable), and de-dup repeated filler so each row is a distinct item.
        int n = Mathf.Max(0, d.shopInventorySize);
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            var slot = pool.state.GetChest(SlotId(d.npcId, i));
            if (slot == null || string.IsNullOrEmpty(slot.itemName)) continue;

            var item = pool.FindItem(slot.itemName);
            if (item == null || item is SOMoney) continue;

            // Progression weapons resolve to the next tier the player needs — so any
            // weapon slot delivers the correct order. Null = family already maxed.
            item = ResolveProgression(item, pool);
            if (item == null) continue;

            if (!seen.Add(item.itemName)) continue;

            _stock.Add(item);
        }
    }

    /// <summary>
    /// For progression weapons, returns the weapon of the same family the player should
    /// receive NEXT (their highest owned tier in that family + 1). Non-weapon items pass
    /// through unchanged. Returns null when the player already owns the family's top tier.
    /// </summary>
    private SOItem ResolveProgression(SOItem slotItem, SOItemPool pool)
    {
        if (slotItem is not SOWeapon w) return slotItem;

        int owned = InventoryHandler.Instance != null
            ? InventoryHandler.Instance.GetHighestWeaponTierOfType(w.GetType())
            : 0;

        // The advertised slot already matches what comes next — deliver it as-is.
        if (w.tier == owned + 1) return slotItem;

        // Otherwise hand over the lowest family tier above what the player owns.
        return FindNextFamilyWeapon(pool, w.GetType(), owned);
    }

    /// <summary>Lowest-tier weapon of <paramref name="family"/> in the pool whose tier exceeds <paramref name="ownedTier"/>, or null.</summary>
    private static SOWeapon FindNextFamilyWeapon(SOItemPool pool, System.Type family, int ownedTier)
    {
        SOWeapon best = null;
        foreach (var entry in pool.items)
        {
            if (entry?.item is SOWeapon w && w.GetType() == family && w.tier > ownedTier
                && (best == null || w.tier < best.tier))
                best = w;
        }
        return best;
    }

    /// <summary>Price for an item after applying the resolved personality multiplier (min 1).</summary>
    public int GetPrice(SOItem item)
    {
        if (item == null || data == null) return 0;
        float mult = ResolvedPersonality switch
        {
            NPCPersonality.Undervalues => 0.6f,
            NPCPersonality.Overcharges => 2.0f,
            _                          => 1.0f,
        };
        return Mathf.Max(1, Mathf.RoundToInt(item.baseValue * mult));
    }

    /// <summary>True if the item has already been bought from this shop (this run/slot).</summary>
    public bool IsSold(SOItem item) =>
        item != null && SaveManager.Instance != null &&
        SaveManager.Instance.HasPurchased(ShopId, item.itemName);

    /// <summary>
    /// True if the shop has at least one item not yet purchased.
    /// Returns true when stock has not been generated yet (assume has items until visited).
    /// </summary>
    public bool HasAnyUnsoldItems()
    {
        if (!_generated) return true;
        foreach (var item in _stock)
            if (!IsSold(item)) return true;
        return false;
    }

    /// <summary>
    /// Attempts to buy: verifies it's in stock, unsold, and affordable. On success
    /// deducts coins, adds to the inventory, and records the purchase on the active
    /// save slot (persisted on the next SaveGame).
    /// </summary>
    public bool TryPurchase(SOItem item)
    {
        if (item == null || !_stock.Contains(item) || IsSold(item)) return false;

        var inv = InventoryHandler.Instance;
        if (inv == null) return false;

        int price = GetPrice(item);
        if (inv.Coins < price) return false;

        // Make sure the inventory can actually hold it before charging.
        if (inv.AddItem(item) == null) return false;

        inv.Coins -= price;
        // Record against the active slot — persisted on the next SaveGame, cleared by
        // NewGame, and restored on load. No per-NPC file, so shops reset with the run.
        SaveManager.Instance?.RecordShopPurchase(ShopId, item.itemName);
        inv.Save();
        return true;
    }
}
