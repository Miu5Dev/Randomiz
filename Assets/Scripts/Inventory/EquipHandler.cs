using UnityEngine;

public class EquipHandler : MonoBehaviour
{
    public static EquipHandler Instance { get; private set; }

    public SOItem EquipedItem;
    
    [Space(10)]
    [Header("CONFIGS")]
    // ❌ Ya no necesitas este campo: public SOItem defaultBottle;
    public Transform ItemsPivotPoint;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Opcional: comprobar que InventoryHandler existe
        if (InventoryHandler.Instance == null)
            Debug.LogError("InventoryHandler no encontrado. Asegúrate de que hay un InventoryHandler en la escena.");
    }

    public void EquipItem(SOItem item)
    {
        if (EquipedItem != null && EquipedItem != item)
            EquipedItem = item;
        item.PivotPoint = ItemsPivotPoint;
    }

    public void UnEquipItem()
    {
        EquipedItem = null;
    }

    public void UseItem()
    {
        if (EquipedItem is SOPotion)
        {
            // Obtener la botella vacía desde el inventario
            SOItem emptyBottle = InventoryHandler.Instance?.defaultBottle;
            if (emptyBottle == null)
            {
                Debug.LogError("EquipHandler: InventoryHandler.defaultBottle no asignado.");
                return;
            }

            // No consumir si es la botella vacía
            if (EquipedItem == emptyBottle) return;

            EquipedItem.Use(this.gameObject);
            EventBus.Raise(new OnPotionConsumeEvent(EquipedItem, emptyBottle));
            EquipedItem = emptyBottle;
        }
        else if (EquipedItem is SOWeapon)
        {
            EquipedItem.Use(this.gameObject);
        }
    }
}