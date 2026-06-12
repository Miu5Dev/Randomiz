using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Singleton MonoBehaviour that tracks all ChestBehaviour objects in the scene
/// and their opened/closed state. Provides IReadOnlyList of chest entries to UI systems.
///
/// Auto-discovers chests on Start using FindObjectsByType and subscribes to
/// OnChestOpenedEvent to track which chests have been opened.
/// </summary>
public class MinimapManager : MonoBehaviour
{
    private static MinimapManager _instance;
    public static MinimapManager Instance => _instance;

    [SerializeField] private float mapRadius = 30f;

    private List<ChestMapEntry> chestEntries = new();
    private List<ShopMapEntry>  shopEntries  = new();
    private HashSet<ChestBehaviour> openedChests = new();

    public IReadOnlyList<ChestMapEntry> ChestEntries => chestEntries.AsReadOnly();
    public IReadOnlyList<ShopMapEntry>  ShopEntries  => shopEntries.AsReadOnly();
    public float MapRadius => mapRadius;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void Start()
    {
        DiscoverChests();
        DiscoverShops();
        EventBus.Subscribe<OnChestOpenedEvent>(OnChestOpened);
    }

    private void OnDestroy()
    {
        EventBus.Unsubscribe<OnChestOpenedEvent>(OnChestOpened);
        if (_instance == this)
            _instance = null;
    }

    /// <summary>
    /// Finds all ChestBehaviour objects in the scene and initializes entries.
    /// Already-opened chests are marked as opened from their state.
    /// </summary>
    private void DiscoverChests()
    {
        chestEntries.Clear();
        openedChests.Clear();

        var chests = FindObjectsByType<ChestBehaviour>(FindObjectsSortMode.None);
        foreach (var chest in chests)
        {
            chestEntries.Add(new ChestMapEntry
            {
                worldPos = chest.transform.position,
                isOpen = chest.IsOpened,
                chest = chest
            });

            if (chest.IsOpened)
                openedChests.Add(chest);
        }

        Debug.Log($"[MinimapManager] Discovered {chestEntries.Count} chests.");
    }

    private void DiscoverShops()
    {
        shopEntries.Clear();
        var npcs = FindObjectsByType<NPCController>(FindObjectsSortMode.None);
        foreach (var npc in npcs)
        {
            if (npc.Data == null || !npc.Data.isShopkeeper) continue;
            var shop = npc.Shop;
            if (shop == null) continue;
            shopEntries.Add(new ShopMapEntry { worldPos = npc.transform.position, shop = shop });
        }
        Debug.Log($"[MinimapManager] Discovered {shopEntries.Count} shops.");
    }

    /// <summary>
    /// Called when a chest is opened. Updates the entry's open state.
    /// </summary>
    private void OnChestOpened(OnChestOpenedEvent evt)
    {
        if (evt?.chest == null) return;

        openedChests.Add(evt.chest);

        // Update the entry
        for (int i = 0; i < chestEntries.Count; i++)
        {
            if (chestEntries[i].chest == evt.chest)
            {
                var entry = chestEntries[i];
                entry.isOpen = true;
                chestEntries[i] = entry;
                break;
            }
        }
    }

    /// <summary>
    /// Checks if a chest is currently opened.
    /// </summary>
    public bool IsChestOpened(ChestBehaviour chest)
    {
        return openedChests.Contains(chest);
    }
}

/// <summary>
/// Data structure holding a chest's map position and state.
/// </summary>
public struct ChestMapEntry
{
    public Vector3 worldPos;
    public bool isOpen;
    public ChestBehaviour chest;
}

/// <summary>
/// Data structure holding a shopkeeper's map position and a reference for state queries.
/// </summary>
public struct ShopMapEntry
{
    public Vector3 worldPos;
    public ShopInventory shop;
}
