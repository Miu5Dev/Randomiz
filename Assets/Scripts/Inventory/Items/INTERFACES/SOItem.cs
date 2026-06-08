using UnityEngine;

/// <summary>
/// Base ScriptableObject for any in-game item. Subclasses (SOWeapon, SOPotion,
/// SOMoney, SOSimpleItem…) implement Use(GameObject user) with type-specific
/// behavior. Filler items are used as a fallback in chests when no higher
/// tier is available.
/// </summary>
public abstract class SOItem : ScriptableObject
{
    [Header("General Info")]
    public string itemName = "Unnamed Item";
    public string itemDescription = "";
    public Sprite itemSprite;

    [Header("Economy")]
    [Tooltip("Base shop price before any NPC personality multiplier is applied.")]
    [Min(0)]
    public int baseValue = 10;

    [Header("Filler Settings")]
    [Tooltip("This item is used as a fallback when no higher tier is available.")]
    public bool isFiller = false;

    [Header("Prefab")]
    public GameObject prefab;

    [Header("Pivot Point")]
    public Transform PivotPoint;

    [Header("Weight")]
    [Min(0f)]
    public float handWeightMultiplier = 1f;

    /// <summary>Default description string — subclasses may override.</summary>
    public virtual string GetDescription()
    {
        return $"{itemName} (weight: {handWeightMultiplier}kg)";
    }

    public abstract void Use(GameObject user);
}
