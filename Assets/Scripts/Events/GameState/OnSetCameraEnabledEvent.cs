/// <summary>
/// Toggles camera look input on or off.
/// CameraTargetController honors this — while disabled it ignores OnLookInputEvent
/// so the camera stays frozen. Useful for cutscenes / fixed-camera moments.
/// </summary>
public class OnSetCameraEnabledEvent
{
    public bool enabled;
}
