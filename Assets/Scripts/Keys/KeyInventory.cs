using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Singleton that tracks all keys the player is currently holding.
/// Separate from the main InventoryHandler — keys are not SOItems and do not
/// occupy inventory slots.
/// Persists to a dedicated JSON file alongside the main inventory save.
/// </summary>
public class KeyInventory : MonoBehaviour
{
    public static KeyInventory Instance { get; private set; }

    // ─── Key data ──────────────────────────────────────────────────────────────

    /// <summary>Runtime entry for a single held key.</summary>
    [System.Serializable]
    public class KeyData
    {
        public string keyId;
        public string displayName;
        public Sprite icon;
    }

    // Internal list — KeyData is serializable for the inspector view but we
    // manage it ourselves so the list is not exposed to the inspector directly.
    [SerializeField] private List<KeyData> keys = new();

    /// <summary>Read-only view of the current key list.</summary>
    public List<KeyData> Keys => keys;

    // ─── Save / load ───────────────────────────────────────────────────────────

    [System.Serializable]
    private class SaveData
    {
        // We only persist id + displayName; Sprite references cannot be saved
        // as JSON, so they are re-resolved from KeyPickup/world objects on load
        // (or simply lost — keys are typically consumed on door use anyway).
        public List<SaveEntry> entries = new();

        [System.Serializable]
        public class SaveEntry
        {
            public string keyId;
            public string displayName;
        }
    }

    private static string SavePath =>
        Path.Combine(Application.persistentDataPath, "keyInventory.json");

    private bool _initialized;

    // ─── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (File.Exists(SavePath)) Load();
        _initialized = true;
    }

    // ─── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a key with the given ID to the inventory, then raises
    /// <see cref="OnKeyInventoryChangedEvent"/>.
    /// </summary>
    public void AddKey(string keyId, string displayName = "", Sprite icon = null)
    {
        keys.Add(new KeyData
        {
            keyId       = keyId,
            displayName = string.IsNullOrEmpty(displayName) ? keyId : displayName,
            icon        = icon
        });

        Save();
        EventBus.Raise(new OnKeyInventoryChangedEvent());
    }

    /// <summary>Returns true if the player holds at least one key with the given ID.</summary>
    public bool HasKey(string keyId)
    {
        return keys.Exists(k => k.keyId == keyId);
    }

    /// <summary>
    /// Removes one key with the given ID from the inventory.
    /// Returns false if no matching key was found.
    /// Raises <see cref="OnKeyInventoryChangedEvent"/> on success.
    /// </summary>
    public bool ConsumeKey(string keyId)
    {
        int index = keys.FindIndex(k => k.keyId == keyId);
        if (index < 0)
        {
            Debug.LogWarning($"[KeyInventory] Tried to consume key '{keyId}' but it is not held.");
            return false;
        }

        keys.RemoveAt(index);
        Save();
        EventBus.Raise(new OnKeyInventoryChangedEvent());
        return true;
    }

    // ─── Persistence ───────────────────────────────────────────────────────────

    private void Save()
    {
        if (!_initialized) return;

        var data = new SaveData();
        foreach (var k in keys)
            data.entries.Add(new SaveData.SaveEntry { keyId = k.keyId, displayName = k.displayName });

#if UNITY_EDITOR
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
#else
        File.WriteAllText(SavePath, JsonUtility.ToJson(data));
#endif
    }

    private void Load()
    {
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            if (data == null) return;

            keys.Clear();
            foreach (var entry in data.entries)
            {
                keys.Add(new KeyData
                {
                    keyId       = entry.keyId,
                    displayName = entry.displayName,
                    icon        = null   // Sprite cannot be serialized; stays null after load
                });
            }

            Debug.Log($"[KeyInventory] Loaded {keys.Count} key(s).");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[KeyInventory] Load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes all held keys without raising events. Used by SaveManager during restore
    /// before re-adding the persisted keys.
    /// </summary>
    public void Clear()
    {
        keys.Clear();
    }

    public static void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }
}
