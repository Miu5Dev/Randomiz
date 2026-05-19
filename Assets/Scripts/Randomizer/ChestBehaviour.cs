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

    // ─────────────────────────────────────────────
    // CICLO DE VIDA
    // ─────────────────────────────────────────────

    private void Awake()
    {
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
            Debug.LogWarning($"[Chest:{locationId}] Item '{s.itemName}' no encontrado en el pool.");
            return;
        }

        var resolved = ResolveItem(item, opener);
        if (resolved == null)
        {
            Debug.LogWarning($"[Chest:{locationId}] Bloqueado — consigue primero el tier anterior.");
            return;
        }

        State.SetOpened(locationId);  // auto-save incluido
        SetVisualOpened();
        EventBus.Raise(new OnItemPickedUpEvent(resolved, opener));

        Debug.Log($"[Chest:{locationId}] {opener.name} obtuvo: {resolved.itemName}" +
                  (resolved is SOWeapon w ? $" [Tier {w.tier}]" : ""));
    }

    // ─────────────────────────────────────────────
    // SEQUENCE BREAK PROTECTION
    // ─────────────────────────────────────────────

    private SOItem ResolveItem(SOItem intended, GameObject opener)
    {
        if (intended is not SOWeapon intendedWeapon)
            return intended; // pociones, llaves → siempre se entregan

        int playerMaxTier = opener.GetComponent<InventoryHandler>()?.GetHighestWeaponTier() ?? 0;
        int intendedTier  = intendedWeapon.tier;

        if (intendedTier <= playerMaxTier + 1)
            return intended;

        int neededTier = playerMaxTier + 1;
        Debug.LogWarning(
            $"[Chest:{locationId}] Sequence break — " +
            $"jugador tiene maxTier={playerMaxTier}, cofre tiene T{intendedTier}. " +
            $"Buscando T{neededTier}...");

        return SwapWithLowestPending(neededTier);
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
