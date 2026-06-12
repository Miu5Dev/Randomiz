using UnityEngine;

/// <summary>Base class for all input events raised by InputSystem; <c>pressed</c> is true on press, false on release.</summary>
public abstract class InputEventBase
{
    public bool pressed;
}

/// <summary>Identifies whether a look/aim delta originated from the mouse or a gamepad stick.</summary>
public enum LookInputSource
{
    Mouse,
    Gamepad
}

// ============================================================================
// INPUT EVENTS
// ============================================================================

/// <summary>Movement input; <c>Direction</c> is the desired move vector (camera-relative).</summary>
public class OnMoveInputEvent : InputEventBase
{
    public Vector2 Direction;
}

/// <summary>Camera look / free-aim input; <c>Delta</c> plus its <c>Source</c> (mouse or gamepad).</summary>
public class OnLookInputEvent : InputEventBase
{
    public Vector2 Delta;
    public LookInputSource Source;
}

/// <summary>Attack button input.</summary>
public class OnAttackInputEvent : InputEventBase
{
}

/// <summary>Quickslot 1 input (Q / gamepad north).</summary>
public class OnItemOneInputEvent : InputEventBase
{
}

/// <summary>Quickslot 2 input (E / gamepad east).</summary>
public class OnItemTwoInputEvent : InputEventBase
{
}

/// <summary>Combined interact / dodge-dash / wallhug input (Space / gamepad south).</summary>
public class OnInteractDodgeInputEvent : InputEventBase
{
}

/// <summary>Z-targeting toggle input.</summary>
public class OnTargetInputEvent : InputEventBase
{
}

/// <summary>Inventory wheel toggle input (Tab / gamepad select).</summary>
public class OnInventoryInputEvent : InputEventBase
{
}

// OnPauseInputEvent lives in Assets/Scripts/UI/PauseMenu/OnPauseInputEvent.cs
// (namespace Randomiz.Events) — do not redeclare here.
