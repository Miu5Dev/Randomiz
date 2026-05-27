using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Rueda radial de inventario (toggle con OnInventoryInputEvent).
/// Excluye slot 0 (espada). Lee posición del mouse o del stick para resaltar slot.
/// Al pulsar item1/item2 mientras está abierta, asigna el item resaltado al quickslot
/// correspondiente y cancela el evento para que QuickslotManager no equipe nada.
/// </summary>
public class InventoryWheelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GameObject wheelRoot;
    [SerializeField] private InventoryWheelSlot slotPrefab;
    [SerializeField] private RectTransform slotsContainer;

    [Header("Layout")]
    [Tooltip("Distancia del centro a cada slot, en unidades del Canvas.")]
    [SerializeField] private float radius = 180f;

    [Header("Input")]
    [Tooltip("Magnitud mínima del stick/cursor virtual para considerar selección válida.")]
    [Range(0f, 1f)] [SerializeField] private float deadZone = 0.25f;
    [Tooltip("Sensibilidad del cursor virtual del mouse (delta px → unidades de selección).")]
    [SerializeField] private float mouseSensitivity = 0.01f;

    private readonly List<InventoryWheelSlot> slots = new();
    private bool isOpen;
    private int highlightedIndex = -1;
    private Vector2 virtualCursor; // acumulador del delta del mouse mientras la rueda está abierta

    private void Awake()
    {
        if (wheelRoot != null) wheelRoot.SetActive(false);
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnInventoryInputEvent>(OnInventoryInput);
        // Prioridad alta para interceptar antes que QuickslotManager / movement / camera
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
            // Forzamos a cero el movimiento y la cámara ANTES de marcar isOpen=true,
            // así estos eventos sintéticos no son cancelados por nuestro propio handler
            // y llegan a PlayerMovement/CameraController, que se detienen.
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

    private void BuildSlots()
    {
        foreach (var s in slots) if (s != null) Destroy(s.gameObject);
        slots.Clear();

        if (InventoryHandler.Instance == null || slotPrefab == null || slotsContainer == null) return;

        SOItem[] items = InventoryHandler.Instance.InvItems;
        int totalSlots = items.Length - 1; // excluye slot 0 (espada)
        if (totalSlots <= 0) return;

        for (int i = 0; i < totalSlots; i++)
        {
            int invIndex = i + 1;
            InventoryWheelSlot slot = Instantiate(slotPrefab, slotsContainer);
            slot.SetItem(items[invIndex]);

            float angleDeg = 90f - (i * 360f / totalSlots); // arranca arriba, sentido horario
            float angleRad = angleDeg * Mathf.Deg2Rad;
            Vector2 pos = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius;

            var rt = slot.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = pos;

            slots.Add(slot);
        }

        highlightedIndex = -1;
    }

    private void Update()
    {
        if (!isOpen) return;
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        Vector2 dir = GetSelectorDirection();
        if (dir.magnitude < deadZone)
        {
            SetHighlight(-1);
            return;
        }

        float inputAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (inputAngle < 0f) inputAngle += 360f;

        int n = slots.Count;
        if (n == 0) return;

        float step = 360f / n;
        const float startAngle = 90f;

        int bestIdx = 0;
        float bestDiff = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float slotAngle = (startAngle - i * step + 360f) % 360f;
            float diff = Mathf.Abs(Mathf.DeltaAngle(slotAngle, inputAngle));
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIdx = i;
            }
        }

        SetHighlight(bestIdx);
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
        // 1) Gamepad: stick izquierdo (vector directo, no acumula)
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            if (stick.magnitude >= deadZone) return stick;
        }

        // 2) Mouse: acumulamos el delta en un cursor virtual.
        //    Funciona aunque el cursor real esté bloqueado/invisible (FPS-like).
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

    // Mientras la rueda está abierta, bloqueamos movimiento, cámara, ataque e interactuar
    // cancelando los eventos antes de que lleguen a movement/camera/quickslot.
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

        // Llamada directa (no por evento) para que la lógica de swap del QuickslotManager
        // pueda emitir múltiples OnQuickslotAssignedEvent sin recursión.
        if (QuickslotManager.Instance != null)
            QuickslotManager.Instance.AssignToSlot(slotIndex, item);
    }
}
