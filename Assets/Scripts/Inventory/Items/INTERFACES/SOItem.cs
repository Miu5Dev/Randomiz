using UnityEngine;

public abstract class SOItem : ScriptableObject
{
    [Header("General Info")]
    public string itemName = "Unnamed Item";
    public string itemDescription = "";
    public Sprite itemSprite;
    
    [Header("Filler Settings")]
    [Tooltip("Este item se usa como fallback cuando no hay tier mayor disponible")]
    public bool isFiller = false;
    
    [Header("Prefab")]
    public GameObject prefab;
    
    [Header("Pivot Point")]
    public Transform PivotPoint;

    [Header("Weight")]
    [Min(0f)]
    public float handWeight = 0f;
    
    // Método virtual que los hijos pueden override
    public virtual string GetDescription()
    {
        return $"{itemName} (peso: {handWeight}kg)";
    }
    
    public abstract void Use(GameObject user);
    
}