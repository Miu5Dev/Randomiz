using UnityEngine;

/// <summary>
/// ScriptableObject definition for the Grappling Hook item.
/// Extends SOItem directly (not SOWeapon) because it is a utility tool
/// rather than a damage-dealing weapon — it occupies a quickslot like a potion.
/// </summary>
[CreateAssetMenu(fileName = "GrappleHook", menuName = "Items/GrappleHook")]
public class SOGrappleHook : SOItem
{
    [Header("Grapple Settings")]
    [Tooltip("Maximum reach of the hook (metres).")]
    public float ropeLength = 15f;

    [Tooltip("Speed at which the player is pulled toward the hook anchor (m/s).")]
    public float pullSpeed = 12f;

    [Tooltip("Lateral swing force applied each FixedUpdate while hooked.")]
    public float swingForce = 8f;

    [Tooltip("Layers the hook ray can attach to. Set this to include terrain, walls, and GrappleAnchor objects.")]
    public LayerMask hookableLayers;

    [Tooltip("Instantiated at the hit point to visualise the hook head.")]
    public GameObject hookPrefab;

    [Tooltip("Optional rope mesh/sprite prefab rendered alongside the LineRenderer.")]
    public GameObject ropePrefab;

    public override void Use(GameObject user)
    {
        // GrappleHookBehaviour drives all behaviour;
        // Use() is intentionally empty — the equip/input flow is handled
        // in the MonoBehaviour (hold/release pattern like the slingshot).
    }

    public override string GetDescription()
    {
        return $"Grappling Hook  |  Reach: {ropeLength} m  |  Pull: {pullSpeed} m/s";
    }
}
