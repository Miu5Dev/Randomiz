using UnityEngine;

/// <summary>Raised when a potion is consumed; carries the consumed potion and the empty-potion replacement item.</summary>
public class OnPotionConsumeEvent
{
    public SOItem consumedPotionItem;
    public SOItem emptyPotionItem;

    public OnPotionConsumeEvent(SOItem consumedPotionItem,SOItem emptyPotionItem)
    {
        this.consumedPotionItem = consumedPotionItem;
        this.emptyPotionItem = emptyPotionItem;
    }
}