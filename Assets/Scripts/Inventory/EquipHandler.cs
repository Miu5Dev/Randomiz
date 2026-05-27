using UnityEngine;

public class EquipHandler : MonoBehaviour
{
    public static EquipHandler Instance { get; private set; }

    public SOItem EquipedItem;
    
    [Space(10)]
    [Header("CONFIGS")]
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

    private void OnEnable()
    {
        EventBus.Subscribe<OnItemPickedUpEvent>(OnItemPickedUp);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnItemPickedUpEvent>(OnItemPickedUp);
    }

    private void Start()
    {
        // Opcional: comprobar que InventoryHandler existe
        if (InventoryHandler.Instance == null)
            Debug.LogError("InventoryHandler no encontrado. Asegúrate de que hay un InventoryHandler en la escena.");

        if(EquipedItem != null)EquipItem(EquipedItem);
    }

    private void OnItemPickedUp(OnItemPickedUpEvent e)
    {
        // Si recogimos una espada (tier replacement reemplazó la del slot 0) y EquipedItem
        // sigue apuntando a la espada antigua, sincronizamos a la nueva.
        if (e.item is SOSword && InventoryHandler.Instance != null)
        {
            SOItem slot0 = InventoryHandler.Instance.GetItem(0);
            if (slot0 != null && slot0 != EquipedItem)
            {
                // Si el jugador estaba en la espada (o no tenía nada) → auto-equipa la nueva.
                // Si tenía otra cosa equipada (poción, etc.) → no interrumpimos su selección.
                if (EquipedItem == null || EquipedItem is SOSword)
                    EquipItem(slot0);
            }
        }
    }
    
    public void EquipItem(SOItem item)
    {
        if (item == null) return;

        SOItem prev = EquipedItem;
        // Set FIRST, raise AFTER — así cualquier handler que lea EquipedItem ve el estado nuevo.
        EquipedItem = item;
        item.PivotPoint = ItemsPivotPoint;

        if (prev != null && prev != item)
        {
            EventBus.Raise(new OnItemUnequipEvent() { item = prev });
        }

        EventBus.Raise(new OnItemEquipEvent() { item = item });
    }

    public void UnEquipItem()
    {
        SOItem prev = EquipedItem;
        SOItem sword = InventoryHandler.Instance != null ? InventoryHandler.Instance.GetItem(0) : null;

        if (prev == sword)
        {
            // No hay cambio real de estado, pero re-emitimos OnItemEquipEvent para que
            // cualquier listener (HUD, animaciones, etc.) pueda resincronizarse — protege
            // contra desincronizaciones causadas por pickups que reemplazan el slot 0
            // o cualquier otra modificación lateral.
            if (sword != null)
                EventBus.Raise(new OnItemEquipEvent() { item = sword });
            return;
        }

        // Set FIRST, raise AFTER.
        EquipedItem = sword;
        if (sword != null) sword.PivotPoint = ItemsPivotPoint;

        if (prev != null)
        {
            EventBus.Raise(new OnItemUnequipEvent() { item = prev });
        }

        if (sword != null)
        {
            EventBus.Raise(new OnItemEquipEvent() { item = sword });
        }
    }

    public void UseItem()
    {
        if (EquipedItem is SOPotion)
        {
            SOItem emptyBottle = InventoryHandler.Instance?.defaultBottle;
            if (emptyBottle == null)
            {
                Debug.LogError("EquipHandler: InventoryHandler.defaultBottle no asignado.");
                return;
            }

            // No consumir si es la botella vacía
            if (EquipedItem == emptyBottle) return;

            SOItem consumed = EquipedItem;
            consumed.Use(this.gameObject);
            EventBus.Raise(new OnPotionConsumeEvent(consumed, emptyBottle));

            // Pasar por EquipItem para que se emitan los eventos Unequip/Equip
            // y se sincronicen HUD, quickslots, etc.
            EquipItem(emptyBottle);
        }
        else if (EquipedItem is SOWeapon)
        {
            EquipedItem.Use(this.gameObject);
        }
    }
}