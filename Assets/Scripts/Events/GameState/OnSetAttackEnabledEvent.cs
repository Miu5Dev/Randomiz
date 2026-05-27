/// <summary>
/// Toggles attack/use input on or off (West button action).
/// The attack handler honors this — while disabled, OnAttackInputEvent is ignored.
/// Useful for cutscenes or sheathing weapons.
/// </summary>
public class OnSetAttackEnabledEvent
{
    public bool enabled;
}
