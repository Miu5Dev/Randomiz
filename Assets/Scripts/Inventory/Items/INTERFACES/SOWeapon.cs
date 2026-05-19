using UnityEngine;

public abstract class SOWeapon : SOItem
{
    [Header("Weapon Data")]
    public float damage;
    public float range;
    public float speed;

    [Space(10)]
    public int tier;




}