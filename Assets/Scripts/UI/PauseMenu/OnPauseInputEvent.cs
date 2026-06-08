/// <summary>
/// Raised by InputSystem when the player presses or releases the Pause button.
/// pressed = true  → button down (open menu).
/// pressed = false → button up.
/// Inherits InputEventBase so it is consistent with all other input events.
/// </summary>
public class OnPauseInputEvent : InputEventBase { }
