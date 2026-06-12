using UnityEngine;

/// <summary>Currency item (coins). Has no active use; the inventory grants its <c>MoneyAmmount</c> on pickup.</summary>
[CreateAssetMenu(fileName = "Money", menuName = "Items/Money")]
public class SOMoney : SOItem
{
    [Header("Money Data")] public int MoneyAmmount;
    
    public override void Use(GameObject user)
    {
        //no use its just money LOL
    }
}