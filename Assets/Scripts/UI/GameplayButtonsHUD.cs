using UnityEngine;

/// <summary>
/// Cruz de 4 botones en el HUD (arriba a la derecha):
///   Norte = item1   Este = item2   Oeste = item equipado (atacar)   Sur = acción/dash
///
/// Iconos: dirigidos por eventos (Equip/Unequip/QuickslotAssigned/PotionConsume).
/// Labels:
///   - Oeste: "Atacar" (SOWeapon) / "Usar" (SOPotion) / vacío.
///   - Norte/Este: "Equipar" si hay item y no está equipado, "Guardar" si lo está, vacío si null.
///   - Sur (polled): "Interactuar" si hay interactuable cerca, "Pegarse" si nearWall y no wallhugging, si no "Dash".
/// </summary>
public class GameplayButtonsHUD : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private HUDButtonWidget northButton; // item1
    [SerializeField] private HUDButtonWidget eastButton;  // item2
    [SerializeField] private HUDButtonWidget westButton;  // atacar (equipped)
    [SerializeField] private HUDButtonWidget southButton; // acción

    [Header("Player refs (opcional, si null se buscan automáticamente)")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private Interactor interactor;

    [Header("Labels")]
    [SerializeField] private string labelAttack    = "Attack";
    [SerializeField] private string labelUse       = "Use";
    [SerializeField] private string labelEquip     = "Equip";
    [SerializeField] private string labelStore     = "Store";
    [SerializeField] private string labelDash      = "Dash";
    [SerializeField] private string labelWallhug   = "Wall hug";
    [SerializeField] private string labelInteract  = "Interact";
    [SerializeField] private string labelClimb     = "Climb";

    private string lastSouthLabel = "";

    private void OnEnable()
    {
        EventBus.Subscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Subscribe<OnItemUnequipEvent>(OnItemUnequip);
        EventBus.Subscribe<OnQuickslotAssignedEvent>(OnQuickslotAssigned);
        EventBus.Subscribe<OnPotionConsumeEvent>(OnPotionConsume);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Unsubscribe<OnItemUnequipEvent>(OnItemUnequip);
        EventBus.Unsubscribe<OnQuickslotAssignedEvent>(OnQuickslotAssigned);
        EventBus.Unsubscribe<OnPotionConsumeEvent>(OnPotionConsume);
    }

    private void Start()
    {
        if (playerMovement == null) playerMovement = FindObjectOfType<PlayerMovement>();
        if (interactor == null)     interactor     = FindObjectOfType<Interactor>();

        RefreshAll();
    }

    private void Update()
    {
        // South label es contextual — se evalúa cada frame (es solo lectura de bools).
        UpdateSouth();
    }

    // ─── Event handlers ─────────────────────────────────────────────────────
    // Cualquier evento que pueda cambiar el estado (equipado / contenido de quickslots /
    // poción consumida) dispara un RefreshAllButtons. Así si dos slots comparten
    // referencia con el item equipado, ambos reciben el label correcto en el mismo
    // refresh — y nunca se queda un botón con label/icono stale por un evento parcial.

    private void OnItemEquip(OnItemEquipEvent e)         => RefreshAllButtons();
    private void OnItemUnequip(OnItemUnequipEvent e)     => RefreshAllButtons();
    private void OnPotionConsume(OnPotionConsumeEvent e) => RefreshAllButtons();
    private void OnQuickslotAssigned(OnQuickslotAssignedEvent e) => RefreshAllButtons();

    // ─── Refresh helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Lee el estado autoritativo (EquipHandler.EquipedItem + QuickslotManager.Slot1/Slot2)
    /// y actualiza los 3 botones (icono + label). Idempotente; barato (sólo set sprites/text).
    /// Garantiza que slots con referencia compartida muestren el mismo label.
    /// </summary>
    private void RefreshAllButtons()
    {
        SOItem equipped = EquipHandler.Instance != null ? EquipHandler.Instance.EquipedItem : null;
        SOItem slot1 = QuickslotManager.Instance != null ? QuickslotManager.Instance.Slot1 : null;
        SOItem slot2 = QuickslotManager.Instance != null ? QuickslotManager.Instance.Slot2 : null;

        UpdateWest(equipped);
        UpdateNorth(slot1);
        UpdateEast(slot2);
    }

    private void RefreshAll()
    {
        RefreshAllButtons();
        UpdateSouth();
    }

    // ─── Per-button updates ─────────────────────────────────────────────────

    private void UpdateNorth(SOItem item)
    {
        if (northButton == null) return;
        northButton.SetIcon(item != null ? item.itemSprite : null);
        northButton.SetLabel(GetQuickslotLabel(item));
    }

    private void UpdateEast(SOItem item)
    {
        if (eastButton == null) return;
        eastButton.SetIcon(item != null ? item.itemSprite : null);
        eastButton.SetLabel(GetQuickslotLabel(item));
    }

    private void UpdateWest(SOItem item)
    {
        if (westButton == null) return;
        westButton.SetIcon(item != null ? item.itemSprite : null);
        westButton.SetLabel(GetWestLabel(item));
    }

    private void UpdateSouth()
    {
        if (southButton == null) return;
        string label = GetSouthLabel();
        if (label == lastSouthLabel) return;
        southButton.SetLabel(label);
        lastSouthLabel = label;
    }

    // ─── Label rules ────────────────────────────────────────────────────────

    private string GetWestLabel(SOItem equipped)
    {
        if (equipped == null) return string.Empty;
        if (equipped is SOWeapon) return labelAttack;
        if (equipped is SOPotion) return labelUse;
        return string.Empty;
    }

    private string GetQuickslotLabel(SOItem slotItem)
    {
        if (slotItem == null) return string.Empty;

        SOItem equipped = EquipHandler.Instance != null ? EquipHandler.Instance.EquipedItem : null;
        return (equipped == slotItem) ? labelStore : labelEquip;
    }

    private string GetSouthLabel()
    {
        if (playerMovement != null)
        {
            // En wallhug no se muestra nada (el botón sigue siendo el mismo input
            // pero el contexto no aplica).
            if (playerMovement.isWallhugging) return string.Empty;
            // En ledge grab → Climb (subir)
            if (playerMovement.isLedgeGrabbing) return labelClimb;
        }

        if (interactor != null && interactor.onInteractArea) return labelInteract;
        if (playerMovement != null && playerMovement.nearWall) return labelWallhug;
        return labelDash;
    }
}
