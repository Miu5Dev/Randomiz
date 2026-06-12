using UnityEngine;

/// <summary>Generic inventory item with no active effect (collectible / filler).</summary>
[CreateAssetMenu(fileName = "Item", menuName = "Items/Item")]
public class SOSimpleItem : SOItem
{
    
    public override void Use(GameObject user)
    {
        //Nothing
    }
}