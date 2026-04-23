using UnityEngine;

public class CameraTargetController : MonoBehaviour
{
    [Header("Sensitivity")]
    public float mouseSensitivity = 0.15f;
    public float gamepadSensitivity = 120f;

    [Header("Vertical Clamp")]
    public float minPitch = -40f;
    public float maxPitch = 70f;

    // -------------------------------------------------------
    private float pitch = 0f; // rotación vertical acumulada

    void OnEnable()  => EventBus.Subscribe<OnLookInputEvent>(OnLook);
    void OnDisable() => EventBus.Unsubscribe<OnLookInputEvent>(OnLook);

    private void OnLook(OnLookInputEvent e)
    {
        float sens = e.Source == LookInputSource.Gamepad
            ? gamepadSensitivity * Time.deltaTime
            : mouseSensitivity;

        // Yaw (horizontal) → rotamos el CameraTarget en Y (world space)
        float yaw = e.Delta.x * sens;
        transform.Rotate(Vector3.up, yaw, Space.World);

        // Pitch (vertical) → clampado para no dar volteretas
        pitch -= e.Delta.y * sens;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        
        // Aplica solo la rotación vertical preservando el yaw actual
        Vector3 euler = transform.eulerAngles;
        transform.eulerAngles = new Vector3(pitch, euler.y, 0f);
    }
}