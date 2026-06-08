using UnityEngine;

/// <summary>
/// A key item that lives in the SOItemPool and can appear in any chest.
/// When picked up through a chest it is NOT stored in the normal inventory —
/// InventoryHandler intercepts it and routes it to KeyInventory instead.
/// The player never "holds" it like a weapon or potion.
/// </summary>
[CreateAssetMenu(fileName = "NewKey", menuName = "Randomizer/Key")]
public class SOKey : SOItem
{
    [Header("Key")]
    [Tooltip("Unique identifier that must match the DoorController.requiredKeyId.")]
    public string keyId = "key_default";

    public override string GetDescription() => $"{itemName} — opens doors with ID '{keyId}'";

    public override void Use(GameObject user)
    {
        // Keys are passive: they sit in KeyInventory and are consumed by DoorController.
        // Nothing happens when "used" directly.
    }
}
