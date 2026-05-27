using UnityEngine;
using UnityEngine.UI;

public class InventoryWheelSlot : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private GameObject highlightObject;

    public SOItem Item { get; private set; }

    public void SetItem(SOItem item)
    {
        Item = item;
        if (iconImage == null) return;

        if (item != null && item.itemSprite != null)
        {
            iconImage.sprite = item.itemSprite;
            iconImage.enabled = true;
        }
        else
        {
            iconImage.enabled = false;
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        if (highlightObject != null) highlightObject.SetActive(highlighted);
    }
}
