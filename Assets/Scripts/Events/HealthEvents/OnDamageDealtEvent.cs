using UnityEngine;

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
