using UnityEngine;

[RequireComponent(typeof(PhysicsController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Hijo del Player que contiene la malla. NUNCA el root.")]
    public Transform modelTransform;

    [Tooltip("Asigna el CameraTarget (NO la cámara). Solo se usa su yaw (eje Y).")]
    public Transform cameraTarget;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 15f;

    [Header("Jump")]
    public float jumpForce = 8f;

    [Header("Gravity")]
    public float gravity = -20f;
    public float groundedGravity = -2f;

    // PRIVADOS
    private PhysicsController physics;
    private Vector3 velocity;

    private Vector2 moveInput;
    private bool jumpPressed;

    void Awake()
    {
        physics = GetComponent<PhysicsController>();

        if (modelTransform == null)
        {
            modelTransform = transform;
            Debug.LogWarning("[PlayerMovement] modelTransform no asignado. Asigna el hijo 'Model' para evitar el giro en círculos.");
        }

        if (cameraTarget == null)
            Debug.LogError("[PlayerMovement] Asigna el CameraTarget en el inspector.");

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    

    public void OnMove(Vector2 direction) => moveInput = direction;
    public void OnJump(bool pressed) => jumpPressed = pressed;

    void FixedUpdate()
    {
        GroundInfo ground = physics.Ground;

        // Solo el yaw del CameraTarget — ignora el pitch por completo
        // Así A/D siempre se mueven perpendicular al forward, sin importar
        // cuánto mire la cámara hacia arriba o abajo.
        float yaw = cameraTarget != null ? cameraTarget.eulerAngles.y : 0f;
        Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);

        Vector3 camForward = yawOnly * Vector3.forward;
        Vector3 camRight   = yawOnly * Vector3.right;

        Vector3 moveDir = camForward * moveInput.y + camRight * moveInput.x;

        velocity.x = moveDir.x * moveSpeed;
        velocity.z = moveDir.z * moveSpeed;

        HandleRotation(moveDir);

        if (jumpPressed && ground.isGrounded)
            velocity.y = jumpForce;

        if (!ground.isGrounded)
            velocity.y += gravity * Time.fixedDeltaTime;
        else if (velocity.y < 0f)
            velocity.y = groundedGravity;

        MoveResult result = physics.Move(velocity * Time.fixedDeltaTime);

        if (result.HitCeiling() && velocity.y > 0f)
            velocity.y = 0f;
    }

    private void HandleRotation(Vector3 moveDir)
    {
        if (moveDir.sqrMagnitude <= 0.01f) return;

        Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);

        modelTransform.rotation = Quaternion.Slerp(
            modelTransform.rotation,
            targetRot,
            rotationSpeed * Time.fixedDeltaTime
        );
    }
}
