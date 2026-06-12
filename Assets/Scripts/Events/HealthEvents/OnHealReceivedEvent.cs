using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Raised when a <c>Target</c> receives healing of <c>Amount</c> hit points.</summary>
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
