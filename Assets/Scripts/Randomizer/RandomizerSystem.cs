using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Drives chest-content randomization for the run. Walks the chest graph respecting
/// reachability (each chest can declare required items) and tier progression to
/// produce a beatable seed.
///
/// Entry points:
///   • LoadOrGenerate — preferred at scene start; restores a save or generates new.
///   • NewGame        — wipes save and generates a fresh seed.
///   • GenerateSeedFromScene — discovers all ChestBehaviour in the scene and generates.
/// </summary>
public class RandomizerSystem : MonoBehaviour
{
    [SerializeField] private SOItemPool pool;
    [Tooltip("-1 = random seed each run")]
    [SerializeField] private int seed = -1;

    private RandomizerState State => pool.state;

    /// <summary>The seed of the current run (exposed for the save system).</summary>
    public int CurrentSeed => State.currentSeed;

    /// <summary>List of location ids whose chests are currently marked opened.</summary>
    public List<string> GetOpenedChestIds()
    {
        var result = new List<string>();
        foreach (var c in State.chests)
            if (c.opened) result.Add(c.locationId);
        return result;
    }

    /// <summary>Re-applies opened flags for the given chest ids after a load.</summary>
    public void RestoreOpenedChests(IEnumerable<string> openedIds)
    {
        if (openedIds == null) return;
        foreach (var id in openedIds)
            State.SetOpened(id);
    }

    // Cache of "what items each location requires", filled during generation/load.
    private Dictionary<string, List<SOItem>> _locationRequirements = new();

    // Shop stock slots that must never receive coins as filler — you can't sell currency.
    private readonly HashSet<string> _coinlessLocations = new();

    // Shared empty requirement list for shop slots (always accessible). Never mutated.
    private static readonly List<SOItem> EmptyReq = new();

    // ─────────────────────────────────────────────
    // PUBLIC ENTRY POINTS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Restores the saved seed if one exists, otherwise generates a new seed
    /// for the supplied location ids and their accessibility requirements.
    /// </summary>
    public void LoadOrGenerate(List<string> locationIds, List<List<SOItem>> requirements)
    {
        CacheRequirements(locationIds, requirements);

        if (State.HasSave() && State.Load())
        {
            Debug.Log("[Randomizer] Previous run restored ✓");
            return;
        }

        GenerateSeed(locationIds, requirements);
    }

    /// <summary>Wipe the existing save and generate a brand-new seed.</summary>
    public void NewGame(List<string> locationIds, List<List<SOItem>> requirements)
    {
        InventoryHandler.DeleteSave();
        State.Clear();
        GenerateSeed(locationIds, requirements);
    }

    /// <summary>
    /// Auto-discover all ChestBehaviour and generate. Pass <paramref name="overrideSeed"/>
    /// (e.g. a saved run's seed) to reproduce that exact layout/shop deterministically;
    /// otherwise a fresh seed is rolled (or the inspector seed, if set).
    /// </summary>
    public void GenerateSeedFromScene(int? overrideSeed = null)
    {
        // FindObjectsSortMode.None skips the sort step (fastest variant).
        // Build the lists directly from the array — no LINQ allocations.
        var chests = FindObjectsByType<ChestBehaviour>(FindObjectsSortMode.None);
        int n = chests.Length;

        var ids  = new List<string>(n);
        var reqs = new List<List<SOItem>>(n);
        for (int i = 0; i < n; i++)
        {
            ids.Add(chests[i].locationId);
            reqs.Add(chests[i].requiredItems);
        }

        GenerateSeed(ids, reqs, overrideSeed);
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    public void GenerateSeed(List<string> locationIds, List<List<SOItem>> requirements, int? overrideSeed = null)
    {
        locationIds  ??= new List<string>();
        requirements ??= new List<List<SOItem>>();

        // Shop stock slots are randomizer locations too: a progression item (e.g. a
        // sword) can be placed in a chest OR in a shop. Append them before validation
        // so EVERY entry point (LoadOrGenerate, NewGame, GenerateSeedFromScene) includes
        // shops without each caller having to discover them.
        AppendShopLocations(locationIds, requirements);

        if (locationIds.Count == 0)
        {
            Debug.LogError("[Randomizer] Empty locationIds list.");
            return;
        }

        // Validate uniqueness of location ids.
        var duplicates = locationIds
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            Debug.LogError(
                $"[Randomizer] ✗ Duplicate location ids: {string.Join(", ", duplicates)}\n" +
                $"Solution: rename the GameObjects so each chest has a unique id.");
            return;
        }

        int usedSeed = overrideSeed ?? (seed == -1 ? Random.Range(0, int.MaxValue) : seed);
        Random.InitState(usedSeed);
        State.SetSeed(usedSeed);
        Debug.Log($"[Randomizer] Generating seed {usedSeed} with {locationIds.Count} chests...");

        // Register all chests in the state.
        State.Clear();
        foreach (var id in locationIds)
            State.Register(id);

        CacheRequirements(locationIds, requirements);

        // Split items into progression (placed with reachability rules) and others.
        var progression = pool.items
            .Where(e => e.isProgression)
            .OrderBy(e => GetTier(e.item))
            .ThenByDescending(e => e.priority)
            .Select(e => e.item)
            .ToList();

        var fill = pool.items
            .Where(e => !e.isProgression)
            .Select(e => e.item)
            .ToList();

        AssumedFill(progression);
        FillRemaining(fill);
        State.Save();

        if (ValidateSeed())
            Debug.Log("[Randomizer] ✓ Seed valid and saved.");
        else
            Debug.LogError("[Randomizer] ✗ Invalid seed — check requiredItems and the pool.");
    }

    // ─────────────────────────────────────────────
    // ASSUMED FILL
    // ─────────────────────────────────────────────

    /// <summary>
    /// Place progression items tier-by-tier, ensuring each placement is reachable
    /// given the items the player has already collected at that tier. Leftover empty
    /// chests are filled with random filler items so no chest is ever empty.
    /// </summary>
    private void AssumedFill(List<SOItem> itemsToPlace)
    {
        // Group items by tier so we can place low tiers first (they may unlock chests).
        var itemsByTier = new Dictionary<int, List<SOItem>>();
        foreach (var item in itemsToPlace)
        {
            int tier = GetTier(item);
            if (!itemsByTier.ContainsKey(tier))
                itemsByTier[tier] = new List<SOItem>();
            itemsByTier[tier].Add(item);
        }

        var sortedTiers = itemsByTier.Keys.OrderBy(t => t).ToList();
        var placed = new HashSet<string>(); // items already placed (by itemName)

        foreach (var tier in sortedTiers)
        {
            var tierItems = itemsByTier[tier];
            Shuffle(tierItems);

            foreach (var item in tierItems)
            {
                var assumed = placed.ToHashSet();
                int currentMaxTier = GetReachableTierSoFar(assumed);
                int itemTier = GetTier(item);

                // Pick reachable chests that respect tier progression.
                var reachable = State.chests
                    .Where(c =>
                        string.IsNullOrEmpty(c.itemName) &&
                        IsAccessible(c.locationId, assumed) &&
                        IsTierProgressionValid(itemTier, currentMaxTier))
                    .ToList();

                if (reachable.Count == 0)
                {
                    // Recovery: for low tiers, force-place in any empty chest.
                    if (itemTier <= 1)
                    {
                        var anyEmpty = State.chests.FirstOrDefault(c => string.IsNullOrEmpty(c.itemName));
                        if (anyEmpty != null)
                        {
                            Debug.LogWarning($"[Randomizer] ⚠ No reachable chest for '{item.itemName}' " +
                                             $"(T{itemTier}); force-placing at {anyEmpty.locationId}.");
                            State.SetItem(anyEmpty.locationId, item);
                            placed.Add(item.itemName);
                            continue;
                        }
                    }

                    Debug.LogError(
                        $"[Randomizer] ✗ No location for '{item.itemName}' " +
                        $"(tier {itemTier}, maxReachable={currentMaxTier}). " +
                        $"Placed so far: {placed.Count}/{itemsToPlace.Count}");
                    continue; // try next item
                }

                State.SetItem(reachable[Random.Range(0, reachable.Count)].locationId, item);
                placed.Add(item.itemName);
            }
        }

        // Report unplaced progression items.
        var unplaced = itemsToPlace.Where(i => !placed.Contains(i.itemName)).ToList();
        if (unplaced.Count > 0)
        {
            Debug.LogWarning($"[Randomizer] ⚠ {unplaced.Count} unplaced items: " +
                             $"{string.Join(", ", unplaced.Select(i => i.itemName))}");
        }

        // Fill remaining empty chests with filler (so no chest is ever empty).
        var emptyChests = State.chests.Where(c => string.IsNullOrEmpty(c.itemName)).ToList();
        if (emptyChests.Count == 0) return;

        if (pool.fillerItems == null || pool.fillerItems.Count == 0)
        {
            Debug.LogError(
                $"[Randomizer] ✗ CRITICAL: {emptyChests.Count} empty chests and pool has no filler items.\n" +
                $"Solution: open the SOItemPool asset and add at least 1 item to 'fillerItems'.");
            return;
        }

        Debug.Log($"[Randomizer] ℹ {emptyChests.Count} empty chests — filling with random filler.");
        foreach (var chest in emptyChests)
        {
            // Shop slots get non-coin filler (a vendor can't sell currency).
            var filler = PickFiller(_coinlessLocations.Contains(chest.locationId));
            if (filler != null) State.SetItem(chest.locationId, filler);
        }
    }

    /// <summary>
    /// Fills any empty chest with non-progression items (one-to-one).
    /// AssumedFill already filled empty chests with filler, so this only matters
    /// when there are more non-progression items than empty chests left.
    /// </summary>
    private void FillRemaining(List<SOItem> fillItems)
    {
        var empty = State.chests.Where(c => string.IsNullOrEmpty(c.itemName)).ToList();
        if (empty.Count == 0 || fillItems == null || fillItems.Count == 0) return;

        Shuffle(fillItems);
        for (int i = 0; i < empty.Count && i < fillItems.Count; i++)
        {
            // Never assign currency to a shop slot — leave it empty (shop skips it).
            if (fillItems[i] is SOMoney && _coinlessLocations.Contains(empty[i].locationId))
                continue;
            State.SetItem(empty[i].locationId, fillItems[i]);
        }
    }

    // ─────────────────────────────────────────────
    // POST-GEN VALIDATION
    // ─────────────────────────────────────────────

    /// <summary>
    /// Simulates a playthrough: collects every reachable chest, then re-checks if
    /// new collected items unlocked more chests, until no more progress is possible.
    /// Returns true if all required items are reachable.
    /// </summary>
    public bool ValidateSeed()
    {
        var collected = new HashSet<string>();
        bool progress = true;

        while (progress)
        {
            progress = false;
            foreach (var chest in State.chests)
                if (!string.IsNullOrEmpty(chest.itemName) &&
                    IsAccessible(chest.locationId, collected) &&
                    collected.Add(chest.itemName))
                    progress = true;
        }

        return pool.items
            .Where(e => e.isRequired)
            .All(e => collected.Contains(e.item.itemName));
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    private void CacheRequirements(List<string> ids, List<List<SOItem>> reqs)
    {
        _locationRequirements = new();
        for (int i = 0; i < ids.Count; i++)
            _locationRequirements[ids[i]] = reqs[i];
    }

    /// <summary>
    /// Discovers every shop in the scene and appends one randomizer location per stock
    /// slot, with no access requirements (shops are always reachable on foot). Slot ids
    /// are sorted for a stable registration order, so the same seed reproduces the same
    /// layout across loads regardless of FindObjectsByType ordering.
    /// </summary>
    private void AppendShopLocations(List<string> ids, List<List<SOItem>> reqs)
    {
        _coinlessLocations.Clear();

        var shops = FindObjectsByType<ShopInventory>(FindObjectsSortMode.None);
        if (shops.Length == 0) return;

        var shopIds = new List<string>();
        foreach (var shop in shops)
            shop.CollectSlotLocationIds(shopIds);

        shopIds.Sort(System.StringComparer.Ordinal);

        foreach (var id in shopIds)
        {
            if (!_coinlessLocations.Add(id)) continue; // defensive dedupe
            ids.Add(id);
            reqs.Add(EmptyReq);
        }
    }

    /// <summary>
    /// Picks a random filler item. When <paramref name="excludeCoins"/> is true (shop
    /// slot), currency items are skipped so vendors never list coins for sale. Returns
    /// null if no suitable filler exists.
    /// </summary>
    private SOItem PickFiller(bool excludeCoins)
    {
        var fillers = pool.fillerItems;
        if (fillers == null || fillers.Count == 0) return null;

        if (!excludeCoins)
            return fillers[Random.Range(0, fillers.Count)];

        int nonCoin = 0;
        foreach (var f in fillers)
            if (f != null && f is not SOMoney) nonCoin++;
        if (nonCoin == 0) return null;

        int pick = Random.Range(0, nonCoin);
        foreach (var f in fillers)
            if (f != null && f is not SOMoney && pick-- == 0)
                return f;
        return null;
    }

    private int GetReachableTierSoFar(HashSet<string> assumed) =>
        State.chests
            .Where(c => !string.IsNullOrEmpty(c.itemName) && IsAccessible(c.locationId, assumed))
            .Select(c => GetTier(pool.FindItem(c.itemName)))
            .DefaultIfEmpty(0)
            .Max();

    private bool IsAccessible(string locationId, HashSet<string> available)
    {
        if (!_locationRequirements.TryGetValue(locationId, out var reqs)) return true;
        return reqs == null || reqs.All(r => r != null && available.Contains(r.itemName));
    }

    private bool IsTierProgressionValid(int itemTier, int currentMaxTier)
    {
        // Tier 0 (potions, keys) and tier 1 (starter sword) always validate.
        if (itemTier <= 1) return true;

        // Tier 2+ requires current max tier ≥ itemTier - 1 (so the player can reach it).
        return itemTier <= currentMaxTier + 1;
    }

    private int GetTier(SOItem item) => item is SOWeapon w ? w.tier : 0;

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
