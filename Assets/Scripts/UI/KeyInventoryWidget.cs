using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD/inventory-panel widget that displays all keys currently held by the player.
/// Attach to a panel inside the inventory wheel or a dedicated HUD panel.
///
/// Subscribe to <see cref="OnKeyInventoryChangedEvent"/> and rebuild the list on
/// every change. Each row is an instance of <see cref="keyRowPrefab"/> containing
/// an Image (icon) and a TMP_Text (display name). The prefab should have a child
/// named "Icon" (Image) and a child named "Label" (TMP_Text); names are resolved
/// by GetComponentInChildren if the named approach fails.
/// </summary>
public class KeyInventoryWidget : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Parent transform that holds the instantiated key rows (use a VerticalLayoutGroup).")]
    [SerializeField] private Transform keyListParent;

    [Tooltip("Prefab for a single key row. Must contain an Image and a TMP_Text child.")]
    [SerializeField] private GameObject keyRowPrefab;

    // Pool of active row GameObjects so we can clear and rebuild cheaply.
    private readonly List<GameObject> _activeRows = new();

    // ─── Unity lifecycle ───────────────────────────────────────────────────────

    private void OnEnable()
    {
        EventBus.Subscribe<OnKeyInventoryChangedEvent>(OnKeyInventoryChanged);
        Refresh(); // Draw the current state on enable.
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnKeyInventoryChangedEvent>(OnKeyInventoryChanged);
    }

    // ─── Event handler ─────────────────────────────────────────────────────────

    private void OnKeyInventoryChanged(OnKeyInventoryChangedEvent _) => Refresh();

    // ─── Rebuild ───────────────────────────────────────────────────────────────

    /// <summary>Destroys all existing rows and rebuilds them from KeyInventory.</summary>
    private void Refresh()
    {
        // Clear existing rows.
        foreach (var row in _activeRows)
        {
            if (row != null) Destroy(row);
        }
        _activeRows.Clear();

        if (KeyInventory.Instance == null || keyRowPrefab == null || keyListParent == null)
            return;

        foreach (var keyData in KeyInventory.Instance.Keys)
        {
            GameObject row = Instantiate(keyRowPrefab, keyListParent);
            _activeRows.Add(row);

            // Set icon — look for an Image child named "Icon" first, then any Image.
            var iconTransform = row.transform.Find("Icon");
            Image iconImage   = iconTransform != null
                ? iconTransform.GetComponent<Image>()
                : row.GetComponentInChildren<Image>();

            if (iconImage != null)
            {
                iconImage.sprite  = keyData.icon;
                iconImage.enabled = keyData.icon != null;
            }

            // Set label — look for a TMP_Text child named "Label" first, then any.
            var labelTransform = row.transform.Find("Label");
            TMP_Text label     = labelTransform != null
                ? labelTransform.GetComponent<TMP_Text>()
                : row.GetComponentInChildren<TMP_Text>();

            if (label != null)
                label.text = keyData.displayName;
        }
    }
}
