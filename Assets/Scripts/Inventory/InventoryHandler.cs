using UnityEngine;

public class InventoryHandler : MonoBehaviour
{
    
    [SerializeField] private SOItem[] invItems = new SOItem[13]; //make sure max is 13 even though inspector allows to edit. //SLOT 0 IS ALWAYS SWORD
    
    public SOItem[] InvItems { get { return invItems; } }

    public SOItem GetItem(int index)
    {
        return invItems[index];
    }

    public bool AddItem(SOItem item)
    {
        if (item == null)
        {
            Debug.LogWarning("InventoryHandler: Tried to add a null item.");
            return false;
        }

        for (int i = 0; i < invItems.Length; i++)
        {
            if (invItems[i] == null)
            {
                invItems[i] = item;
                Debug.Log($"InventoryHandler: '{item.itemName}' added to slot {i}.");
                return true;
            }
        }

        Debug.LogWarning("InventoryHandler: Inventory is full. Could not add item.");
        return false;
    }
    
}
