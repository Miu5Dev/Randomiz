using UnityEngine;

/// <summary>Raised when an <c>Attacker</c> deals <c>Damage</c> to a <c>Target</c>; consumed by HealthSystem.</summary>
public class OnDamageDealtEvent
{
    public GameObject Attacker { get; }
    public GameObject Target   { get; }
    public float      Damage   { get; }

    public OnDamageDealtEvent(GameObject attacker, GameObject target, float damage)
    {
        Attacker = attacker;
        Target   = target;
        Damage   = damage;
    }
}
