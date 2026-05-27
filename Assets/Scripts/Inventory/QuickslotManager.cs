using UnityEngine;

/// <summary>
/// Estado de los 2 quickslots (item1 / item2) y manejo de su input.
/// - Si la rueda está cerrada: pulsar item1/2 equipa el item asignado.
///   Pulsarla con el item ya equipado → desequipa (vuelve a la espada).
/// - Si la rueda está abierta: la rueda intercepta el input (cancela el evento)
///   y emite OnQuickslotAssignedEvent que actualiza el estado aquí.
/// - Pulsar interactuar con un item equipado (que no sea la espada ni null) → desequipa.
/// </summary>
public class QuickslotManager : MonoBehaviour
{
    public static QuickslotManager Instance { get; private set; }

    public SOItem Slot1 { get; private set; }
    public SOItem Slot2 { get; private set; }

    private bool wheelOpen;

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
        EventBus.Subscribe<OnItemOneInputEvent>(OnItemOne);
        EventBus.Subscribe<OnItemTwoInputEvent>(OnItemTwo);
        EventBus.Subscribe<OnInteractDodgeInputEvent>(OnInteract);
        EventBus.Subscribe<OnInventoryWheelStateEvent>(OnWheelState);
        // Prioridad negativa para correr DESPUÉS de InventoryHandler.OnPotionConsume,
        // que actualiza el array de inventario en respuesta al mismo evento.
        EventBus.Subscribe<OnPotionConsumeEvent>(OnPotionConsume, -10);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnItemOneInputEvent>(OnItemOne);
        EventBus.Unsubscribe<OnItemTwoInputEvent>(OnItemTwo);
        EventBus.Unsubscribe<OnInteractDodgeInputEvent>(OnInteract);
        EventBus.Unsubscribe<OnInventoryWheelStateEvent>(OnWheelState);
        EventBus.Unsubscribe<OnPotionConsumeEvent>(OnPotionConsume);
    }

    private void OnPotionConsume(OnPotionConsumeEvent e)
    {
        // En este punto el InventoryHandler ya cambió UNA instancia de la poción consumida
        // por la botella vacía. Calculamos cuántas pociones (iguales) quedan en inventario
        // y cuántos quickslots la referencian: vaciamos solo (quickslots - restantes) slots.
        int remainingInInventory = CountInInventory(e.consumedPotionItem);
        int inQuickslots = (Slot1 == e.consumedPotionItem ? 1 : 0)
                         + (Slot2 == e.consumedPotionItem ? 1 : 0);
        int toReplace = inQuickslots - remainingInInventory;
        if (toReplace <= 0) return; // hay suficientes pociones, los quickslots siguen válidos

        if (toReplace > 0 && Slot1 == e.consumedPotionItem)
        {
            Slot1 = e.emptyPotionItem;
            EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 1, item = Slot1 });
            toReplace--;
        }
        if (toReplace > 0 && Slot2 == e.consumedPotionItem)
        {
            Slot2 = e.emptyPotionItem;
            EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 2, item = Slot2 });
        }
    }

    private int CountInInventory(SOItem item)
    {
        if (item == null || InventoryHandler.Instance == null) return 0;
        SOItem[] inv = InventoryHandler.Instance.InvItems;
        if (inv == null) return 0;

        int count = 0;
        for (int i = 0; i < inv.Length; i++)
            if (inv[i] == item) count++;
        return count;
    }

    private void OnWheelState(OnInventoryWheelStateEvent e) => wheelOpen = e.open;

    /// <summary>
    /// Asigna un item a un quickslot (1 o 2). Si el item ya está en el OTRO slot
    /// Y el inventario sólo tiene una instancia de ese item, se hace swap
    /// (el item del slot destino pasa al slot origen para evitar duplicados imposibles).
    /// Si el inventario tiene ≥2 instancias (típico de pociones), se permite duplicar.
    /// Llamado directamente desde InventoryWheelUI (no por evento, para evitar recursión).
    /// Emite OnQuickslotAssignedEvent por cada slot que cambia.
    /// </summary>
    public void AssignToSlot(int slotIndex, SOItem item)
    {
        if (slotIndex != 1 && slotIndex != 2) return;

        bool canDuplicate = HasMultipleInventoryInstances(item);

        if (slotIndex == 1)
        {
            if (item == Slot1) return; // no hay cambio

            // Swap sólo si NO podemos duplicar y el item ya está en el otro slot
            if (!canDuplicate && item != null && item == Slot2)
            {
                SOItem prevSlot1 = Slot1;
                Slot2 = prevSlot1;
                EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 2, item = Slot2 });
            }

            Slot1 = item;
            EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 1, item = Slot1 });
        }
        else // slotIndex == 2
        {
            if (item == Slot2) return;

            if (!canDuplicate && item != null && item == Slot1)
            {
                SOItem prevSlot2 = Slot2;
                Slot1 = prevSlot2;
                EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 1, item = Slot1 });
            }

            Slot2 = item;
            EventBus.Raise(new OnQuickslotAssignedEvent { slotIndex = 2, item = Slot2 });
        }
    }

    /// <summary>
    /// True si el inventario contiene al menos 2 referencias del mismo SOItem
    /// (ej. dos pociones iguales). Para null o items sin duplicado, false.
    /// </summary>
    private bool HasMultipleInventoryInstances(SOItem item)
    {
        if (item == null) return false;
        if (InventoryHandler.Instance == null) return false;

        SOItem[] inv = InventoryHandler.Instance.InvItems;
        if (inv == null) return false;

        int count = 0;
        for (int i = 0; i < inv.Length; i++)
        {
            if (inv[i] == item)
            {
                count++;
                if (count >= 2) return true;
            }
        }
        return false;
    }

    private void OnItemOne(OnItemOneInputEvent e)
    {
        if (wheelOpen || !e.pressed) return;
        HandleQuickslotPress(Slot1);
        // Cancelamos para que ningún EventBusListener configurado por inspector
        // re-equipe encima y deshaga la unequip.
        EventBus.Cancel<OnItemOneInputEvent>();
    }

    private void OnItemTwo(OnItemTwoInputEvent e)
    {
        if (wheelOpen || !e.pressed) return;
        HandleQuickslotPress(Slot2);
        EventBus.Cancel<OnItemTwoInputEvent>();
    }

    private void HandleQuickslotPress(SOItem item)
    {
        if (item == null || EquipHandler.Instance == null) return;

        if (EquipHandler.Instance.EquipedItem == item)
            EquipHandler.Instance.UnEquipItem();
        else
            EquipHandler.Instance.EquipItem(item);
    }

    private void OnInteract(OnInteractDodgeInputEvent e)
    {
        if (wheelOpen || !e.pressed) return;
        if (EquipHandler.Instance == null) return;

        SOItem equipped = EquipHandler.Instance.EquipedItem;
        if (equipped == null) return;

        SOItem sword = InventoryHandler.Instance != null ? InventoryHandler.Instance.GetItem(0) : null;
        if (equipped == sword) return;

        EquipHandler.Instance.UnEquipItem();
    }
}
