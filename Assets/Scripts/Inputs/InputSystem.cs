using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Routes raw Unity InputSystem callbacks into our EventBus events.
///
/// Performance: per-frame events (look / move) reuse cached event instances
/// instead of allocating each frame. EventBus.Raise dispatches synchronously
/// and doesn't retain the reference, so mutation between raises is safe as
/// long as no handler triggers a nested Raise of the same event type.
/// </summary>
public class InputSystem : MonoBehaviour
{
    // Single authoritative input router. A duplicate would raise every input event
    // twice — harmless for additive inputs (move) but breaks toggles (pause/inventory),
    // since two presses net to no change. The guard below destroys any duplicate.
    public static InputSystem Instance { get; private set; }

    private MyInputs inputs;
    private LookInputSource currentLookSource = LookInputSource.Mouse;

    // ── Cached event instances (avoid GC churn on per-frame inputs) ──────
    private readonly OnLookInputEvent          _lookEvt     = new();
    private readonly OnMoveInputEvent          _moveEvt     = new();
    private readonly OnAttackInputEvent        _attackEvt   = new();
    private readonly OnItemOneInputEvent       _item1Evt    = new();
    private readonly OnItemTwoInputEvent       _item2Evt    = new();
    private readonly OnInteractDodgeInputEvent _interactEvt = new();
    private readonly OnTargetInputEvent        _targetEvt   = new();
    private readonly OnInventoryInputEvent     _invEvt      = new();
    private readonly OnPauseInputEvent         _pauseEvt    = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[InputSystem] A second InputSystem was found and destroyed. " +
                             "Only one may exist or every input fires twice (breaks pause/inventory toggles).", this);
            Destroy(this);
            return;
        }
        Instance = this;

        inputs = new MyInputs();

        inputs.Player.MOVE.performed += OnMovePerformed;
        inputs.Player.MOVE.canceled  += OnMoveCanceled;

        inputs.Player.LOOK.performed += OnLookPerformed;
        inputs.Player.LOOK.canceled  += OnLookCanceled;

        inputs.Player.ATTACK.performed     += OnAttackInput;
        inputs.Player.ATTACK.canceled      += OnAttackInput;

        inputs.Player.ITEM1.performed      += OnItemOneInput;
        inputs.Player.ITEM1.canceled       += OnItemOneInput;

        inputs.Player.ITEM2.performed      += OnItemTwoInput;
        inputs.Player.ITEM2.canceled       += OnItemTwoInput;

        inputs.Player.INTERACT.performed   += OnInteractDodgeInput;
        inputs.Player.INTERACT.canceled    += OnInteractDodgeInput;

        inputs.Player.TARGET.performed     += OnTargetInput;
        inputs.Player.TARGET.canceled      += OnTargetInput;

        inputs.Player.INVENTORY.performed  += OnInventoryInput;
        inputs.Player.INVENTORY.canceled   += OnInventoryInput;

        inputs.Player.PAUSE.performed      += OnPauseInput;
        inputs.Player.PAUSE.canceled       += OnPauseInput;
    }

    // Null-safe: a destroyed duplicate (inputs == null) must not throw here.
    private void OnEnable()  { if (inputs != null) inputs.Player.Enable(); }
    private void OnDisable() { if (inputs != null) inputs.Player.Disable(); }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Gameplay inputs are suppressed while the game is paused (timeScale == 0).
    // Move/Look cancels are always allowed through to zero out accumulated state.
    // Pause itself is never blocked.
    private bool IsGameplayBlocked => Time.timeScale == 0f;

    // ── Look ────────────────────────────────────────────────────────────

    private void OnLookPerformed(InputAction.CallbackContext context)
    {
        if (IsGameplayBlocked) return;

        currentLookSource = context.control.device is Gamepad
            ? LookInputSource.Gamepad
            : LookInputSource.Mouse;

        _lookEvt.pressed = true;
        _lookEvt.Delta   = context.ReadValue<Vector2>();
        _lookEvt.Source  = currentLookSource;
        EventBus.Raise(_lookEvt);
    }

    private void OnLookCanceled(InputAction.CallbackContext context)
    {
        _lookEvt.pressed = false;
        _lookEvt.Delta   = Vector2.zero;
        _lookEvt.Source  = currentLookSource;
        EventBus.Raise(_lookEvt);
    }

    // ── Move ────────────────────────────────────────────────────────────

    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        if (IsGameplayBlocked) return;

        _moveEvt.pressed   = true;
        _moveEvt.Direction = context.ReadValue<Vector2>();
        EventBus.Raise(_moveEvt);
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        _moveEvt.pressed   = false;
        _moveEvt.Direction = Vector2.zero;
        EventBus.Raise(_moveEvt);
    }

    // ── Button inputs ───────────────────────────────────────────────────

    private void OnAttackInput(InputAction.CallbackContext context)
    {
        if (IsGameplayBlocked) return;
        _attackEvt.pressed = context.performed;
        EventBus.Raise(_attackEvt);
    }

    private void OnItemOneInput(InputAction.CallbackContext context)
    {
        if (IsGameplayBlocked) return;
        _item1Evt.pressed = context.performed;
        EventBus.Raise(_item1Evt);
    }

    private void OnItemTwoInput(InputAction.CallbackContext context)
    {
        if (IsGameplayBlocked) return;
        _item2Evt.pressed = context.performed;
        EventBus.Raise(_item2Evt);
    }

    private void OnInteractDodgeInput(InputAction.CallbackContext context)
    {
        if (IsGameplayBlocked) return;
        _interactEvt.pressed = context.performed;
        EventBus.Raise(_interactEvt);
    }

    private void OnTargetInput(InputAction.CallbackContext context)
    {
        if (IsGameplayBlocked) return;
        _targetEvt.pressed = context.performed;
        EventBus.Raise(_targetEvt);
    }

    private void OnInventoryInput(InputAction.CallbackContext context)
    {
        if (IsGameplayBlocked) return;
        _invEvt.pressed = context.performed;
        EventBus.Raise(_invEvt);
    }

    private void OnPauseInput(InputAction.CallbackContext context)
    {
        _pauseEvt.pressed = context.performed;
        EventBus.Raise(_pauseEvt);
    }
}
