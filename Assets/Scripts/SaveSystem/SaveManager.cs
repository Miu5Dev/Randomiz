using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Central save/load coordinator for Randomiz. Owns up to <see cref="SlotCount"/>
/// JSON save slots in <see cref="Application.persistentDataPath"/> and snapshots /
/// restores the live game state by talking to the existing singletons:
///   • RandomizerSystem  — seed + opened chests
///   • InventoryHandler  — items, coins, equipped item, quickslots
///   • HealthSystem       — player health
///   • CheckpointManager — active spawn point
///   • BossTracker        — defeated bosses
///   • SaveManager itself — shop purchases (tracked here via RecordShopPurchase)
///
/// All file I/O is synchronous and happens on the main thread (Unity APIs touched
/// during snapshot/restore are not thread-safe).
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    public const int SlotCount = 3;

    [Header("Scene")]
    [Tooltip("Name of the gameplay scene loaded by NewGame / LoadGame.")]
    [SerializeField] private string gameplaySceneName = "Game";

    [Header("References (optional — auto-found by singleton if left empty)")]
    [SerializeField] private RandomizerSystem randomizer;
    [SerializeField] private string playerTag = "Player";

    private int _currentSlot;

    /// <summary>The slot most recently selected for save/load operations.</summary>
    public int CurrentSlot => _currentSlot;

    // Shop purchases for the active run (kept in memory; persisted on save).
    private readonly List<ShopPurchase> _shopPurchases = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─────────────────────────────────────────────
    // PATHS
    // ─────────────────────────────────────────────

    private static string SlotPath(int slot) =>
        Path.Combine(Application.persistentDataPath, $"save_slot_{slot}.json");

    private static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;

    // ─────────────────────────────────────────────
    // SLOT METADATA
    // ─────────────────────────────────────────────

    /// <summary>
    /// Reads metadata for all <see cref="SlotCount"/> slots. Slots that don't exist
    /// (or fail to parse) are returned as <see cref="SaveSlotInfo.Empty"/>.
    /// </summary>
    public SaveSlotInfo[] GetAllSlots()
    {
        var infos = new SaveSlotInfo[SlotCount];
        for (int i = 0; i < SlotCount; i++)
            infos[i] = ReadSlotInfo(i);
        return infos;
    }

    private SaveSlotInfo ReadSlotInfo(int slot)
    {
        string path = SlotPath(slot);
        if (!File.Exists(path)) return SaveSlotInfo.Empty(slot);

        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            if (data == null) return SaveSlotInfo.Empty(slot);

            return new SaveSlotInfo
            {
                slotIndex = slot,
                exists = true,
                timestampTicks = data.timestampTicks,
                seed = data.seed,
                highestWeaponTier = HighestTierFromNames(data.inventoryItemNames),
                coins = data.coins,
            };
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveManager] Failed to read slot {slot}: {ex.Message}");
            return SaveSlotInfo.Empty(slot);
        }
    }

    // ─────────────────────────────────────────────
    // SLOT SELECTION
    // ─────────────────────────────────────────────

    public void SetCurrentSlot(int slot)
    {
        if (!IsValidSlot(slot))
        {
            Debug.LogError($"[SaveManager] Invalid slot index {slot} (valid 0..{SlotCount - 1}).");
            return;
        }
        _currentSlot = slot;
    }

    // ─────────────────────────────────────────────
    // SAVE
    // ─────────────────────────────────────────────

    /// <summary>Serializes the full live game state to the given slot's JSON file.</summary>
    public void SaveGame(int slot)
    {
        if (!IsValidSlot(slot))
        {
            Debug.LogError($"[SaveManager] SaveGame: invalid slot {slot}.");
            return;
        }

        var data = new SaveData();

        // ─ Seed ─
        var rnd = ResolveRandomizer();
        if (rnd != null)
        {
            data.seed = rnd.CurrentSeed;
            data.openedChestIds = rnd.GetOpenedChestIds();
        }

        // ─ Player transform & health ─
        GameObject player = FindPlayer();
        if (player != null)
        {
            Vector3 p = player.transform.position;
            data.playerPosX = p.x;
            data.playerPosY = p.y;
            data.playerPosZ = p.z;

            var health = player.GetComponent<HealthSystem>();
            if (health != null) data.playerHealth = health.CurrentHealth;
        }

        // ─ Inventory ─
        var inv = InventoryHandler.Instance;
        if (inv != null)
        {
            data.inventoryItemNames = new List<string>(inv.GetItemNamesSnapshot());
            data.coins = inv.Coins;
        }
        data.equippedItemName = EquipHandler.Instance?.EquipedItem?.itemName;
        data.quickslot1Name   = QuickslotManager.Instance?.Slot1?.itemName;
        data.quickslot2Name   = QuickslotManager.Instance?.Slot2?.itemName;

        // ─ World progress ─
        if (BossTracker.Instance != null)
            data.defeatedBossIds = BossTracker.Instance.DefeatedBosses;
        data.shopPurchases = new List<ShopPurchase>(_shopPurchases);

        // ─ Key inventory ─
        if (KeyInventory.Instance != null)
        {
            data.heldKeyIds = new List<string>();
            foreach (var k in KeyInventory.Instance.Keys)
                data.heldKeyIds.Add(k.keyId);
        }

        // ─ Checkpoint ─
        var cp = CheckpointManager.Instance;
        if (cp != null)
        {
            data.checkpointId = cp.ActiveCheckpointId;
            Vector3 spawn = cp.GetSpawnPosition();
            data.checkpointX = spawn.x;
            data.checkpointY = spawn.y;
            data.checkpointZ = spawn.z;
        }

        // ─ Metadata ─
        data.timestampTicks = DateTime.Now.Ticks;

        try
        {
#if UNITY_EDITOR
            File.WriteAllText(SlotPath(slot), JsonUtility.ToJson(data, prettyPrint: true));
#else
            File.WriteAllText(SlotPath(slot), JsonUtility.ToJson(data));
#endif
            _currentSlot = slot;
            Debug.Log($"[SaveManager] Saved to slot {slot}.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveManager] Save to slot {slot} failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // LOAD
    // ─────────────────────────────────────────────

    /// <summary>
    /// Reads the given slot and restores all game state. Returns false if the slot
    /// is empty or fails to parse. Restoration of scene-bound state (player position,
    /// checkpoints, opened chests) is deferred until the gameplay scene finishes
    /// loading, then applied via <see cref="ApplyRestore"/>.
    /// </summary>
    public bool LoadGame(int slot)
    {
        if (!IsValidSlot(slot))
        {
            Debug.LogError($"[SaveManager] LoadGame: invalid slot {slot}.");
            return false;
        }

        string path = SlotPath(slot);
        if (!File.Exists(path)) return false;

        SaveData data;
        try
        {
            data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveManager] Load slot {slot} failed: {ex.Message}");
            return false;
        }

        if (data == null) return false;

        _currentSlot = slot;
        LoadGameplaySceneThenRestore(data);
        return true;
    }

    // ─────────────────────────────────────────────
    // NEW GAME
    // ─────────────────────────────────────────────

    /// <summary>
    /// Starts a fresh run in the given slot: clears prior progress, generates a new
    /// seed, writes an initial save, and loads the gameplay scene. The randomizer
    /// regenerates its seed from the scene once it loads.
    /// </summary>
    public void NewGame(int slot)
    {
        if (!IsValidSlot(slot))
        {
            Debug.LogError($"[SaveManager] NewGame: invalid slot {slot}.");
            return;
        }

        _currentSlot = slot;
        _shopPurchases.Clear();
        BossTracker.Instance?.Clear();
        CheckpointManager.Instance?.Clear();
        DeleteSlot(slot);

        // Deferred: once the scene loads, generate the seed from the scene's chests
        // and write the initial save.
        SceneManager.sceneLoaded += OnNewGameSceneLoaded;
        SceneManager.LoadScene(gameplaySceneName);
    }

    private void OnNewGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnNewGameSceneLoaded;

        var rnd = ResolveRandomizer();
        rnd?.GenerateSeedFromScene();

        // Persist the initial state for this run.
        SaveGame(_currentSlot);
    }

    // ─────────────────────────────────────────────
    // DELETE
    // ─────────────────────────────────────────────

    public void DeleteSlot(int slot)
    {
        if (!IsValidSlot(slot)) return;
        string path = SlotPath(slot);
        if (File.Exists(path)) File.Delete(path);
    }

    // ─────────────────────────────────────────────
    // SHOP PURCHASES
    // ─────────────────────────────────────────────

    /// <summary>Records that the player bought an item from a shop (idempotent).</summary>
    public void RecordShopPurchase(string shopId, string itemName)
    {
        if (string.IsNullOrEmpty(shopId) || string.IsNullOrEmpty(itemName)) return;
        if (HasPurchased(shopId, itemName)) return;
        _shopPurchases.Add(new ShopPurchase(shopId, itemName));
    }

    public bool HasPurchased(string shopId, string itemName)
    {
        foreach (var p in _shopPurchases)
            if (p.shopId == shopId && p.itemName == itemName)
                return true;
        return false;
    }

    // ─────────────────────────────────────────────
    // RESTORE (scene-load deferred)
    // ─────────────────────────────────────────────

    private SaveData _pendingRestore;

    private void LoadGameplaySceneThenRestore(SaveData data)
    {
        _pendingRestore = data;
        SceneManager.sceneLoaded += OnLoadGameSceneLoaded;
        SceneManager.LoadScene(gameplaySceneName);
    }

    private void OnLoadGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnLoadGameSceneLoaded;
        if (_pendingRestore != null)
        {
            ApplyRestore(_pendingRestore);
            _pendingRestore = null;
        }
    }

    /// <summary>Applies a loaded <see cref="SaveData"/> to the live scene objects.</summary>
    private void ApplyRestore(SaveData data)
    {
        // ─ Randomizer: regenerate the exact seed, then re-apply opened chests ─
        var rnd = ResolveRandomizer();
        if (rnd != null)
        {
            // The seed lives in RandomizerState; the scene's chests rebuild the layout
            // deterministically from it. GenerateSeedFromScene reads the scene chests;
            // because Random.InitState uses the saved seed, the layout matches.
            // We restore the saved seed first so generation is reproducible.
            rnd.GenerateSeedFromScene();
            rnd.RestoreOpenedChests(data.openedChestIds);
        }

        // ─ Inventory + coins ─
        var inv = InventoryHandler.Instance;
        inv?.RestoreInventory(data.inventoryItemNames, data.coins);

        // ─ Equipped item ─
        if (inv != null && !string.IsNullOrEmpty(data.equippedItemName) && EquipHandler.Instance != null)
        {
            var equipped = inv.ResolveItem(data.equippedItemName);
            if (equipped != null) EquipHandler.Instance.EquipItem(equipped);
        }

        // ─ Quickslots ─
        if (inv != null && QuickslotManager.Instance != null)
        {
            var s1 = inv.ResolveItem(data.quickslot1Name);
            if (s1 != null) QuickslotManager.Instance.AssignToSlot(1, s1);
            var s2 = inv.ResolveItem(data.quickslot2Name);
            if (s2 != null) QuickslotManager.Instance.AssignToSlot(2, s2);
        }

        // ─ Bosses + shop purchases ─
        BossTracker.Instance?.RestoreFrom(data.defeatedBossIds);
        _shopPurchases.Clear();
        if (data.shopPurchases != null)
            _shopPurchases.AddRange(data.shopPurchases);

        // ─ Key inventory ─
        if (KeyInventory.Instance != null && data.heldKeyIds != null)
        {
            KeyInventory.Instance.Clear();
            foreach (string kid in data.heldKeyIds)
                KeyInventory.Instance.AddKey(kid);
        }

        // ─ Checkpoint ─
        Vector3 checkpointPos = new Vector3(data.checkpointX, data.checkpointY, data.checkpointZ);
        CheckpointManager.Instance?.RestoreActiveCheckpoint(data.checkpointId, checkpointPos);

        // ─ Player transform & health ─
        GameObject player = FindPlayer();
        if (player != null)
        {
            // Spawn at the checkpoint if we have one, otherwise the saved position.
            Vector3 spawn = CheckpointManager.Instance != null &&
                            !string.IsNullOrEmpty(data.checkpointId)
                ? CheckpointManager.Instance.GetSpawnPosition()
                : new Vector3(data.playerPosX, data.playerPosY, data.playerPosZ);

            // Move via CharacterController-safe approach: disable, set, re-enable if present.
            var controller = player.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
                player.transform.position = spawn;
                controller.enabled = true;
            }
            else
            {
                player.transform.position = spawn;
            }

            var health = player.GetComponent<HealthSystem>();
            if (health != null && data.playerHealth > 0f)
                health.SetHealth(data.playerHealth);
        }

        Debug.Log($"[SaveManager] Restored slot {_currentSlot}.");
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    private RandomizerSystem ResolveRandomizer()
    {
        if (randomizer == null)
            randomizer = FindObjectOfType<RandomizerSystem>();
        return randomizer;
    }

    private GameObject FindPlayer() => GameObject.FindGameObjectWithTag(playerTag);

    private static int HighestTierFromNames(List<string> itemNames)
    {
        // Resolve names through the live inventory pool (if the InventoryHandler
        // singleton exists) to surface the highest weapon tier for slot listings.
        // Falls back to 0 when no pool is available (e.g. on a cold main menu).
        var inv = InventoryHandler.Instance;
        if (inv == null || itemNames == null) return 0;

        int max = 0;
        foreach (var name in itemNames)
        {
            if (inv.ResolveItem(name) is SOWeapon w && w.tier > max)
                max = w.tier;
        }
        return max;
    }
}
