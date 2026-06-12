using UnityEngine;

/// <summary>
/// Tracks the currently-equipped item and routes attack/use input to it.
/// Self-subscribes to:
///   • OnAttackInputEvent       → calls <see cref="UseItem"/> on press.
///   • OnItemPickedUpEvent      → auto-syncs to a freshly-picked sword when appropriate.
///   • OnSetAttackEnabledEvent  → toggles whether attack input is honored.
///
/// Equip/Unequip emit OnItemEquipEvent / OnItemUnequipEvent so HUD, animations
/// and quickslots stay in sync without inspector wiring.
/// </summary>
public class EquipHandler : MonoBehaviour
{
    public static EquipHandler Instance { get; private set; }

    public SOItem EquipedItem;

    [Space(10)]
    [Header("CONFIGS")]
    public Transform ItemsPivotPoint;

    private GameObject _currentWeaponInstance;

    private bool attackEnabled = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Player-attached singleton: remove only the duplicate component, never the
            // host GameObject — Destroy(gameObject) here would delete the whole player.
            Destroy(this);
            return;
        }
        Instance = this;

        // The player starts empty-handed. Clear any serialized default so the resting
        // state is "nothing in hand" — the sword is drawn on the first attack press.
        // Save-load re-equips explicitly (via OnItemEquipEvent), so this is safe.
        EquipedItem = null;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Subscribe<OnAttackInputEvent>(OnAttackInput);
        EventBus.Subscribe<OnSetAttackEnabledEvent>(OnSetAttackEnabled);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Unsubscribe<OnAttackInputEvent>(OnAttackInput);
        EventBus.Unsubscribe<OnSetAttackEnabledEvent>(OnSetAttackEnabled);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_currentWeaponInstance != null)
            Destroy(_currentWeaponInstance);
    }

    private void Start()
    {
        if (InventoryHandler.Instance == null)
            Debug.LogError("[EquipHandler] InventoryHandler not found in scene.");

        // No auto-equip: the player begins empty-handed. The sword is drawn by the
        // first attack press; save-load equips explicitly when restoring a session.
    }

    // ─── Event handlers ────────────────────────────────────────────────────

    /// <summary>
    /// When a sword is picked up that performed a tier replacement on slot 0,
    /// EquipedItem may still reference the old sword. Auto-sync if appropriate.
    /// </summary>
    private void OnItemPickedUp(OnItemPickedUpEvent e)
    {
        if (e.item is SOSword && InventoryHandler.Instance != null)
        {
            SOItem slot0 = InventoryHandler.Instance.GetItem(0);
            // Only refresh when the player is CURRENTLY holding a (now tier-replaced)
            // sword. Never auto-draw onto an empty hand — empty hand is the resting
            // state, and we don't interrupt a held potion/tool either.
            if (slot0 != null && EquipedItem is SOSword && slot0 != EquipedItem)
                EquipItem(slot0);
        }
    }

    private void OnAttackInput(OnAttackInputEvent e)
    {
        if (!attackEnabled || !e.pressed) return;
        // Gate by the player's authoritative state: no attacking/using items while
        // hanging, climbing, dashing, airborne, wallhugging, etc.
        if (PlayerStateMachine.Instance != null && !PlayerStateMachine.Instance.CanAct) return;

        // Empty-handed: the first press DRAWS the sword (slot 0) rather than attacking.
        // The player then presses again to actually swing — "press twice from empty".
        if (EquipedItem == null)
        {
            SOItem sword = InventoryHandler.Instance != null ? InventoryHandler.Instance.GetItem(0) : null;
            if (sword != null) EquipItem(sword);
            return;
        }

        UseItem();
    }

    private void OnSetAttackEnabled(OnSetAttackEnabledEvent e) => attackEnabled = e.enabled;

    // ─── Public API ────────────────────────────────────────────────────────

    public void EquipItem(SOItem item)
    {
        if (item == null) return;

        if (_currentWeaponInstance != null)
        {
            Destroy(_currentWeaponInstance);
            _currentWeaponInstance = null;
        }

        SOItem prev = EquipedItem;
        // Set FIRST, raise AFTER — handlers reading EquipedItem see the new state.
        EquipedItem = item;
        item.PivotPoint = ItemsPivotPoint;

        if (item is SOWeapon && item.prefab != null && ItemsPivotPoint != null)
            _currentWeaponInstance = Instantiate(item.prefab, ItemsPivotPoint);

        if (prev != null && prev != item)
            EventBus.Raise(new OnItemUnequipEvent { item = prev });

        EventBus.Raise(new OnItemEquipEvent { item = item });
    }

    /// <summary>Holsters the current item — returns the player to an empty hand.</summary>
    public void UnEquipItem()
    {
        SOItem prev = EquipedItem;
        if (prev == null) return;   // already empty-handed

        EquipedItem = null;

        if (_currentWeaponInstance != null)
        {
            Destroy(_currentWeaponInstance);
            _currentWeaponInstance = null;
        }

        EventBus.Raise(new OnItemUnequipEvent { item = prev });
    }

    public void UseItem()
    {
        if (EquipedItem is SOPotion)
        {
            SOItem emptyBottle = InventoryHandler.Instance != null ? InventoryHandler.Instance.defaultBottle : null;
            if (emptyBottle == null)
            {
                Debug.LogError("[EquipHandler] InventoryHandler.defaultBottle is not assigned.");
                return;
            }

            // Don't consume the empty bottle.
            if (EquipedItem == emptyBottle) return;

            SOItem consumed = EquipedItem;
            consumed.Use(gameObject);
            EventBus.Raise(new OnPotionConsumeEvent(consumed, emptyBottle));

            // Route through EquipItem so the proper Unequip/Equip events fire
            // (HUD, quickslots, etc. stay in sync).
            EquipItem(emptyBottle);
        }
        else if (EquipedItem is SOWeapon)
        {
            EquipedItem.Use(gameObject);
        }
    }
}
