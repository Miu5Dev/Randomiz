using UnityEngine;
using UnityEngine.UIElements;

public class OnHealReceivedEvent
{
    public float Amount  { get; }
    public GameObject Target { get; }

    public OnHealReceivedEvent(float amount,GameObject target)
    {
        Amount  = amount;
        Target = target;
    }
}
