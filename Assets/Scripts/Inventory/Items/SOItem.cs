using UnityEngine;

[CreateAssetMenu(fileName = "Item", menuName = "Items/Item")]
public class SOItem : ScriptableObject
{
    [Header("General Info")]
    public string itemName = "Unnamed Item";
    public Sprite itemSprite;

    [Header("Weight")]
    [Min(0f)]
    public float handWeight = 0f;
}