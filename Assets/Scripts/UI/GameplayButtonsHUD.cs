using UnityEngine;

/// <summary>
/// 4-button cross HUD (top-right corner):
///   North = item1   East = item2   West = equipped item (attack)   South = action/dash
///
/// Icons: event-driven (Equip/Unequip/QuickslotAssigned/PotionConsume).
/// Labels:
///   - West: "Attack" (SOWeapon) / "Use" (SOPotion) / empty.
///   - North/East: "Equip" when there's an item and it isn't equipped, "Store" when it is, empty if null.
///   - South: "Interact" when an interactable is nearby, "Wall hug" when nearWall, otherwise "Dash".
///     During wallhug → empty. During ledge grab → "Climb".
/// </summary>
public class GameplayButtonsHUD : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private HUDButtonWidget northButton; // item1
    [SerializeField] private HUDButtonWidget eastButton;  // item2
    [SerializeField] private HUDButtonWidget westButton;  // attack (equipped)
    [SerializeField] private HUDButtonWidget southButton; // action

    // Note: PlayerMovement / Interactor references are no longer cached here.
    // State arrives via OnPlayerLocomotionStateEvent / OnInteractableProximityChangedEvent,
    // so the HUD has no per-frame dependency on those components.

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

    // ─── Cached locomotion / proximity state (event-driven, no polling) ─────
    private bool _isWallhugging;
    private bool _isLedgeGrabbing;
    private bool _nearWall;
    private bool _interactableNearby;

    private void OnEnable()
    {
        EventBus.Subscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Subscribe<OnItemUnequipEvent>(OnItemUnequip);
        EventBus.Subscribe<OnQuickslotAssignedEvent>(OnQuickslotAssigned);
        EventBus.Subscribe<OnPotionConsumeEvent>(OnPotionConsume);
        EventBus.Subscribe<OnPlayerLocomotionStateEvent>(OnPlayerLocomotionState);
        EventBus.Subscribe<OnInteractableProximityChangedEvent>(OnInteractableProximity);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Unsubscribe<OnItemUnequipEvent>(OnItemUnequip);
        EventBus.Unsubscribe<OnQuickslotAssignedEvent>(OnQuickslotAssigned);
        EventBus.Unsubscribe<OnPotionConsumeEvent>(OnPotionConsume);
        EventBus.Unsubscribe<OnPlayerLocomotionStateEvent>(OnPlayerLocomotionState);
        EventBus.Unsubscribe<OnInteractableProximityChangedEvent>(OnInteractableProximity);
    }

    private void Start()
    {
        // Pull initial state from the singletons (set up in their Awake — zero cost).
        // Subsequent updates arrive via events; no per-frame polling needed.
        if (PlayerMovement.Instance != null)
        {
            _isWallhugging   = PlayerMovement.Instance.isWallhugging;
            _isLedgeGrabbing = PlayerMovement.Instance.isLedgeGrabbing;
            _nearWall        = PlayerMovement.Instance.nearWall;
        }
        if (Interactor.Instance != null)
            _interactableNearby = Interactor.Instance.onInteractArea;

        RefreshAll();
    }

    // ─── Event handlers ─────────────────────────────────────────────────────
    // Any event that may change state (equipped / quickslot contents / consumed potion)
    // fires a full RefreshAllButtons. This way, if two slots share a reference with
    // the equipped item, both receive the correct label in the same refresh — and no
    // button is left with a stale label/icon from a partial update.

    private void OnItemEquip(OnItemEquipEvent e)         => RefreshAllButtons();
    private void OnItemUnequip(OnItemUnequipEvent e)     => RefreshAllButtons();
    private void OnPotionConsume(OnPotionConsumeEvent e) => RefreshAllButtons();
    private void OnQuickslotAssigned(OnQuickslotAssignedEvent e) => RefreshAllButtons();

    private void OnPlayerLocomotionState(OnPlayerLocomotionStateEvent e)
    {
        _isWallhugging   = e.isWallhugging;
        _isLedgeGrabbing = e.isLedgeGrabbing;
        _nearWall        = e.nearWall;
        UpdateSouth();
    }

    private void OnInteractableProximity(OnInteractableProximityChangedEvent e)
    {
        _interactableNearby = e.nearby;
        UpdateSouth();
    }

    // ─── Refresh helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Reads the authoritative state (EquipHandler.EquipedItem + QuickslotManager.Slot1/Slot2)
    /// and updates the 3 buttons (icon + label). Idempotent; cheap (only sprite/text sets).
    /// Guarantees that slots sharing a reference end up with the same label.
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

    /// <summary>
    /// Pure function over cached state — no component access, no polling.
    /// State is kept up to date by OnPlayerLocomotionState / OnInteractableProximity.
    /// </summary>
    private string GetSouthLabel()
    {
        // Wallhug consumes the south slot: nothing meaningful to surface there.
        if (_isWallhugging)      return string.Empty;
        // Ledge grab → Climb up.
        if (_isLedgeGrabbing)    return labelClimb;
        // Otherwise the button represents whichever action applies.
        if (_interactableNearby) return labelInteract;
        if (_nearWall)           return labelWallhug;
        return labelDash;
    }
}
