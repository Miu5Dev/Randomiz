using UnityEngine;

[RequireComponent(typeof(PhysicsController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Child of the Player that holds the mesh. NEVER the root.")]
    public Transform modelTransform;

    [Tooltip("Assign the CameraTarget (NOT the camera). Only its yaw (Y axis) is used.")]
    public Transform cameraTarget;
    
    [Header("Movement")]
    public float moveSpeed = 4f;
    public float runSpeed = 8f;
    public float rotationSpeed = 15f;

    [Header("Run Acceleration")]
    [Tooltip("How fast the speed transitions between moveSpeed and runSpeed.")]
    public float runAcceleration = 5f;

    [Range(0.85f, 1f)]
    public float runThreshold = 0.9f;

    [Header("Dash / Roll")]
    [Tooltip("Velocidad máxima del dash.")]
    public float dashSpeed = 18f;

    [Tooltip("Duración total del dash en segundos.")]
    public float dashDuration = 0.2f;

    [Tooltip("Cooldown tras el dash (segundos).")]
    public float dashCooldown = 0.8f;

    [Tooltip("Curva de velocidad del dash. X = tiempo normalizado (0-1), Y = multiplicador de dashSpeed.")]
    public AnimationCurve dashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Targeting Jump / Backflip")]
    [Tooltip("Optional TargetingSystem reference. If assigned, pressing dash while targeting becomes a backflip / forward jump instead of a dash.")]
    public TargetingSystem targetingSystem;

    [Tooltip("Vertical impulse used by the jump and backflip while targeting.")]
    public float targetingJumpForce = 8f;

    [Tooltip("Horizontal impulse used by the backflip (opposite to the target). Set 0 for a pure vertical hop.")]
    public float backflipHorizontalForce = 6f;

    [Tooltip("Horizontal impulse used when targeting is active but there is no current target. Set 0 for a vertical jump.")]
    public float targetingForwardJumpForce = 4f;

    [Header("Gravity")]
    public float gravity = -20f;
    public float groundedGravity = -2f;

    [Header("Air Control")]
    [Range(0f, 1f)]
    public float airControlFactor = 0.1f;
    public float airDrag = 2f;

    // PRIVATE
    private PhysicsController physics;
    private Vector3 velocity;
    private float currentSpeed;
    private float baseMoveSpeed;
    private float baseRunSpeed;

    private Vector2 moveInput;
    private bool dashPressed;

    // Dash state
    private bool isDashing;
    private float dashTimer;
    private float dashCooldownTimer;
    private Vector3 dashDirection;

    void Awake()
    {
        physics = GetComponent<PhysicsController>();
        currentSpeed = moveSpeed;

        baseMoveSpeed = moveSpeed;
        baseRunSpeed  = runSpeed;

        // Auto-resolve TargetingSystem if it lives on the same GameObject and was not wired up in the Inspector.
        if (targetingSystem == null)
            TryGetComponent(out targetingSystem);

        if (modelTransform == null)
        {
            modelTransform = transform;
            Debug.LogWarning("[PlayerMovement] modelTransform not assigned.");
        }

        if (cameraTarget == null)
            Debug.LogError("[PlayerMovement] Assign the CameraTarget in the Inspector.");

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OnMove(Vector2 direction) => moveInput = direction;

    /// <summary>Llama esto desde tu InputHandler cuando se pulse el botón de dash.</summary>
    public void OnDash(bool pressed) => dashPressed = pressed;

    void FixedUpdate()
    {
        GroundInfo ground = physics.Ground;

        // Cooldown tick
        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.fixedDeltaTime;

        // ── Iniciar dash ──────────────────────────────────────────────────
        if (dashPressed && !isDashing && dashCooldownTimer <= 0f)
        {
            dashPressed = false;

            // While targeting, the dash button performs a backflip / forward jump instead of a normal dash.
            if (targetingSystem != null && targetingSystem.IsTargeting && ground.isGrounded)
                StartTargetingJump();
            else
                StartDash();
        }

        // ── Durante el dash ───────────────────────────────────────────────
        if (isDashing)
        {
            dashTimer += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(dashTimer / dashDuration);
            float speedMultiplier = dashCurve.Evaluate(t);

            velocity.x = dashDirection.x * dashSpeed * speedMultiplier;
            velocity.z = dashDirection.z * dashSpeed * speedMultiplier;

            // Gravedad mínima durante el dash (se mantiene pegado al suelo)
            velocity.y = ground.isGrounded ? groundedGravity : velocity.y;

            if (dashTimer >= dashDuration)
            {
                isDashing = false;
                dashCooldownTimer = dashCooldown;
            }

            physics.Move(velocity * Time.fixedDeltaTime);
            return; // Saltar el resto del movimiento mientras dasheas
        }

        // ── Movimiento normal ─────────────────────────────────────────────
        float yaw = cameraTarget != null ? cameraTarget.eulerAngles.y : 0f;
        Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);

        Vector3 camForward = yawOnly * Vector3.forward;
        Vector3 camRight   = yawOnly * Vector3.right;

        Vector3 moveDir = camForward * moveInput.y + camRight * moveInput.x;

        float inputMagnitude = moveInput.magnitude;
        float targetSpeed = (inputMagnitude >= runThreshold) ? runSpeed : moveSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, runAcceleration * Time.fixedDeltaTime);

        bool hasInput = moveInput.sqrMagnitude > 0.01f;

        if (ground.isGrounded)
        {
            velocity.x = moveDir.x * currentSpeed;
            velocity.z = moveDir.z * currentSpeed;
        }
        else
        {
            if (hasInput)
            {
                velocity.x = Mathf.Lerp(velocity.x, moveDir.x * currentSpeed, airControlFactor);
                velocity.z = Mathf.Lerp(velocity.z, moveDir.z * currentSpeed, airControlFactor);
            }
            else
            {
                float drag = airDrag * Time.fixedDeltaTime;
                velocity.x = Mathf.Lerp(velocity.x, 0f, drag);
                velocity.z = Mathf.Lerp(velocity.z, 0f, drag);
            }
        }

        HandleRotation(moveDir);

        if (!ground.isGrounded)
            velocity.y += gravity * Time.fixedDeltaTime;
        else if (velocity.y < 0f)
            velocity.y = groundedGravity;

        MoveResult result = physics.Move(velocity * Time.fixedDeltaTime);

        if (result.HitCeiling() && velocity.y > 0f)
            velocity.y = 0f;
    }

    private void StartDash()
    {
        // Dirección: hacia donde nos movemos; si estamos quietos, hacia donde miramos
        float yaw = cameraTarget != null ? cameraTarget.eulerAngles.y : 0f;
        Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);

        if (moveInput.sqrMagnitude > 0.01f)
        {
            Vector3 camForward = yawOnly * Vector3.forward;
            Vector3 camRight   = yawOnly * Vector3.right;
            dashDirection = (camForward * moveInput.y + camRight * moveInput.x).normalized;
        }
        else
        {
            // Sin input → dash hacia donde mira el modelo
            dashDirection = modelTransform.forward;
        }

        // Snap de rotación al inicio del dash
        modelTransform.rotation = Quaternion.LookRotation(dashDirection, Vector3.up);

        isDashing  = true;
        dashTimer  = 0f;
        velocity.y = 0f; // Cancelar caída al iniciar dash
    }

    private void StartTargetingJump()
    {
        Vector3 inputWorld = modelTransform.forward * moveInput.y + modelTransform.right * moveInput.x;

        Vector3 direction = inputWorld.magnitude < 0.1f
            ? modelTransform.forward
            : inputWorld.normalized;

        Vector3 horizontal = direction * dashSpeed;

        velocity.x = horizontal.x;
        velocity.z = horizontal.z;
        velocity.y = targetingJumpForce;

        dashCooldownTimer = dashCooldown;
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

    /// <summary>
    /// Expone si el jugador está en medio de un dash (útil para el Animator).
    /// </summary>
    public bool IsDashing => isDashing;

    /// <summary>
    /// Devuelve el progreso del cooldown normalizado (0 = listo, 1 = recién usado).
    /// Útil para pintar el icono del dash en la UI.
    /// </summary>
    public float DashCooldownNormalized => Mathf.Clamp01(dashCooldownTimer / dashCooldown);

    public void onItemEquip(OnItemEquipEvent e)
    {
        ChangeMoveSpeed(e.item.handWeightMultiplier);
    }

    public void OnItemUnequip(OnItemUnequipEvent e)
    {
        ResetMoveSpeed();
    }
    
    
    public void ChangeMoveSpeed(float multiplier)
    {
        moveSpeed = baseMoveSpeed * multiplier;
        runSpeed  = baseRunSpeed  * multiplier;
    }

    public void ResetMoveSpeed()
    {
        moveSpeed = baseMoveSpeed;
        runSpeed  = baseRunSpeed;
    }
    
    public Vector2 MoveInput    => moveInput;
    public Transform CameraTarget => cameraTarget;
}