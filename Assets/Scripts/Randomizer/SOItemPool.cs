using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pool of items the randomizer can distribute across chests in a run.
/// Also owns the shared <see cref="RandomizerState"/> so generation and pickups
/// reference a single source of truth.
/// </summary>
[CreateAssetMenu(fileName = "ItemPool", menuName = "Randomizer/ItemPool")]
public class SOItemPool : ScriptableObject
{
    [System.Serializable]
    public class ItemEntry
    {
        public SOItem item;
        [Range(1, 10)] public int priority = 5;
        [Tooltip("Unlocks new zones (progression weapons, keys...).")]
        public bool isProgression;
        [Tooltip("Without this item the game cannot be completed.")]
        public bool isRequired;
    }

    [Header("Run items")]
    public List<ItemEntry> items = new();

    [Header("Filler items — fallback when no higher tier is available")]
    public List<SOItem> fillerItems = new();

    [Header("Global run state")]
    public RandomizerState state;

    // Name-keyed lookup so we can resolve items from the saved state by their name.
    private Dictionary<string, SOItem> _lookup;

    /// <summary>
    /// Resolves an item from its name (as stored in the saved state). Builds the
    /// lookup table lazily on first call and reuses it from then on.
    /// </summary>
    public SOItem FindItem(string itemName)
    {
        if (_lookup == null)
        {
            _lookup = new Dictionary<string, SOItem>(items.Count + fillerItems.Count);

            foreach (var e in items)
                if (e.item != null && !_lookup.ContainsKey(e.item.itemName))
                    _lookup[e.item.itemName] = e.item;

            // Filler items must also be resolvable from the saved state.
            foreach (var filler in fillerItems)
                if (filler != null && !_lookup.ContainsKey(filler.itemName))
                    _lookup[filler.itemName] = filler;
        }

        _lookup.TryGetValue(itemName ?? string.Empty, out var item);
        return item;
    }

    private void OnValidate()
    {
        // Invalidate cache so changes in the inspector take effect.
        _lookup = null;

#if UNITY_EDITOR
        // Auto-populate fillerItems with anything flagged as isFiller (editor convenience).
        if (!Application.isPlaying)
        {
            var autoFiller = items
                .Where(e => e.item != null && e.item.isFiller)
                .Select(e => e.item);

            foreach (var f in autoFiller)
                if (!fillerItems.Contains(f))
                    fillerItems.Add(f);
        }
#endif
    }
}
