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


    // ─────────────────────────────────────────────
    // CICLO DE VIDA
    // ─────────────────────────────────────────────

    private void Awake()
    {
        // Auto-asignar locationId desde el nombre del GameObject si está vacío
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

    // ─────────────────────────────────────────────
    // APERTURA
    // ─────────────────────────────────────────────

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

        var resolved = ResolveItem(item, opener);
        if (resolved == null)
        {
            Debug.LogWarning($"[Chest:{locationId}] Bloqueado — consigue primero el tier anterior.");
            return;
        }

        // Determinar qué ítem se va a entregar (resolved o un filler)
        SOItem actualItemGiven = resolved;
        
        if (actualItemGiven == null && pool.fillerItems.Count > 0)
        {
            actualItemGiven = pool.fillerItems[Random.Range(0, pool.fillerItems.Count)];
            Debug.Log($"[Chest:{locationId}] ⚠️ {resolved?.itemName} no se pudo usar, dando filler: {actualItemGiven.itemName}");
        }

        if (actualItemGiven == null)
        {
            Debug.LogError($"[Chest:{locationId}] ✗ CRÍTICO: No hay ningún ítem para entregar. Cofre no se abrió.");
            return;
        }

        // Solo notificar mediante evento, sin tocar el inventario
        EventBus.Raise(new OnItemPickedUpEvent(actualItemGiven, opener));
    
        // Marcar como abierto y cambiar visual
        State.SetOpened(locationId);
        SetVisualOpened();

        Debug.Log($"[Chest:{locationId}] {opener.name} recibió (vía evento): {actualItemGiven.itemName}" +
                  (actualItemGiven is SOWeapon w ? $" [Tier {w.tier}]" : ""));
    }

    // ─────────────────────────────────────────────
    // SEQUENCE BREAK PROTECTION
    // ─────────────────────────────────────────────

    private SOItem ResolveItem(SOItem intended, GameObject opener)
    {
        if (intended is not SOWeapon intendedWeapon)
            return intended;

        var inv = GetInventoryHandler(opener);
        int playerMaxTier = inv?.GetHighestWeaponTier() ?? 0;
        int intendedTier = intendedWeapon.tier;

        Debug.Log($"[Chest:{locationId}] Player maxTier={playerMaxTier}, cofre tiene T{intendedTier}");

        // Mismo tier o menor → filler
        if (intendedTier <= playerMaxTier)
        {
            Debug.Log($"[Chest:{locationId}] Jugador ya tiene T{playerMaxTier}. Dando filler.");
            return GetRandomFiller();
        }

        // Progresión normal: tier == maxTier + 1
        if (intendedTier == playerMaxTier + 1)
            return intended;

        // Sequence break: tier > maxTier + 1 → intentar swap
        int neededTier = playerMaxTier + 1;
        Debug.LogWarning($"[Chest:{locationId}] ⚠️ Sequence break — buscando T{neededTier}...");

        var swapped = TrySwapForNextTier(neededTier);   // ✅ ahora un solo argumento
        if (swapped != null)
            return swapped;

        // No hay tier disponible para swap → filler
        Debug.LogWarning($"[Chest:{locationId}] No hay T{neededTier} disponible. Dando filler.");
        return GetRandomFiller();
    }

    /// <summary>
    /// Devuelve un filler aleatorio del pool, o null si no hay.
    /// </summary>
    private SOItem GetRandomFiller()
    {
        if (pool.fillerItems.Count == 0)
        {
            Debug.LogError($"[Chest:{locationId}] ✗ No hay filler items. Cofre no se abrirá.");
            return null;
        }
        return pool.fillerItems[Random.Range(0, pool.fillerItems.Count)];
    }
    
    /// <summary>
    /// Busca y swap con el cofre que tiene el tier EXACTO que el player necesita (maxTier + 1).
    /// </summary>
    private SOWeapon TrySwapForNextTier(int neededTier)   // ← sin opener
    {
        var myState = State.GetChest(locationId);
        if (myState == null || myState.opened) return null;

        foreach (var other in State.chests)
        {
            if (other.locationId == locationId || other.opened) continue;

            var otherItem = pool.FindItem(other.itemName);
            if (otherItem is SOWeapon w && w.tier == neededTier)
            {
                // Intercambiar nombres
                string temp = myState.itemName;
                myState.itemName = other.itemName;
                other.itemName = temp;
                State.Save();

                return (SOWeapon)pool.FindItem(myState.itemName);
            }
        }
        return null;
    }
    /// <summary>
    /// Helper para obtener InventoryHandler con fallback a parent/child.
    /// </summary>
    private InventoryHandler GetInventoryHandler(GameObject opener)
    {
        return opener.GetComponent<InventoryHandler>()
            ?? opener.GetComponentInParent<InventoryHandler>()
            ?? opener.GetComponentInChildren<InventoryHandler>();
    }

    private SOWeapon SwapWithLowestPending(int neededTier)
    {
        var myState = State.GetChest(locationId);

        // Busca en el STATE global — funciona aunque el otro cofre esté en otra escena
        foreach (var other in State.chests)
        {
            if (other.locationId == locationId || other.opened) continue;
            var otherItem = pool.FindItem(other.itemName);
            if (otherItem is SOWeapon w && w.tier == neededTier)
            {
                string temp      = myState.itemName;
                myState.itemName = other.itemName;
                other.itemName   = temp;
                State.Save(); // persiste el swap

                var give = (SOWeapon)pool.FindItem(myState.itemName);
                Debug.Log($"[Chest] Swap: {locationId} ↔ {other.locationId} | Entrego {give.itemName}");
                return give;
            }
        }

        Debug.LogWarning($"[Chest:{locationId}] No hay T{neededTier} disponible. Cofre bloqueado.");
        return null;
    }

    // ─────────────────────────────────────────────
    // VISUAL (implementa con tu sistema de animación)
    // ─────────────────────────────────────────────

    private void SetVisualOpened()
    {
        // Ejemplo: GetComponent<Animator>()?.SetTrigger("Open");
        // Ejemplo: GetComponent<SpriteRenderer>().sprite = openedSprite;
    }

    // ─────────────────────────────────────────────
    // GIZMO — muestra el contenido en Scene View
    // ─────────────────────────────────────────────

    #if UNITY_EDITOR
        private void OnValidate()
        {
            // Auto-sincronizar locationId con el nombre del GameObject
            if (string.IsNullOrEmpty(locationId))
            {
                locationId = gameObject.name;
            }
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
