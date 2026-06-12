using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that tracks all keys the player is currently holding.
/// Separate from the main InventoryHandler — keys are not SOItems and do not
/// occupy inventory slots.
///
/// State is persisted exclusively through SaveManager (SaveData.heldKeyIds).
/// There is intentionally no independent JSON file here — a separate file
/// caused keys to leak across seeds/runs.
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

    [SerializeField] private List<KeyData> keys = new();

    /// <summary>Read-only view of the current key list.</summary>
    public List<KeyData> Keys => keys;

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
        EventBus.Raise(new OnKeyInventoryChangedEvent());
        return true;
    }

    /// <summary>
    /// Removes all held keys without raising events. Used by SaveManager during restore
    /// before re-adding the persisted keys.
    /// </summary>
    public void Clear()
    {
        keys.Clear();
    }
}
