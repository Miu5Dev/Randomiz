using UnityEngine;
using System.Collections.Generic;

public class ChestBehaviour : MonoBehaviour
{
    [Header("ID único — debe ser el mismo en todas las runs")]
    public string locationId;

    [Header("SOItems necesarios para acceder a este cofre")]
    public List<SOItem> requiredItems = new();

    [Header("Pool — necesario para resolver items desde el state")]
    [SerializeField] private SOItemPool pool;

    private RandomizerState State => pool.state;
    public bool IsOpened => pool?.state?.GetChest(locationId)?.opened ?? false;

    private void Awake()
    {
        if (string.IsNullOrEmpty(locationId))
        {
            locationId = gameObject.name;
            Debug.Log($"[Chest:{locationId}] Auto-asignado desde GameObject name");
        }

        if (string.IsNullOrEmpty(locationId))
        {
            Debug.LogError($"[Chest] {gameObject.name} no tiene locationId asignado.");
            return;
        }

        var s = State.GetChest(locationId);
        if (s == null)
        {
            Debug.LogWarning($"[Chest:{locationId}] No encontrado en el State. ¿Generaste la seed?");
            return;
        }

        if (s.opened)
            SetVisualOpened();
    }

    public void Open(GameObject opener)
    {
        var s = State.GetChest(locationId);
        if (s == null || s.opened) return;

        var item = pool.FindItem(s.itemName);
        if (item == null)
        {
            Debug.LogError($"[Chest:{locationId}] Item '{s.itemName}' no encontrado en el pool.");
            return;
        }

        var resolved = ResolveItem(item);
        if (resolved == null)
        {
            Debug.LogWarning($"[Chest:{locationId}] Bloqueado — consigue primero el tier anterior.");
            return;
        }

        // Intentar añadir el ítem resuelto
        SOItem addedItem = InventoryHandler.Instance?.AddItem(resolved);

        // Si falla, probar todos los fillers (mezclados)
        if (addedItem == null && pool.fillerItems.Count > 0)
        {
            List<SOItem> shuffled = new List<SOItem>(pool.fillerItems);
            for (int i = 0; i < shuffled.Count; i++)
            {
                int rand = Random.Range(i, shuffled.Count);
                (shuffled[i], shuffled[rand]) = (shuffled[rand], shuffled[i]);
            }

            foreach (var filler in shuffled)
            {
                addedItem = InventoryHandler.Instance?.AddItem(filler);
                if (addedItem != null)
                {
                    Debug.Log($"[Chest:{locationId}] ⚠️ {resolved.itemName} no se pudo agregar. Se dio filler: {filler.itemName}");
                    break;
                }
            }
        }

        if (addedItem == null)
        {
            Debug.LogError($"[Chest:{locationId}] ✗ CRÍTICO: No se pudo añadir ningún ítem (ni siquiera filler). Cofre permanece cerrado.");
            return;
        }

        EventBus.Raise(new OnItemPickedUpEvent(addedItem, opener));
        State.SetOpened(locationId);
        SetVisualOpened();

        Debug.Log($"[Chest:{locationId}] {opener.name} obtuvo: {addedItem.itemName}" +
                  (addedItem is SOWeapon w ? $" [Tier {w.tier}]" : ""));
    }

    private SOItem ResolveItem(SOItem intended)
    {
        if (intended is not SOWeapon intendedWeapon)
            return intended;

        int playerMaxTier = InventoryHandler.Instance?.GetHighestWeaponTier() ?? 0;
        int intendedTier = intendedWeapon.tier;

        Debug.Log($"[Chest:{locationId}] Player maxTier={playerMaxTier}, cofre tiene T{intendedTier}");

        // Mismo tier o menor → filler
        if (intendedTier <= playerMaxTier)
        {
            Debug.Log($"[Chest:{locationId}] Jugador ya tiene T{playerMaxTier}. Dando filler.");
            return GetRandomFiller();
        }

        // Progresión normal
        if (intendedTier == playerMaxTier + 1)
            return intended;

        // Sequence break → intentar swap
        int neededTier = playerMaxTier + 1;
        Debug.LogWarning($"[Chest:{locationId}] Sequence break — buscando T{neededTier}...");
        var swapped = TrySwapForNextTier(neededTier);
        if (swapped != null)
            return swapped;

        // No hay swap → filler
        Debug.LogWarning($"[Chest:{locationId}] No hay T{neededTier} disponible. Dando filler.");
        return GetRandomFiller();
    }

    private SOItem GetRandomFiller()
    {
        if (pool.fillerItems.Count == 0)
        {
            Debug.LogError($"[Chest:{locationId}] ✗ No hay filler items configurados.");
            return null;
        }
        return pool.fillerItems[Random.Range(0, pool.fillerItems.Count)];
    }

    private SOWeapon TrySwapForNextTier(int neededTier)
    {
        var myState = State.GetChest(locationId);
        if (myState == null || myState.opened) return null;

        foreach (var other in State.chests)
        {
            if (other.locationId == locationId || other.opened) continue;

            var otherItem = pool.FindItem(other.itemName);
            if (otherItem is SOWeapon w && w.tier == neededTier)
            {
                string temp = myState.itemName;
                myState.itemName = other.itemName;
                other.itemName = temp;
                State.Save();
                return (SOWeapon)pool.FindItem(myState.itemName);
            }
        }
        return null;
    }

    private void SetVisualOpened()
    {
        // Implementar animación/sprite
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(locationId))
            locationId = gameObject.name;
    }

    private void OnDrawGizmosSelected()
    {
        var s = pool?.state?.GetChest(locationId);
        string label;
        if (s == null)
            label = "[ sin state ]";
        else if (s.opened)
            label = "✓ abierto";
        else if (!string.IsNullOrEmpty(s.itemName))
        {
            var item = pool.FindItem(s.itemName);
            label = s.itemName + (item is SOWeapon w ? $" [T{w.tier}]" : "");
        }
        else
            label = "[ vacío ]";
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, label);
    }
#endif
}