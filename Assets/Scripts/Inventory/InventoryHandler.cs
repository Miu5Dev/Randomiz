using System.Linq;
using UnityEngine;

public class InventoryHandler : MonoBehaviour
{
    
    [SerializeField] private SOItem[] invItems = new SOItem[13]; //make sure max is 13 even though inspector allows to edit. //SLOT 0 IS ALWAYS SWORD
    
    public SOItem[] InvItems { get { return invItems; } }

    public SOItem GetItem(int index)
    {
        return invItems[index];
    }

    public void OnItemPickedUp(OnItemPickedUpEvent e)
    {
        if (e.receiver != gameObject) return;
        AddItem(e.item);
    }
    

    public SOItem AddItem(SOItem item)
    {
        if (item == null)
        {
            Debug.LogWarning("InventoryHandler: Tried to add a null item.");
            return null;
        }

        if (item is SOSword newSword)
        {
            if (invItems[0] is SOSword existingSword)
            {
                if (newSword.tier > existingSword.tier)
                {
                    invItems[0] = newSword;
                    Debug.Log($"InventoryHandler: Sword '{newSword.itemName}' (tier {newSword.tier}) replaced '{existingSword.itemName}' (tier {existingSword.tier}) in slot 0.");
                    return newSword;
                }
                else
                {
                    Debug.LogWarning($"InventoryHandler: Sword '{newSword.itemName}' (tier {newSword.tier}) not added. Existing sword has equal or higher tier ({existingSword.tier}).");
                    return null;  // ← Fallback a filler
                }
            }
            else
            {
                invItems[0] = newSword;
                Debug.Log($"InventoryHandler: Sword '{newSword.itemName}' placed in slot 0.");
                return newSword;
            }
        }

        if (item is SOWeapon newWeapon)
        {
            for (int i = 1; i < invItems.Length; i++)
            {
                if (invItems[i] != null && invItems[i].GetType() == newWeapon.GetType())
                {
                    if (newWeapon.tier > ((SOWeapon)invItems[i]).tier)
                    {
                        invItems[i] = newWeapon;
                        Debug.Log($"InventoryHandler: '{newWeapon.itemName}' (tier {newWeapon.tier}) replaced existing in slot {i}.");
                        return newWeapon;
                    }
                    else
                    {
                        Debug.LogWarning($"InventoryHandler: '{newWeapon.itemName}' (tier {newWeapon.tier}) not added. Equal or higher tier already exists in slot {i}.");
                        return null;  // ← Fallback a filler
                    }
                }
            }
        }

        for (int i = 1; i < invItems.Length; i++)
        {
            if (invItems[i] == null)
            {
                invItems[i] = item;
                Debug.Log($"InventoryHandler: '{item.itemName}' added to slot {i}.");
                return item;
            }
        }

        Debug.LogWarning("InventoryHandler: Inventory is full. Could not add item.");
        return null;  // ← Fallback a filler
    }


    public void OnPotionConsume(OnPotionConsumeEvent e)
    {
        ConsumeItem(e.consumedPotionItem, e.emptyPotionItem);
    }
    
    private void ConsumeItem(SOItem item, SOItem consumedItem)
    {
        if (invItems.Contains(item))
        {
            invItems[GetIndex(item)] = consumedItem;
        }
    }
    
    private int GetIndex(SOItem item)
    {
        for (int i = 0; i < invItems.Length; i++)
        {
            if (invItems[i] == item)
                return i;
        }
        return -1;
    }
    
    // Añade esto en InventoryHandler.cs
    public int GetHighestWeaponTier()
    {
        // Slot 0 siempre es la espada — es suficiente para el tier check
        if (invItems[0] is SOWeapon sword)
            return sword.tier;

        // Por si acaso hay otras weapons en slots 1-12
        int max = 0;
        for (int i = 1; i < invItems.Length; i++)
            if (invItems[i] is SOWeapon w && w.tier > max)
                max = w.tier;

        return max;
    }
    
}
