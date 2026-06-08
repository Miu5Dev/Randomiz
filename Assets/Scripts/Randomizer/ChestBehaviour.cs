using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A chest in the world. Its content is determined at runtime by the randomizer
/// (via the shared <see cref="RandomizerState"/> on the pool ScriptableObject)
/// — not by inspector wiring on each chest.
///
/// Lifecycle:
///   • Awake → ensure locationId is assigned (falls back to GameObject name).
///   • Start → read state from the pool (TestSceneBootstrap.Awake has run by now,
///             so the state is populated).
/// </summary>
public class ChestBehaviour : MonoBehaviour
{
    [Header("Unique location ID — must match across runs")]
    public string locationId;

    [Header("SOItems required to access this chest")]
    public List<SOItem> requiredItems = new();

    [Header("Pool — needed to resolve items from the saved state")]
    [SerializeField] private SOItemPool pool;

    private RandomizerState State => pool != null ? pool.state : null;
    public bool IsOpened => pool?.state?.GetChest(locationId)?.opened ?? false;

    private void Awake()
    {
        // Auto-fill locationId from the GameObject name if left blank.
        if (string.IsNullOrEmpty(locationId))
        {
            locationId = gameObject.name;
            Debug.Log($"[Chest:{locationId}] locationId auto-assigned from GameObject name.");
        }
    }

    private void Start()
    {
        // By Start, TestSceneBootstrap.Awake has loaded/generated state.
        if (pool == null)
        {
            Debug.LogError($"[Chest:{locationId}] No SOItemPool assigned.");
            return;
        }

        if (State == null)
        {
            Debug.LogError($"[Chest:{locationId}] Pool has no RandomizerState assigned.");
            return;
        }

        var s = State.GetChest(locationId);
        if (s == null)
        {
            Debug.LogWarning($"[Chest:{locationId}] Not found in state — was the seed generated?");
            return;
        }

        if (s.opened) SetVisualOpened();
    }

    /// <summary>No-arg wrapper for UnityEvent wiring (e.g. Interactable.OnUse).</summary>
    public void OpenInteract() => Open(gameObject);

    /// <summary>Called by an Interactable trigger / Use(): resolves item and adds to inventory.</summary>
    public void Open(GameObject opener)
    {
        if (State == null) return;
        var s = State.GetChest(locationId);
        if (s == null || s.opened) return;

        var item = pool.FindItem(s.itemName);
        if (item == null)
        {
            Debug.LogError($"[Chest:{locationId}] Item '{s.itemName}' not found in pool.");
            return;
        }

        var resolved = ResolveItem(item);
        if (resolved == null)
        {
            Debug.LogWarning($"[Chest:{locationId}] Locked — get the previous tier first.");
            return;
        }

        if (InventoryHandler.Instance == null)
        {
            Debug.LogError($"[Chest:{locationId}] No InventoryHandler in scene — chest cannot open.");
            return;
        }

        // Try adding the resolved item first.
        SOItem addedItem = InventoryHandler.Instance.AddItem(resolved);

        // If that failed (inventory full / type constraint), try any filler.
        if (addedItem == null && pool.fillerItems != null && pool.fillerItems.Count > 0)
        {
            var shuffled = new List<SOItem>(pool.fillerItems);
            ShuffleInPlace(shuffled);

            foreach (var filler in shuffled)
            {
                addedItem = InventoryHandler.Instance.AddItem(filler);
                if (addedItem != null)
                {
                    Debug.Log($"[Chest:{locationId}] ⚠ {resolved.itemName} didn't fit; gave filler {filler.itemName}.");
                    break;
                }
            }
        }

        if (addedItem == null)
        {
            Debug.LogWarning($"[Chest:{locationId}] Inventory rejected every item (full or constrained). Chest stays closed.");
            return;
        }

        // OnItemPickedUpEvent is raised inside InventoryHandler.AddItem (the single
        // chokepoint for all acquisitions) — not here, to avoid firing it twice.
        EventBus.Raise(new OnChestOpenedEvent(this));
        State.SetOpened(locationId);
        SetVisualOpened();

        Debug.Log($"[Chest:{locationId}] {opener.name} got: {addedItem.itemName}" +
                  (addedItem is SOWeapon w ? $" [Tier {w.tier}]" : ""));
    }

    /// <summary>
    /// Adjusts the intended item before delivery — handles tier mismatch and sequence breaks.
    /// </summary>
    private SOItem ResolveItem(SOItem intended)
    {
        if (intended is not SOWeapon intendedWeapon)
            return intended;

        int playerMaxTier = InventoryHandler.Instance != null ? InventoryHandler.Instance.GetHighestWeaponTier() : 0;
        int intendedTier  = intendedWeapon.tier;

        // Same tier or lower → no upgrade, give filler.
        if (intendedTier <= playerMaxTier)
            return GetRandomFiller();

        // Normal progression.
        if (intendedTier == playerMaxTier + 1)
            return intended;

        // Sequence break — try to swap with another chest holding the right tier.
        int neededTier = playerMaxTier + 1;
        var swapped = TrySwapForNextTier(neededTier);
        if (swapped != null) return swapped;

        // No swap available → filler.
        return GetRandomFiller();
    }

    private SOItem GetRandomFiller()
    {
        if (pool.fillerItems == null || pool.fillerItems.Count == 0)
        {
            Debug.LogError($"[Chest:{locationId}] No filler items configured in pool.");
            return null;
        }
        return pool.fillerItems[Random.Range(0, pool.fillerItems.Count)];
    }

    private SOWeapon TrySwapForNextTier(int neededTier)
    {
        var myState = State.GetChest(locationId);
        if (myState == null || myState.opened) return null;

        foreach (var other in State.chests)
        {
            if (other.locationId == locationId || other.opened) continue;

            var otherItem = pool.FindItem(other.itemName);
            if (otherItem is SOWeapon w && w.tier == neededTier)
            {
                // Swap item names between this chest and the other.
                (myState.itemName, other.itemName) = (other.itemName, myState.itemName);
                State.Save();
                return (SOWeapon)pool.FindItem(myState.itemName);
            }
        }
        return null;
    }

    private static void ShuffleInPlace<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void SetVisualOpened()
    {
        // Hook for animation / sprite swap.
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(locationId))
            locationId = gameObject.name;
    }

    private void OnDrawGizmosSelected()
    {
        var s = pool != null && pool.state != null ? pool.state.GetChest(locationId) : null;
        string label;
        if (s == null)            label = "[ no state ]";
        else if (s.opened)        label = "✓ opened";
        else if (!string.IsNullOrEmpty(s.itemName))
        {
            var item = pool.FindItem(s.itemName);
            label = s.itemName + (item is SOWeapon w ? $" [T{w.tier}]" : "");
        }
        else                      label = "[ empty ]";

        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, label);
    }
#endif
}
