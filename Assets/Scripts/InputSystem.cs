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
        inputs.Player.Move.performed += OnMovePerformed;
        inputs.Player.Move.canceled  += OnMoveCanceled;

        // Look
        inputs.Player.Look.performed += OnLookPerformed;
        inputs.Player.Look.canceled  += OnLookCanceled;

        // Button inputs
        inputs.Player.Action.performed += OnActionInput;
        inputs.Player.Action.canceled  += OnActionInput;
        inputs.Player.Jump.performed   += OnJumpInput;
        inputs.Player.Jump.canceled    += OnJumpInput;
        inputs.Player.Crouch.performed += OnCrouchInput;
        inputs.Player.Crouch.canceled  += OnCrouchInput;
        inputs.Player.Swap.performed   += OnSwapInput;
        inputs.Player.Swap.canceled    += OnSwapInput;

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

    private void OnActionInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnActionInputEvent()
        {
            pressed = context.performed
        });
    }

    private void OnJumpInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnJumpInputEvent()
        {
            pressed = context.performed
        });
    }

    private void OnCrouchInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnCrouchInputEvent()
        {
            pressed = context.performed
        });
    }

    private void OnSwapInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnSwapInputEvent()
        {
            pressed = context.performed
        });
    }
}