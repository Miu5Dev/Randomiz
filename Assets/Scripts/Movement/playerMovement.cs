using UnityEngine;

[RequireComponent(typeof(PhysicsController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    public Transform modelTransform;
    public Transform cameraTarget;
    
    [Header("Movement")]
    public float moveSpeed = 4f;
    public float runSpeed = 8f;
    public float rotationSpeed = 15f;

    [Header("Run Acceleration")]
    public float runAcceleration = 5f;
    [Range(0.85f, 1f)] public float runThreshold = 0.9f;

    [Header("Dash / Roll")]
    public float dashSpeed = 18f;
    public float dashDuration = 0.2f;
    public float dashCooldown = 0.8f;
    public AnimationCurve dashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Targeting Jump / Backflip")]
    public TargetingSystem targetingSystem;
    public float targetingJumpForce = 8f;
    public float backflipHorizontalForce = 6f;
    public float targetingForwardJumpForce = 4f;

    [Header("Gravity")]
    public float gravity = -20f;
    public float groundedGravity = -2f;

    [Header("Air Control")]
    [Range(0f, 1f)] public float airControlFactor = 0.1f;
    public float airDrag = 2f;

    [Header("Wallhug")]
    public float wallhugJumpForce = 6f;
    [Range(0f, 1f)] public float wallhugExitThreshold = 0.3f;

    private PhysicsController physics;
    private Vector3 velocity;
    private float currentSpeed;
    private float baseMoveSpeed;
    private float baseRunSpeed;

    private Vector2 moveInput;
    private Vector2 lastTargetingInput;     // Para detectar última tecla
    private Vector2 lastTargetingMoveDir;   // Última dirección cardinal seleccionada
    private bool dashPressed;

    private bool isDashing;
    private float dashTimer;
    private float dashCooldownTimer;
    private Vector3 dashDirection;
    private bool isJumping;

    private bool isWallhugging;
    private Vector3 wallNormal;
    private bool isWallJumping;
    private Vector3 wallJumpNormal;

    void Awake()
    {
        physics = GetComponent<PhysicsController>();
        currentSpeed = moveSpeed;
        baseMoveSpeed = moveSpeed;
        baseRunSpeed = runSpeed;

        if (targetingSystem == null) TryGetComponent(out targetingSystem);
        if (modelTransform == null)
        {
            modelTransform = transform;
            Debug.LogWarning("[PlayerMovement] modelTransform not assigned.");
        }
        if (cameraTarget == null) Debug.LogError("[PlayerMovement] Assign CameraTarget.");

        lastTargetingInput = Vector2.zero;
        lastTargetingMoveDir = Vector2.zero;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OnMove(Vector2 direction) => moveInput = direction;
    public void OnDash(bool pressed) => dashPressed = pressed;

    void FixedUpdate()
    {
        GroundInfo ground = physics.Ground;
        if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.fixedDeltaTime;
        if (isJumping && ground.isGrounded && velocity.y <= 0f) { isJumping = false; isWallJumping = false; }

        // Salto wallhug tiene prioridad (solo arriba, sin velocidad horizontal)
        if (dashPressed && isWallhugging)
        {
            dashPressed = false;
            StartWallhugJump();
            physics.Move(velocity * Time.fixedDeltaTime);
            return;
        }
        // Iniciar dash / salto targeting
        else if (dashPressed && !isDashing && dashCooldownTimer <= 0f && ground.isGrounded && !isJumping && !isWallhugging)
        {
            dashPressed = false;
            if (targetingSystem != null && targetingSystem.IsTargeting)
                StartTargetingJump();
            else
                StartDash();
        }
        else if (dashPressed) dashPressed = false;

        // Durante el dash
        if (isDashing)
        {
            dashTimer += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(dashTimer / dashDuration);
            float speedMultiplier = dashCurve.Evaluate(t);
            velocity.x = dashDirection.x * dashSpeed * speedMultiplier;
            velocity.z = dashDirection.z * dashSpeed * speedMultiplier;
            velocity.y = ground.isGrounded ? groundedGravity : velocity.y;
            if (dashTimer >= dashDuration)
            {
                isDashing = false;
                dashCooldownTimer = dashCooldown;
            }
            physics.Move(velocity * Time.fixedDeltaTime);
            return;
        }

        bool isTargeting = targetingSystem != null && targetingSystem.IsTargeting;

        // Calcular ejes de movimiento (forward/right)
        Vector3 forwardAxis, rightAxis;
        if (isTargeting)
        {
            Vector3 facing;
            if (targetingSystem.CurrentTarget != null)
            {
                facing = targetingSystem.CurrentTarget.position - transform.position;
                facing.y = 0f;
                if (facing.sqrMagnitude < 0.0001f) facing = modelTransform.forward;
            }
            else
            {
                float yawNoTarget = cameraTarget != null ? cameraTarget.eulerAngles.y : modelTransform.eulerAngles.y;
                facing = Quaternion.Euler(0f, yawNoTarget, 0f) * Vector3.forward;
                modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, Quaternion.LookRotation(facing, Vector3.up), rotationSpeed * Time.fixedDeltaTime);
            }
            forwardAxis = facing.normalized;
            rightAxis = Vector3.Cross(Vector3.up, forwardAxis);
        }
        else
        {
            float yaw = cameraTarget != null ? cameraTarget.eulerAngles.y : 0f;
            Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);
            forwardAxis = yawOnly * Vector3.forward;
            rightAxis = yawOnly * Vector3.right;
        }

        // ✅ Prioridad de la última tecla (movimiento cardinal sin diagonales)
        Vector2 rawInput = moveInput;
        Vector2 cardinalInput = Vector2.zero;

        if (isTargeting)
        {
            bool xActive = Mathf.Abs(rawInput.x) > 0.01f;
            bool yActive = Mathf.Abs(rawInput.y) > 0.01f;

            if (!xActive && !yActive)
            {
                cardinalInput = Vector2.zero;
            }
            else if (xActive && !yActive)
            {
                cardinalInput = new Vector2(Mathf.Sign(rawInput.x), 0f);
            }
            else if (!xActive && yActive)
            {
                cardinalInput = new Vector2(0f, Mathf.Sign(rawInput.y));
            }
            else // Ambos activos
            {
                float deltaX = Mathf.Abs(rawInput.x - lastTargetingInput.x);
                float deltaY = Mathf.Abs(rawInput.y - lastTargetingInput.y);
                if (deltaX > deltaY)
                    cardinalInput = new Vector2(Mathf.Sign(rawInput.x), 0f);
                else if (deltaY > deltaX)
                    cardinalInput = new Vector2(0f, Mathf.Sign(rawInput.y));
                else
                    cardinalInput = lastTargetingMoveDir; // mantiene la anterior
            }

            lastTargetingInput = rawInput;
            lastTargetingMoveDir = cardinalInput;
        }
        else
        {
            // Fuera de targeting, movimiento normal (diagonales permitidas)
            cardinalInput = rawInput;
        }

        // Wallhug tick (return early, gestiona su propio Move)
        if (isWallhugging)
        {
            TickWallhug(ground, forwardAxis, rightAxis, cardinalInput);
            return;
        }
        // Intentar entrar a wallhug si el jugador camina hacia una pared
        if (!isJumping) TryEnterWallhug(ground, forwardAxis, rightAxis, cardinalInput);

        Vector3 moveDir = forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x;

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

        if (isWallJumping)
        {
            Vector3 faceWall = new Vector3(-wallJumpNormal.x, 0f, -wallJumpNormal.z);
            if (faceWall.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(faceWall.normalized, Vector3.up);
                modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
            }
        }
        else if (!isTargeting) HandleRotation(moveDir);

        if (!ground.isGrounded)
            velocity.y += gravity * Time.fixedDeltaTime;
        else if (velocity.y < 0f)
            velocity.y = groundedGravity;

        MoveResult result = physics.Move(velocity * Time.fixedDeltaTime);
        if (result.HitCeiling() && velocity.y > 0f) velocity.y = 0f;
    }

    private void StartDash()
    {
        float yaw = cameraTarget != null ? cameraTarget.eulerAngles.y : 0f;
        Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);
        if (moveInput.sqrMagnitude > 0.01f)
        {
            Vector3 camForward = yawOnly * Vector3.forward;
            Vector3 camRight = yawOnly * Vector3.right;
            dashDirection = (camForward * moveInput.y + camRight * moveInput.x).normalized;
        }
        else dashDirection = modelTransform.forward;
        modelTransform.rotation = Quaternion.LookRotation(dashDirection, Vector3.up);
        isDashing = true;
        dashTimer = 0f;
        velocity.y = 0f;
    }

    private void StartTargetingJump()
    {
        Vector3 direction;
        if (moveInput.magnitude < 0.1f) direction = modelTransform.forward;
        else
        {
            Vector2 cardinal = Mathf.Abs(moveInput.x) > Mathf.Abs(moveInput.y)
                ? new Vector2(Mathf.Sign(moveInput.x), 0f)
                : new Vector2(0f, Mathf.Sign(moveInput.y));
            direction = (modelTransform.forward * cardinal.y + modelTransform.right * cardinal.x).normalized;
        }
        Vector3 horizontal = direction * dashSpeed;
        velocity.x = horizontal.x;
        velocity.z = horizontal.z;
        velocity.y = targetingJumpForce;
        isJumping = true;
        dashCooldownTimer = dashCooldown;
    }

    private void HandleRotation(Vector3 moveDir)
    {
        if (moveDir.sqrMagnitude <= 0.01f) return;
        Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
        modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
    }

    private void TickWallhug(GroundInfo ground, Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput)
    {
        // Verificar que la pared sigue ahí
        CollisionInfo wallCheck = physics.CheckDirection(-wallNormal, 0.15f);
        if (!wallCheck.hit || !wallCheck.IsWall(physics.maxGroundAngle))
        {
            isWallhugging = false;
            ApplyGravityAndMove(ground);
            return;
        }
        wallNormal = wallCheck.normal;

        // Salir si se cayó del borde (ni saltando ni en suelo)
        if (!ground.isGrounded && !isJumping)
        {
            isWallhugging = false;
            ApplyGravityAndMove(ground);
            return;
        }

        // Salir si el input apunta en dirección contraria a la pared
        if (cardinalInput.sqrMagnitude > 0.01f)
        {
            Vector3 inputDir = (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized;
            if (Vector3.Dot(inputDir, wallNormal) > wallhugExitThreshold)
            {
                isWallhugging = false;
                ApplyGravityAndMove(ground);
                return;
            }
        }

        // Proyectar movimiento sobre el plano de la pared (solo componente horizontal)
        Vector3 moveDir = Vector3.zero;
        if (cardinalInput.sqrMagnitude > 0.01f)
        {
            Vector3 rawDir = forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x;
            Vector3 projected = Vector3.ProjectOnPlane(rawDir, wallNormal);
            projected.y = 0f;
            if (projected.sqrMagnitude > 0.01f)
                moveDir = projected.normalized;
        }

        float inputMagnitude = moveInput.magnitude;
        float targetSpeed = (inputMagnitude >= runThreshold) ? runSpeed : moveSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, runAcceleration * Time.fixedDeltaTime);

        velocity.x = moveDir.x * currentSpeed;
        velocity.z = moveDir.z * currentSpeed;

        // Mirar hacia la pared
        Vector3 faceWall = new Vector3(-wallNormal.x, 0f, -wallNormal.z);
        if (faceWall.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(faceWall.normalized, Vector3.up);
            modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
        }

        ApplyGravityAndMove(ground);
    }

    private void TryEnterWallhug(GroundInfo ground, Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput)
    {
        if (!ground.isGrounded || isDashing) return;
        if (cardinalInput.sqrMagnitude < 0.01f) return;

        Vector3 inputDir = (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized;
        CollisionInfo wallCheck = physics.CheckDirection(inputDir, 0.1f);
        if (wallCheck.hit && wallCheck.IsWall(physics.maxGroundAngle))
        {
            isWallhugging = true;
            wallNormal = wallCheck.normal;
        }
    }

    private void StartWallhugJump()
    {
        velocity.x = 0f;
        velocity.z = 0f;
        velocity.y = wallhugJumpForce;
        isJumping = true;
        isWallJumping = true;
        wallJumpNormal = wallNormal;
        isWallhugging = false;
        dashCooldownTimer = dashCooldown;
    }

    private void ApplyGravityAndMove(GroundInfo ground)
    {
        if (!ground.isGrounded)
            velocity.y += gravity * Time.fixedDeltaTime;
        else if (velocity.y < 0f)
            velocity.y = groundedGravity;

        MoveResult result = physics.Move(velocity * Time.fixedDeltaTime);
        if (result.HitCeiling() && velocity.y > 0f) velocity.y = 0f;
    }

    public bool IsDashing => isDashing;
    public bool IsWallhugging => isWallhugging;
    public Vector3 WallNormal => wallNormal;
    public bool IsWallJumping => isWallJumping;
    public Vector3 WallJumpNormal => wallJumpNormal;
    public float DashCooldownNormalized => Mathf.Clamp01(dashCooldownTimer / dashCooldown);
    public void onItemEquip(OnItemEquipEvent e) => ChangeMoveSpeed(e.item.handWeightMultiplier);
    public void OnItemUnequip(OnItemUnequipEvent e) => ResetMoveSpeed();
    public void ChangeMoveSpeed(float multiplier) { moveSpeed = baseMoveSpeed * multiplier; runSpeed = baseRunSpeed * multiplier; }
    public void ResetMoveSpeed() { moveSpeed = baseMoveSpeed; runSpeed = baseRunSpeed; }
    public Vector2 MoveInput => moveInput;
    public Transform CameraTarget => cameraTarget;
}