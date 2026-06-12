using UnityEngine;

/// <summary>Consumable potion item; <see cref="Use"/> raises a heal event for <c>healAmount</c> hit points.</summary>
[CreateAssetMenu(fileName = "Potion", menuName = "Items/Potion")]
public class SOPotion : SOItem
{
    [Header("Potion Data")]
    public int healAmount;
    
    public override void Use(GameObject user)
    {
        EventBus.Raise(new OnHealReceivedEvent(healAmount, user));
    }
}