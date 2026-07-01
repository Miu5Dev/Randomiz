using System.Collections.Generic;
using Randomiz.UI;
using TMPro;
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
    [Tooltip("Container where the key list label is placed (e.g. [WheelContainer]).")]
    [SerializeField] private RectTransform keyPanelContainer;

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
    private bool actionsEnabled = true;
    private int highlightedIndex = -1;
    private Vector2 virtualCursor;     // accumulated mouse delta while the wheel is open

    // Code-built hint labels (no prefab wiring): center shows the highlighted item,
    // the line below reminds the player which key assigns to which quickslot.
    private TMP_Text _centerLabel;
    private TMP_Text _hintLabel;
    private TMP_Text _keyListLabel;

    private void Awake()
    {
        if (wheelRoot != null) wheelRoot.SetActive(false);
        BuildHints();
        BuildKeyPanel();
    }

    /// <summary>
    /// Builds the Q/E assignment hints in code, parented under the wheel so they
    /// show and hide together with it. Centered on the radial pivot.
    /// </summary>
    private void BuildHints()
    {
        Transform parent = slotsContainer != null ? (Transform)slotsContainer
                         : (wheelRoot != null ? wheelRoot.transform : null);
        if (parent == null) return;

        _centerLabel = UIFactory.CreateLabel(parent, "", 24, Color.white);
        var cRt = _centerLabel.rectTransform;
        cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0.5f);
        cRt.pivot = new Vector2(0.5f, 0.5f);
        cRt.sizeDelta = new Vector2(260f, 40f);
        cRt.anchoredPosition = Vector2.zero;

        _hintLabel = UIFactory.CreateLabel(parent, "[Q] Slot 1        [E] Slot 2", 22,
            new Color(0.85f, 0.85f, 0.85f));
        var hRt = _hintLabel.rectTransform;
        hRt.anchorMin = hRt.anchorMax = new Vector2(0.5f, 0.5f);
        hRt.pivot = new Vector2(0.5f, 0.5f);
        hRt.sizeDelta = new Vector2(520f, 44f);
        hRt.anchoredPosition = new Vector2(0f, -(radius + 64f));
    }

    /// <summary>
    /// Builds a key-list label anchored to the left edge of the wheel root.
    /// Shows the player which keys they hold and the count of each type.
    /// </summary>
    private void BuildKeyPanel()
    {
        Transform parent = keyPanelContainer != null ? (Transform)keyPanelContainer
                         : (wheelRoot != null ? wheelRoot.transform : null);
        if (parent == null) return;

        _keyListLabel = UIFactory.CreateLabel(parent, "",
            20, Color.white, TextAlignmentOptions.TopLeft);

        var rt = _keyListLabel.rectTransform;
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot     = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(220f, 400f);
        rt.anchoredPosition = new Vector2(30f, 0f);
        _keyListLabel.enableWordWrapping = true;
        _keyListLabel.overflowMode = TextOverflowModes.Overflow;
    }

    private void RefreshKeyPanel()
    {
        if (_keyListLabel == null) return;

        if (KeyInventory.Instance == null || KeyInventory.Instance.Keys.Count == 0)
        {
            _keyListLabel.text = "";
            return;
        }

        // Count each unique key id.
        var counts = new Dictionary<string, (string name, int count)>();
        foreach (var k in KeyInventory.Instance.Keys)
        {
            if (counts.TryGetValue(k.keyId, out var entry))
                counts[k.keyId] = (entry.name, entry.count + 1);
            else
                counts[k.keyId] = (k.displayName, 1);
        }

        string text = "<b>Keys</b>";
        foreach (var kv in counts)
            text += kv.Value.count > 1
                ? $"\n{kv.Value.name}  x{kv.Value.count}"
                : $"\n{kv.Value.name}";

        _keyListLabel.text = text;
    }

    private void OnKeyInventoryChanged(OnKeyInventoryChangedEvent _) => RefreshKeyPanel();

    private void OnEnable()
    {
        EventBus.Subscribe<OnInventoryInputEvent>(OnInventoryInput);
        // Priority 20: above PauseMenuUI (10) so Esc closes the wheel and is consumed
        // — the pause menu must NOT open on the same press.
        EventBus.Subscribe<OnPauseInputEvent>(OnPauseInput, 20);
        EventBus.Subscribe<OnKeyInventoryChangedEvent>(OnKeyInventoryChanged);
        // High priority — runs before QuickslotManager / movement / camera so we can intercept.
        EventBus.Subscribe<OnItemOneInputEvent>(OnItemOne, 10);
        EventBus.Subscribe<OnItemTwoInputEvent>(OnItemTwo, 10);
        EventBus.Subscribe<OnMoveInputEvent>(OnMoveInput, 10);
        EventBus.Subscribe<OnLookInputEvent>(OnLookInput, 10);
        EventBus.Subscribe<OnAttackInputEvent>(OnAttackInput, 10);
        EventBus.Subscribe<OnInteractDodgeInputEvent>(OnInteractInput, 10);
        EventBus.Subscribe<OnTargetInputEvent>(OnTargetInput, 10);
        // While the player is dead/dying, opening the wheel could re-equip/unequip
        // through AssignToSlot, the same class of bug the death/respawn flow guards
        // against elsewhere (see DeathScreenUI, QuickslotManager).
        EventBus.Subscribe<OnSetAttackEnabledEvent>(OnSetAttackEnabled);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnInventoryInputEvent>(OnInventoryInput);
        EventBus.Unsubscribe<OnPauseInputEvent>(OnPauseInput);
        EventBus.Unsubscribe<OnKeyInventoryChangedEvent>(OnKeyInventoryChanged);
        EventBus.Unsubscribe<OnItemOneInputEvent>(OnItemOne);
        EventBus.Unsubscribe<OnItemTwoInputEvent>(OnItemTwo);
        EventBus.Unsubscribe<OnMoveInputEvent>(OnMoveInput);
        EventBus.Unsubscribe<OnLookInputEvent>(OnLookInput);
        EventBus.Unsubscribe<OnAttackInputEvent>(OnAttackInput);
        EventBus.Unsubscribe<OnInteractDodgeInputEvent>(OnInteractInput);
        EventBus.Unsubscribe<OnTargetInputEvent>(OnTargetInput);
        EventBus.Unsubscribe<OnSetAttackEnabledEvent>(OnSetAttackEnabled);
    }

    private void OnSetAttackEnabled(OnSetAttackEnabledEvent e) => actionsEnabled = e.enabled;

    private void OnInventoryInput(OnInventoryInputEvent e)
    {
        if (!e.pressed || !actionsEnabled) return;
        SetOpen(!isOpen);
    }

    private void OnPauseInput(OnPauseInputEvent e)
    {
        if (!e.pressed || !isOpen) return;
        // Consume the press so PauseMenuUI (lower priority) doesn't also open.
        EventBus.Cancel<OnPauseInputEvent>();
        SetOpen(false);
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

        if (open) { BuildSlots(); RefreshKeyPanel(); }
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

        // Surface the highlighted item's name so the player knows what Q/E will assign.
        if (_centerLabel != null)
        {
            SOItem item = (highlightedIndex >= 0 && highlightedIndex < slots.Count)
                ? slots[highlightedIndex].Item : null;
            _centerLabel.text = item != null ? item.itemName : string.Empty;
        }
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
