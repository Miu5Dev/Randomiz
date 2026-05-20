using UnityEngine;
using System.Linq;

public class InventoryHandler : MonoBehaviour
{
    // Singleton
    public static InventoryHandler Instance { get; private set; }

    [SerializeField] private SOItem[] invItems = new SOItem[13];
    [SerializeField] public SOItem defaultBottle; // Arrastrar la misma del EquipHandler

    public SOItem[] InvItems => invItems;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // DontDestroyOnLoad(gameObject); // Opcional
    }

    public SOItem GetItem(int index) => invItems[index];

    public void OnItemPickedUp(OnItemPickedUpEvent e)
    {
        if (e.receiver != gameObject) return;
        // Solo logging o efectos visuales
        Debug.Log($"{e.receiver.name} picked up {e.item.name}");
        // O actualizar UI, etc.
    }

    public void OnPotionConsume(OnPotionConsumeEvent e)
    {
        ConsumeItem(e.consumedPotionItem, e.emptyPotionItem);
    }

    public SOItem AddItem(SOItem item)
    {
        if (item == null) return null;

        // Lógica para pociones (SOPotion)
        if (item is SOPotion)
        {
            // 1. Si hay una botella vacía (defaultBottle), la reemplazamos directamente
            int emptyIndex = FindDefaultBottleIndex();
            if (emptyIndex != -1)
            {
                invItems[emptyIndex] = item;
                Debug.Log($"Inventory: Botella vacía reemplazada por {item.name} en slot {emptyIndex}");
                return item;
            }

            // 2. No hay vacía: contar solo pociones llenas (excluyendo la defaultBottle)
            int fullPotions = CountFullPotions();
            if (fullPotions >= 2)
            {
                Debug.Log($"Inventory: Ya hay {fullPotions} pociones llenas. No se añade {item.name}.");
                return null;
            }

            // 3. Añadir en primer slot libre
            int freeSlot = GetFirstEmptySlot();
            if (freeSlot != -1)
            {
                invItems[freeSlot] = item;
                Debug.Log($"Inventory: Añadida poción {item.name} en slot {freeSlot}");
                return item;
            }

            Debug.LogWarning("Inventory: No hay espacio para la poción.");
            return null;
        }

        // Lógica para espadas (slot 0)
        if (item is SOSword newSword)
        {
            if (invItems[0] is SOSword existingSword)
            {
                if (newSword.tier > existingSword.tier)
                {
                    invItems[0] = newSword;
                    return newSword;
                }
                return null;
            }
            invItems[0] = newSword;
            return newSword;
        }

        // Lógica para otras armas
        if (item is SOWeapon newWeapon)
        {
            for (int i = 1; i < invItems.Length; i++)
            {
                if (invItems[i] != null && invItems[i].GetType() == newWeapon.GetType())
                {
                    if (newWeapon.tier > ((SOWeapon)invItems[i]).tier)
                    {
                        invItems[i] = newWeapon;
                        return newWeapon;
                    }
                    return null;
                }
            }
        }

        // Items normales (no armas, no pociones)
        int firstEmpty = GetFirstEmptySlot();
        if (firstEmpty != -1)
        {
            invItems[firstEmpty] = item;
            return item;
        }

        Debug.LogWarning("Inventory full, cannot add item");
        return null;
    }

    private void ConsumeItem(SOItem item, SOItem consumedItem)
    {
        int index = GetIndex(item);
        if (index != -1)
            invItems[index] = consumedItem;
    }

    private int GetIndex(SOItem item)
    {
        for (int i = 0; i < invItems.Length; i++)
            if (invItems[i] == item) return i;
        return -1;
    }

    public int GetHighestWeaponTier()
    {
        if (invItems[0] is SOWeapon sword)
            return sword.tier;

        int max = 0;
        for (int i = 1; i < invItems.Length; i++)
            if (invItems[i] is SOWeapon w && w.tier > max)
                max = w.tier;
        return max;
    }

    // ─── HELPERS PARA POCIONES ─────────────────────────────
    private int CountTotalBottles()
    {
        int count = 0;
        for (int i = 1; i < invItems.Length; i++)
            if (invItems[i] != null && invItems[i] is SOPotion)
                count++;
        return count;
    }

    private int FindDefaultBottleIndex()
    {
        for (int i = 1; i < invItems.Length; i++)
            if (invItems[i] != null && invItems[i] == defaultBottle)
                return i;
        return -1;
    }

    private int GetFirstEmptySlot()
    {
        for (int i = 1; i < invItems.Length; i++)
            if (invItems[i] == null) return i;
        return -1;
    }
    
    private int CountFullPotions()
    {
        int count = 0;
        for (int i = 1; i < invItems.Length; i++)
            if (invItems[i] is SOPotion && invItems[i] != defaultBottle)
                count++;
        return count;
    }
}