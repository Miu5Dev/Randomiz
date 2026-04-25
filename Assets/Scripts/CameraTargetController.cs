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
    
    public void OnLook(LookInputSource Source , Vector2 Delta)
    {
        float sens = Source == LookInputSource.Gamepad
            ? gamepadSensitivity * Time.deltaTime
            : mouseSensitivity;

        // Yaw (horizontal) → rotamos el CameraTarget en Y (world space)
        float yaw = Delta.x * sens;
        transform.Rotate(Vector3.up, yaw, Space.World);

        // Pitch (vertical) → clampado para no dar volteretas
        pitch -= Delta.y * sens;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        
        // Aplica solo la rotación vertical preservando el yaw actual
        Vector3 euler = transform.eulerAngles;
        transform.eulerAngles = new Vector3(pitch, euler.y, 0f);
    }
}