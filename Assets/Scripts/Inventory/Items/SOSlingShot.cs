using UnityEngine;

/// <summary>
/// ScriptableObject definition for the Slingshot weapon.
/// Extends SOWeapon so it occupies a weapon slot and participates in
/// tier-based chest randomization like any other ranged weapon.
/// </summary>
[CreateAssetMenu(fileName = "Slingshot", menuName = "Items/Slingshot")]
public class SOSlingShot : SOWeapon
{
    [Header("Slingshot Settings")]
    [Tooltip("Initial speed (m/s) applied to the stone projectile on release.")]
    public float projectileSpeed = 20f;

    [Tooltip("Stone prefab with a Rigidbody — spawned at firePoint on release.")]
    public GameObject stonePrefab;

    [Tooltip("Field of view while aiming (zoomed-in state). Default camera FOV is restored on unequip.")]
    public float aimFOV = 40f;

    public override void Use(GameObject user)
    {
        // The SlingshotBehaviour MonoBehaviour drives the fire logic;
        // Use() is left as a no-op here because hold/release is handled
        // in MonoBehaviour Update — EquipHandler.UseItem() still routes to this
        // but the behaviour component intercepts input before it reaches here.
    }

    public override string GetDescription()
    {
        return $"Slingshot  |  Damage: {damage}  |  Speed: {projectileSpeed} m/s";
    }
}
