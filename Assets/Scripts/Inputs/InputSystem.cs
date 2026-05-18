using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputSystem : MonoBehaviour
{
    private MyInputs inputs;
    private LookInputSource currentLookSource = LookInputSource.Mouse;

    private void Awake()
    {
        inputs = new MyInputs();

        // Movement
        inputs.Player.MOVE.performed += OnMovePerformed;
        inputs.Player.MOVE.canceled  += OnMoveCanceled;

        // Look
        inputs.Player.LOOK.performed += OnLookPerformed;
        inputs.Player.LOOK.canceled  += OnLookCanceled;

        // Button inputs
        inputs.Player.ATTACK.performed += OnAttackInput;
        inputs.Player.ATTACK.canceled  += OnAttackInput;
        
        inputs.Player.ITEM1.performed += OnItemOneInput;
        inputs.Player.ITEM1.canceled  += OnItemOneInput;
        
        inputs.Player.ITEM2.performed += OnItemTwoInput;
        inputs.Player.ITEM2.canceled  += OnItemTwoInput;
        
        inputs.Player.INTERACT.performed += OnInteractDodgeInput;
        inputs.Player.INTERACT.canceled  += OnInteractDodgeInput;
        
        inputs.Player.TARGET.performed += OnTargetInput;
        inputs.Player.TARGET.canceled  += OnTargetInput;
        
        inputs.Player.INVENTORY.performed += OnInventoryInput;
        inputs.Player.INVENTORY.canceled  += OnInventoryInput;
        
        inputs.Player.PAUSE.performed += OnPauseInput;
        inputs.Player.PAUSE.canceled  += OnPauseInput;

        Debug.Log("[InputSystem] Initialized");
    }

    void OnEnable()  => inputs.Player.Enable();
    void OnDisable() => inputs.Player.Disable();

    // ========================================================================
    // LOOK INPUT
    // ========================================================================

    private void OnLookPerformed(InputAction.CallbackContext context)
    {
        currentLookSource = context.control.device is Gamepad
            ? LookInputSource.Gamepad
            : LookInputSource.Mouse;

        EventBus.Raise(new OnLookInputEvent()
        {
            pressed = true,
            Delta   = context.ReadValue<Vector2>(),
            Source  = currentLookSource
        });
    }

    private void OnLookCanceled(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnLookInputEvent()
        {
            pressed = false,
            Delta   = Vector2.zero,
            Source  = currentLookSource
        });
    }

    // ========================================================================
    // MOVEMENT INPUT
    // ========================================================================

    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnMoveInputEvent()
        {
            pressed   = true,
            Direction = context.ReadValue<Vector2>()
        });
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnMoveInputEvent()
        {
            pressed   = false,
            Direction = Vector2.zero
        });
    }

    // ========================================================================
    // BUTTON INPUTS
    // ========================================================================

    private void OnAttackInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnAttackInputEvent()      { pressed = context.performed });
    }

    private void OnItemOneInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnItemOneInputEvent()     { pressed = context.performed });
    }

    private void OnItemTwoInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnItemTwoInputEvent()     { pressed = context.performed });
    }

    private void OnInteractDodgeInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnInteractDodgeInputEvent() { pressed = context.performed });
    }

    private void OnTargetInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnTargetInputEvent()      { pressed = context.performed });
    }

    private void OnInventoryInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnInventoryInputEvent()   { pressed = context.performed });
    }

    private void OnPauseInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnPauseInputEvent()       { pressed = context.performed });
    }
}