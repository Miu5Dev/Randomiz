
    using UnityEngine;

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