
    using UnityEngine;

    /// <summary>Raised when an entity picks up an item; carries the picked-up <c>item</c> and the <c>receiver</c> GameObject.</summary>
    public class OnItemPickedUpEvent
    {
            public SOItem item;
            public GameObject receiver;

            public OnItemPickedUpEvent(SOItem item, GameObject receiver)
            {
                this.item     = item;
                this.receiver = receiver;
            }
    }