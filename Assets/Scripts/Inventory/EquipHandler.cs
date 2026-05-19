using UnityEngine;

public class EquipHandler : MonoBehaviour
{
    public SOItem EquipedItem;
    
    [Space(10)]
    [Header("CONFIGS")]
    public SOItem defaultBottle;
    public Transform ItemsPivotPoint;

    public void EquipItem(SOItem item)
    {
        if(EquipedItem != null && EquipedItem != item)
        EquipedItem = item;
        item.PivotPoint = ItemsPivotPoint;
    }

    public void UnEquipItem()
    {
        EquipedItem = null;
    }

    public void UseItem()
    {
        if (EquipedItem is SOPotion)
        {
            if(EquipedItem.name == defaultBottle.name) return;
            EquipedItem.Use(this.gameObject);
            EventBus.Raise(new OnPotionConsumeEvent(EquipedItem,defaultBottle));
            EquipedItem = defaultBottle;
        }
        else
        if(EquipedItem is SOWeapon)
        {
            EquipedItem.Use(this.gameObject);
        }
        
        
    }
}
