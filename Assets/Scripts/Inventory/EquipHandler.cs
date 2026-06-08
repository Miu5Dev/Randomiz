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

    private bool attackEnabled = true;

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
    }

    private void Start()
    {
        if (InventoryHandler.Instance == null)
            Debug.LogError("[EquipHandler] InventoryHandler not found in scene.");

        if (EquipedItem != null) EquipItem(EquipedItem);
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
            if (slot0 != null && slot0 != EquipedItem)
            {
                // Only auto-swap if the player was on a sword (or empty-handed).
                // If they were holding e.g. a potion, don't interrupt them.
                if (EquipedItem == null || EquipedItem is SOSword)
                    EquipItem(slot0);
            }
        }
    }

    private void OnAttackInput(OnAttackInputEvent e)
    {
        if (!attackEnabled || !e.pressed) return;
        // Gate by the player's authoritative state: no attacking/using items while
        // hanging, climbing, dashing, airborne, wallhugging, etc.
        if (PlayerStateMachine.Instance != null && !PlayerStateMachine.Instance.CanAct) return;
        UseItem();
    }

    private void OnSetAttackEnabled(OnSetAttackEnabledEvent e) => attackEnabled = e.enabled;

    // ─── Public API ────────────────────────────────────────────────────────

    public void EquipItem(SOItem item)
    {
        if (item == null) return;

        SOItem prev = EquipedItem;
        // Set FIRST, raise AFTER — handlers reading EquipedItem see the new state.
        EquipedItem = item;
        item.PivotPoint = ItemsPivotPoint;

        if (prev != null && prev != item)
            EventBus.Raise(new OnItemUnequipEvent { item = prev });

        EventBus.Raise(new OnItemEquipEvent { item = item });
    }

    public void UnEquipItem()
    {
        SOItem prev = EquipedItem;
        SOItem sword = InventoryHandler.Instance != null ? InventoryHandler.Instance.GetItem(0) : null;

        if (prev == sword)
        {
            // No actual state change, but re-emit OnItemEquipEvent so listeners (HUD,
            // animations, etc.) can resync — protects against earlier desync from a
            // pickup that swapped slot 0 silently.
            if (sword != null)
                EventBus.Raise(new OnItemEquipEvent { item = sword });
            return;
        }

        EquipedItem = sword;
        if (sword != null) sword.PivotPoint = ItemsPivotPoint;

        if (prev != null)
            EventBus.Raise(new OnItemUnequipEvent { item = prev });

        if (sword != null)
            EventBus.Raise(new OnItemEquipEvent { item = sword });
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
