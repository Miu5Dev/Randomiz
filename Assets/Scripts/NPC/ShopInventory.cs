using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Per-NPC shop stock. Generated once from the NPC's <see cref="SOItemPool"/> using
/// a deterministic seed (worldSeed ^ npcId hash) so the same shop rolls the same
/// goods every session. Sold items are remembered and persisted to disk, so a
/// purchase made in a previous session stays sold.
///
/// Prices are the item's <see cref="SOItem.baseValue"/> scaled by the NPC's
/// personality multiplier.
/// </summary>
[RequireComponent(typeof(NPCController))]
public class ShopInventory : MonoBehaviour
{
    [SerializeField] private NPCData data;

    private readonly List<SOItem> _stock = new();
    private readonly HashSet<string> _sold = new();
    private bool _generated;

    /// <summary>Items currently rolled for this shop (sold ones remain listed but flagged).</summary>
    public IReadOnlyList<SOItem> Stock => _stock;

    private void Awake()
    {
        if (data == null) data = GetComponent<NPCController>()?.Data;
    }

    /// <summary>
    /// Builds (or rebuilds, on first access) the deterministic stock and restores
    /// which items were already sold. Safe to call repeatedly.
    /// </summary>
    public void EnsureGenerated()
    {
        if (_generated) return;
        Generate();
        LoadSold();
        _generated = true;
    }

    private void Generate()
    {
        _stock.Clear();
        if (data == null || data.shopItemPool == null) return;

        var pool = data.shopItemPool;
        var candidates = new List<SOItem>();
        foreach (var entry in pool.items)
            if (entry != null && entry.item != null)
                candidates.Add(entry.item);

        if (candidates.Count == 0) return;

        // Deterministic seed: world run seed combined with this NPC's identity.
        int worldSeed = pool.state != null ? pool.state.currentSeed : 0;
        var rng = new System.Random(worldSeed ^ (data.npcId?.GetHashCode() ?? 0));

        int size = Mathf.Min(data.shopInventorySize, candidates.Count);
        // Fisher–Yates partial shuffle to pick 'size' distinct items.
        for (int i = 0; i < size; i++)
        {
            int j = rng.Next(i, candidates.Count);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            _stock.Add(candidates[i]);
        }
    }

    /// <summary>Price for an item after applying the personality multiplier (min 1).</summary>
    public int GetPrice(SOItem item)
    {
        if (item == null || data == null) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(item.baseValue * data.PriceMultiplier));
    }

    /// <summary>True if the item has already been bought from this shop.</summary>
    public bool IsSold(SOItem item) => item != null && _sold.Contains(item.itemName);

    /// <summary>
    /// Attempts to buy: verifies it's in stock, unsold, and affordable. On success
    /// deducts coins, adds to the inventory, marks it sold, and persists.
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
        _sold.Add(item.itemName);
        SaveSold();
        inv.Save();
        return true;
    }

    // ─── Persistence (SaveManager integration point) ───────────────────────

    [System.Serializable]
    private class ShopSaveData { public string[] soldItems; }

    private string SavePath =>
        Path.Combine(Application.persistentDataPath, $"shop_{data?.npcId}.json");

    private void SaveSold()
    {
        if (data == null) return;
        var save = new ShopSaveData { soldItems = new string[_sold.Count] };
        _sold.CopyTo(save.soldItems);
        File.WriteAllText(SavePath, JsonUtility.ToJson(save));
    }

    private void LoadSold()
    {
        _sold.Clear();
        if (data == null || !File.Exists(SavePath)) return;
        try
        {
            var save = JsonUtility.FromJson<ShopSaveData>(File.ReadAllText(SavePath));
            if (save?.soldItems == null) return;
            foreach (var name in save.soldItems)
                if (!string.IsNullOrEmpty(name)) _sold.Add(name);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ShopInventory] Load failed for {data.npcId}: {ex.Message}");
        }
    }
}
