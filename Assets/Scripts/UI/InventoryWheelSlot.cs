using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single slot widget in the inventory wheel. Shows an item icon and a highlight
/// ring when selected. Pooled across wheel opens — SetItem / SetHighlighted are
/// the hot calls and short-circuit when the value didn't change.
/// </summary>
public class InventoryWheelSlot : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private GameObject highlightObject;

    public SOItem Item { get; private set; }

    private bool _highlighted;

    public void SetItem(SOItem item)
    {
        if (Item == item) return; // already showing this item
        Item = item;
        if (iconImage == null) return;

        Sprite next = item != null ? item.itemSprite : null;
        if (next != null)
        {
            iconImage.sprite = next;
            if (!iconImage.enabled) iconImage.enabled = true;
        }
        else if (iconImage.enabled)
        {
            iconImage.enabled = false;
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        if (highlighted == _highlighted) return;
        _highlighted = highlighted;
        if (highlightObject != null) highlightObject.SetActive(highlighted);
    }
}
