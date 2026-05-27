using UnityEngine;

/// <summary>
/// Rotates the camera-target transform (yaw + pitch) in response to look input.
/// Self-subscribes to OnLookInputEvent — no inspector binding required.
/// Honors OnSetCameraEnabledEvent: while disabled, look input is ignored.
/// </summary>
public class CameraTargetController : MonoBehaviour
{
    [Header("Sensitivity")]
    public float mouseSensitivity = 0.15f;
    public float gamepadSensitivity = 120f;

    [Header("Vertical clamp")]
    public float minPitch = -40f;
    public float maxPitch = 70f;

    private float pitch = 0f;        // accumulated vertical rotation
    private bool lookEnabled = true; // toggled by OnSetCameraEnabledEvent

    private void OnEnable()
    {
        EventBus.Subscribe<OnLookInputEvent>(OnLook);
        EventBus.Subscribe<OnSetCameraEnabledEvent>(OnSetCameraEnabled);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnLookInputEvent>(OnLook);
        EventBus.Unsubscribe<OnSetCameraEnabledEvent>(OnSetCameraEnabled);
    }

    private void OnSetCameraEnabled(OnSetCameraEnabledEvent e) => lookEnabled = e.enabled;

    private void OnLook(OnLookInputEvent e)
    {
        if (!lookEnabled) return;

        float sens = e.Source == LookInputSource.Gamepad
            ? gamepadSensitivity * Time.deltaTime
            : mouseSensitivity;

        // Yaw — rotate around world up.
        float yaw = e.Delta.x * sens;
        transform.Rotate(Vector3.up, yaw, Space.World);

        // Pitch — accumulated and clamped to avoid flipping.
        pitch -= e.Delta.y * sens;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // Apply pitch while preserving current yaw.
        Vector3 euler = transform.eulerAngles;
        transform.eulerAngles = new Vector3(pitch, euler.y, 0f);
    }
}
