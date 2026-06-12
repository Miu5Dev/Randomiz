using UnityEngine;

/// <summary>Base ScriptableObject for weapons; adds combat stats (damage, range, speed) and a progression <c>tier</c>.</summary>
public abstract class SOWeapon : SOItem
{
    [Header("Weapon Data")]
    public float damage;
    public float range;
    public float speed;

    [Space(10)]
    public int tier;




}