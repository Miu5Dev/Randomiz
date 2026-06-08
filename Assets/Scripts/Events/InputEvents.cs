using UnityEngine;

public abstract class InputEventBase
{
    public bool pressed;
}

public enum LookInputSource
{
    Mouse,
    Gamepad
}

// ============================================================================
// INPUT EVENTS
// ============================================================================

public class OnMoveInputEvent : InputEventBase
{
    public Vector2 Direction;
}

public class OnLookInputEvent : InputEventBase
{
    public Vector2 Delta;
    public LookInputSource Source;
}

public class OnAttackInputEvent : InputEventBase
{
}

public class OnItemOneInputEvent : InputEventBase
{
}

public class OnItemTwoInputEvent : InputEventBase
{
}

public class OnInteractDodgeInputEvent : InputEventBase
{
}

public class OnTargetInputEvent : InputEventBase
{
}

public class OnInventoryInputEvent : InputEventBase
{
}

// OnPauseInputEvent lives in Assets/Scripts/UI/PauseMenu/OnPauseInputEvent.cs
// (namespace Randomiz.Events) — do not redeclare here.
