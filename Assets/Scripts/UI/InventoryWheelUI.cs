using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Radial inventory wheel (toggled by OnInventoryInputEvent).
/// Excludes slot 0 (sword). Reads mouse or stick position to highlight a slot.
/// Pressing item1/item2 while open assigns the highlighted item to that quickslot
/// and cancels the event so QuickslotManager doesn't also equip it.
/// </summary>
public class InventoryWheelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GameObject wheelRoot;
    [SerializeField] private InventoryWheelSlot slotPrefab;
    [SerializeField] private RectTransform slotsContainer;

    [Header("Layout")]
    [Tooltip("Distance from the center to each slot, in Canvas units.")]
    [SerializeField] private float radius = 180f;

    [Header("Input")]
    [Tooltip("Minimum stick/virtual-cursor magnitude required to count as a selection.")]
    [Range(0f, 1f)] [SerializeField] private float deadZone = 0.25f;
    [Tooltip("Mouse virtual-cursor sensitivity (delta px → selection units).")]
    [SerializeField] private float mouseSensitivity = 0.01f;

    private readonly List<InventoryWheelSlot> slots = new();
    private int activeSlotCount;       // visible slots this open (pool may hold more)
    private bool isOpen;
    private int highlightedIndex = -1;
    private Vector2 virtualCursor;     // accumulated mouse delta while the wheel is open

    private void Awake()
    {
        if (wheelRoot != null) wheelRoot.SetActive(false);
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnInventoryInputEvent>(OnInventoryInput);
        // High priority — runs before QuickslotManager / movement / camera so we can intercept.
        EventBus.Subscribe<OnItemOneInputEvent>(OnItemOne, 10);
        EventBus.Subscribe<OnItemTwoInputEvent>(OnItemTwo, 10);
        EventBus.Subscribe<OnMoveInputEvent>(OnMoveInput, 10);
        EventBus.Subscribe<OnLookInputEvent>(OnLookInput, 10);
        EventBus.Subscribe<OnAttackInputEvent>(OnAttackInput, 10);
        EventBus.Subscribe<OnInteractDodgeInputEvent>(OnInteractInput, 10);
        EventBus.Subscribe<OnTargetInputEvent>(OnTargetInput, 10);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnInventoryInputEvent>(OnInventoryInput);
        EventBus.Unsubscribe<OnItemOneInputEvent>(OnItemOne);
        EventBus.Unsubscribe<OnItemTwoInputEvent>(OnItemTwo);
        EventBus.Unsubscribe<OnMoveInputEvent>(OnMoveInput);
        EventBus.Unsubscribe<OnLookInputEvent>(OnLookInput);
        EventBus.Unsubscribe<OnAttackInputEvent>(OnAttackInput);
        EventBus.Unsubscribe<OnInteractDodgeInputEvent>(OnInteractInput);
        EventBus.Unsubscribe<OnTargetInputEvent>(OnTargetInput);
    }

    private void OnInventoryInput(OnInventoryInputEvent e)
    {
        if (!e.pressed) return;
        SetOpen(!isOpen);
    }

    private void SetOpen(bool open)
    {
        if (open)
        {
            // Force movement and camera to zero BEFORE setting isOpen=true so these
            // synthetic events aren't cancelled by our own handler and reach
            // PlayerMovement/CameraController to halt them.
            EventBus.Raise(new OnMoveInputEvent { Direction = Vector2.zero, pressed = false });
            EventBus.Raise(new OnLookInputEvent { Delta = Vector2.zero, pressed = false, Source = LookInputSource.Mouse });

            virtualCursor = Vector2.zero;
        }

        isOpen = open;
        if (wheelRoot != null) wheelRoot.SetActive(open);

        if (open) BuildSlots();
        else ClearHighlight();

        EventBus.Raise(new OnInventoryWheelStateEvent { open = open });
    }

    // Cached RectTransforms for each slot — avoid GetComponent on every open.
    private readonly List<RectTransform> slotRects = new();

    /// <summary>
    /// Populates / refreshes the wheel slots. Pools widgets between opens —
    /// only instantiates when we need MORE slots than the pool currently has,
    /// otherwise just re-positions and re-binds items. Excess slots are
    /// deactivated rather than destroyed.
    /// </summary>
    private void BuildSlots()
    {
        if (InventoryHandler.Instance == null || slotPrefab == null || slotsContainer == null) return;

        SOItem[] items = InventoryHandler.Instance.InvItems;
        int totalSlots = items.Length - 1; // exclude slot 0 (sword)
        if (totalSlots <= 0) return;

        // Grow the pool if needed.
        while (slots.Count < totalSlots)
        {
            var newSlot = Instantiate(slotPrefab, slotsContainer);
            slots.Add(newSlot);
            slotRects.Add(newSlot.GetComponent<RectTransform>());
        }

        float angleStep = 360f / totalSlots;
        const float startAngleDeg = 90f;

        // Configure the slots we need.
        for (int i = 0; i < totalSlots; i++)
        {
            int invIndex = i + 1;
            var slot = slots[i];
            if (!slot.gameObject.activeSelf) slot.gameObject.SetActive(true);

            slot.SetItem(items[invIndex]);

            float angleRad = (startAngleDeg - i * angleStep) * Mathf.Deg2Rad;
            var rt = slotRects[i];
            if (rt != null)
                rt.anchoredPosition = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius;
        }

        // Deactivate any excess pool entries (e.g. inventory shrank).
        for (int i = totalSlots; i < slots.Count; i++)
            if (slots[i].gameObject.activeSelf) slots[i].gameObject.SetActive(false);

        activeSlotCount = totalSlots;
        highlightedIndex = -1;
    }

    private void Update()
    {
        if (!isOpen) return;
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        // Cheap squared-magnitude check avoids the sqrt in .magnitude.
        Vector2 dir = GetSelectorDirection();
        if (dir.sqrMagnitude < deadZone * deadZone)
        {
            SetHighlight(-1);
            return;
        }

        int n = activeSlotCount;
        if (n <= 0) return;

        // Closed-form pick: convert input angle to slot index directly.
        // Slot i sits at angle (90° - i * step). Inverting:
        //   i = round((90° - inputAngle) / step), modulo n.
        float inputAngleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float step = 360f / n;
        int idx = Mathf.RoundToInt((90f - inputAngleDeg) / step);
        idx = ((idx % n) + n) % n;    // normalize to [0, n)
        SetHighlight(idx);
    }

    private void SetHighlight(int idx)
    {
        if (highlightedIndex == idx) return;
        if (highlightedIndex >= 0 && highlightedIndex < slots.Count)
            slots[highlightedIndex].SetHighlighted(false);

        highlightedIndex = idx;

        if (highlightedIndex >= 0 && highlightedIndex < slots.Count)
            slots[highlightedIndex].SetHighlighted(true);
    }

    private void ClearHighlight() => SetHighlight(-1);

    private Vector2 GetSelectorDirection()
    {
        // 1) Gamepad: left stick (direct vector, no accumulation)
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            if (stick.sqrMagnitude >= deadZone * deadZone) return stick;
        }

        // 2) Mouse: accumulate the delta into a virtual cursor.
        //    Works even when the real cursor is locked/invisible (FPS-like setup).
        if (Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            virtualCursor += delta * mouseSensitivity;
            if (virtualCursor.magnitude > 1f) virtualCursor = virtualCursor.normalized;
            return virtualCursor;
        }

        return Vector2.zero;
    }

    private void OnItemOne(OnItemOneInputEvent e)
    {
        if (!isOpen || !e.pressed) return;
        AssignHighlighted(1);
        EventBus.Cancel<OnItemOneInputEvent>();
    }

    private void OnItemTwo(OnItemTwoInputEvent e)
    {
        if (!isOpen || !e.pressed) return;
        AssignHighlighted(2);
        EventBus.Cancel<OnItemTwoInputEvent>();
    }

    // While the wheel is open, block movement, camera, attack and interact
    // by cancelling the events before they reach movement/camera/quickslot handlers.
    private void OnMoveInput(OnMoveInputEvent e)
    {
        if (!isOpen) return;
        EventBus.Cancel<OnMoveInputEvent>();
    }

    private void OnLookInput(OnLookInputEvent e)
    {
        if (!isOpen) return;
        EventBus.Cancel<OnLookInputEvent>();
    }

    private void OnAttackInput(OnAttackInputEvent e)
    {
        if (!isOpen) return;
        EventBus.Cancel<OnAttackInputEvent>();
    }

    private void OnInteractInput(OnInteractDodgeInputEvent e)
    {
        if (!isOpen) return;
        EventBus.Cancel<OnInteractDodgeInputEvent>();
    }

    private void OnTargetInput(OnTargetInputEvent e)
    {
        if (!isOpen) return;
        EventBus.Cancel<OnTargetInputEvent>();
    }

    private void AssignHighlighted(int slotIndex)
    {
        SOItem item = null;
        if (highlightedIndex >= 0 && highlightedIndex < slots.Count)
            item = slots[highlightedIndex].Item;

        // Direct call (not via event) so QuickslotManager's swap logic can emit
        // multiple OnQuickslotAssignedEvent without recursion.
        if (QuickslotManager.Instance != null)
            QuickslotManager.Instance.AssignToSlot(slotIndex, item);
    }
}
