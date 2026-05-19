using UnityEngine;

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