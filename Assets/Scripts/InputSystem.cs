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
        inputs.Player.Move.canceled += OnMoveCanceled;

        // Look — detectar dispositivo en performed para saber cuál está activo
        inputs.Player.Look.performed += OnLookDeviceDetect;

        // Button inputs
        inputs.Player.Action.performed += OnActionInput;
        inputs.Player.Action.canceled += OnActionInput;
        inputs.Player.Jump.performed += OnJumpInput;
        inputs.Player.Jump.canceled += OnJumpInput;
        inputs.Player.Crouch.performed += OnCrouchInput;
        inputs.Player.Crouch.canceled += OnCrouchInput;
        inputs.Player.Swap.performed += OnSwapInput;
        inputs.Player.Swap.canceled += OnSwapInput;

        Debug.Log("[InputSystem] Initialized");
    }

    void OnEnable()
    {
        inputs.Player.Enable();
    }

    void OnDisable()
    {
        inputs.Player.Disable();
    }

    private void OnLookDeviceDetect(InputAction.CallbackContext context)
    {
        // Detectar si el input viene de mouse o gamepad
        var device = context.control.device;

        if (device is Gamepad)
            currentLookSource = LookInputSource.Gamepad;
        else
            currentLookSource = LookInputSource.Mouse;
    }

    void Update()
    {
        Vector2 lookValue = inputs.Player.Look.ReadValue<Vector2>();
        EventBus.Raise(new OnLookInputEvent()
        {
            pressed = lookValue.sqrMagnitude > 0.01f,
            Delta = lookValue,
            Source = currentLookSource
        });
    }

    // ========================================================================
    // MOVEMENT INPUT
    // ========================================================================

    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnMoveInputEvent()
        {
            pressed = context.performed,
            Direction = context.ReadValue<Vector2>()
        });
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnMoveInputEvent()
        {
            pressed = context.performed,
            Direction = context.ReadValue<Vector2>()
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

    private void OnCrouchInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnCrouchInputEvent()
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

    private void OnSwapInput(InputAction.CallbackContext context)
    {
        EventBus.Raise(new OnSwapInputEvent()
        {
            pressed = context.performed
        });
    }
}