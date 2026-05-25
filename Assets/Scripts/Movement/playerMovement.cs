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
    [Tooltip("Altura mínima que debe tener la pared para entrar a wallhug. Escalones más bajos los maneja auto step-up o movimiento normal.")]
    public float wallhugMinWallHeight = 1.0f;

    [Header("Auto Step Up")]
    [Tooltip("Altura máxima de escalón que el jugador puede subir automáticamente al caminar. Por encima requiere salto / ledge grab.")]
    public float autoStepMaxHeight = 0.5f;
    [Tooltip("Duración de la animación de subida de escalón (segundos).")]
    public float autoStepUpDuration = 0.12f;

    [Header("Ledge Grab")]
    public float ledgeDetectionDistance = 0.6f;
    public float ledgeTopSearchHeight = 0.5f;
    public float ledgeClimbDuration = 0.25f;
    [Tooltip("Máximo escalón hacia ARRIBA que el jugador puede alcanzar al hacer ledge grab.")]
    public float ledgeMaxReachUp = 0.3f;
    [Tooltip("Máximo escalón hacia ABAJO que el jugador puede alcanzar al hacer ledge grab.")]
    public float ledgeMaxReachDown = 0.5f;

    [Header("Ledge Grab - Falloff (auto-agarrar al caerse)")]
    [Tooltip("Caída mínima debajo del jugador para activar auto-grab. Si hay piso cercano, es solo un escalón y no se agarra.")]
    public float falloffMinDropHeight = 1.5f;
    [Tooltip("Velocidad horizontal mínima para activar el auto-grab al caerse de un borde.")]
    public float falloffMinSpeed = 1.0f;

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

    private bool isLedgeGrabbing;
    private Vector3 ledgeTopPoint;
    private Vector3 ledgeWallNormal;
    private bool ledgeBackwardBlocked; // S estaba presionada al entrar; requiere soltar antes de poder salir
    private bool isClimbingLedge;
    private float climbTimer;
    private Vector3 climbStartPos;
    private Vector3 climbEndPos;

    private bool isSteppingUp;
    private float stepUpTimer;

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

        // Bloquear targeting durante ledge grab / climb
        if (targetingSystem != null)
            targetingSystem.Locked = isLedgeGrabbing || isClimbingLedge;

        if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.fixedDeltaTime;
        if (isJumping && ground.isGrounded && velocity.y <= 0f)
        {
            isJumping = false;
            if (isWallJumping)
            {
                isWallJumping = false;
                // Si la pared sigue ahí al aterrizar, volver a wallhug
                CollisionInfo reentry = physics.CheckDirection(-wallJumpNormal, 0.15f);
                if (reentry.hit && reentry.IsWall(physics.maxGroundAngle))
                {
                    isWallhugging = true;
                    wallNormal = reentry.normal;
                }
            }
        }

        // Animación de subida — bloquea todo input
        if (isClimbingLedge)
        {
            dashPressed = false;
            TickLedgeClimb();
            return;
        }

        // Animación de step-up — bloquea todo input
        if (isSteppingUp)
        {
            TickStepUp();
            return;
        }

        // Subir cornisa (ledge grab → climb)
        if (dashPressed && isLedgeGrabbing)
        {
            dashPressed = false;
            StartLedgeClimb();
            return;
        }
        // Salto wallhug tiene prioridad (solo arriba, sin velocidad horizontal)
        else if (dashPressed && isWallhugging)
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
        // Auto step-up: escalones pequeños se suben automáticamente antes de cualquier wallhug
        if (!isJumping) TryAutoStepUp(ground, forwardAxis, rightAxis, cardinalInput);
        // Intentar entrar a wallhug si el jugador camina hacia una pared
        if (!isJumping) TryEnterWallhug(ground, forwardAxis, rightAxis, cardinalInput);

        // Ledge grab tick
        if (isLedgeGrabbing)
        {
            TickLedgeGrab(forwardAxis, rightAxis, cardinalInput);
            return;
        }
        // Detección de cornisa mientras está en el aire
        if (!ground.isGrounded && !isDashing && velocity.y > -8f)
            TryGrabLedge();

        // Si TryGrabLedge enganchó este frame, no ejecutar movimiento normal
        if (isLedgeGrabbing) return;

        Vector3 moveDir = forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x;

        float inputMagnitude = moveInput.magnitude;
        float targetSpeed = (inputMagnitude >= runThreshold) ? runSpeed : moveSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, runAcceleration * Time.fixedDeltaTime);
        bool hasInput = moveInput.sqrMagnitude > 0.01f;

        if (isWallJumping)
        {
            velocity.x = 0f;
            velocity.z = 0f;
        }
        else if (ground.isGrounded)
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

        // Detección de suelo extendida: en ledges estrechos pegados a la pared, el SphereCast
        // estándar puede fallar porque el centro de la esfera queda sobre el vacío. Complementamos
        // con un raycast desde el lado de la pared (justo en la superficie del muro, a nivel de pies)
        // para detectar esas superficies estrechas que el movimiento normal no alcanzaría.
        CapsuleCollider col = physics.Collider;
        bool isGrounded = ground.isGrounded;
        if (!isGrounded)
        {
            Vector3 feetPos = physics.GetFeetPosition();
            // La normal apunta hacia el jugador, así que -wallNormal apunta hacia la pared.
            // Offset = col.radius en esa dirección → queda justo en la superficie del muro.
            Vector3 wallSideProbe = feetPos - wallNormal * col.radius + Vector3.up * 0.05f;
            isGrounded = Physics.Raycast(wallSideProbe, Vector3.down,
                physics.groundCheckDistance + 0.15f, physics.collisionMask, QueryTriggerInteraction.Ignore);
        }

        // Salir si se cayó del borde (ni saltando ni en suelo)
        if (!isGrounded && !isJumping)
        {
            isWallhugging = false;
            ApplyGravityAndMove(ground);
            return;
        }

        // Salir solo si el jugador presiona explícitamente hacia atrás (S).
        // Usar cardinalInput.y evita falsos positivos al girar la cámara (mismo criterio que ledge grab).
        if (cardinalInput.y < -wallhugExitThreshold)
        {
            isWallhugging = false;
            ApplyGravityAndMove(ground);
            return;
        }

        // Movimiento lateral a lo largo de la pared (mismo sistema que ledge grab)
        bool hasInput = cardinalInput.sqrMagnitude > 0.01f;
        Vector3 inputDir3D = hasInput
            ? (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized
            : Vector3.zero;

        Vector3 wallRight = Vector3.Cross(Vector3.up, wallNormal).normalized;
        float lateralInput = hasInput ? Vector3.Dot(inputDir3D, wallRight) : 0f;

        float inputMagnitude = moveInput.magnitude;
        float targetSpeed = (inputMagnitude >= runThreshold) ? runSpeed : moveSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, runAcceleration * Time.fixedDeltaTime);

        Vector3 moveVel = wallRight * (lateralInput * currentSpeed);
        velocity.x = moveVel.x;
        velocity.z = moveVel.z;

        // Mirar hacia la pared
        Vector3 faceWall = new Vector3(-wallNormal.x, 0f, -wallNormal.z);
        if (faceWall.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(faceWall.normalized, Vector3.up);
            modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
        }

        // Gravedad usando la detección extendida (no ApplyGravityAndMove para no perder isGrounded)
        if (!isGrounded)
            velocity.y += gravity * Time.fixedDeltaTime;
        else if (velocity.y < 0f)
            velocity.y = groundedGravity;

        MoveResult result = physics.Move(velocity * Time.fixedDeltaTime);
        if (result.HitCeiling() && velocity.y > 0f) velocity.y = 0f;
    }

    private void TryAutoStepUp(GroundInfo ground, Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput)
    {
        if (!ground.isGrounded || isDashing) return;
        if (cardinalInput.sqrMagnitude < 0.01f) return;

        CapsuleCollider col = physics.Collider;
        Vector3 inputDir = (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized;
        Vector3 feetPos = physics.GetFeetPosition();

        // 1. Buscar pared al frente a nivel de pies
        Vector3 lowProbe = feetPos + Vector3.up * 0.1f;
        float probeDistance = col.radius + 0.15f;
        if (!Physics.Raycast(lowProbe, inputDir, out RaycastHit wallHit,
            probeDistance, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        float wallAngle = Vector3.Angle(wallHit.normal, Vector3.up);
        if (wallAngle < physics.maxGroundAngle || wallAngle >= 135f) return;

        // 2. Buscar la superficie superior del escalón (cast hacia abajo desde encima)
        Vector3 highProbe = wallHit.point + Vector3.up * (autoStepMaxHeight + 0.1f) - wallHit.normal * 0.05f;
        if (!Physics.Raycast(highProbe, Vector3.down, out RaycastHit stepHit,
            autoStepMaxHeight + 0.2f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        // 3. Altura del escalón dentro del rango (positiva y ≤ autoStepMaxHeight)
        float stepHeight = stepHit.point.y - feetPos.y;
        if (stepHeight <= 0.001f || stepHeight > autoStepMaxHeight) return;

        // 4. La superficie superior debe ser caminable
        float stepTopAngle = Vector3.Angle(stepHit.normal, Vector3.up);
        if (stepTopAngle > physics.maxGroundAngle) return;

        // 5. Verificar que el jugador quepa de pie encima del escalón
        Vector3 standingPos = new Vector3(
            stepHit.point.x - wallHit.normal.x * (col.radius + 0.05f),
            stepHit.point.y - col.center.y + col.height * 0.5f + 0.02f,
            stepHit.point.z - wallHit.normal.z * (col.radius + 0.05f)
        );
        Vector3 capCenter = standingPos + col.center;
        Vector3 capBottom = capCenter + Vector3.down * (col.height * 0.5f - col.radius);
        Vector3 capTop    = capCenter + Vector3.up   * (col.height * 0.5f - col.radius);
        if (Physics.CheckCapsule(capBottom, capTop, col.radius - 0.02f,
            physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        // 6. Iniciar animación de subida
        climbStartPos = transform.position;
        climbEndPos = standingPos;
        stepUpTimer = 0f;
        isSteppingUp = true;
    }

    private void TryEnterWallhug(GroundInfo ground, Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput)
    {
        if (!ground.isGrounded || isDashing) return;
        if (cardinalInput.sqrMagnitude < 0.01f) return;

        Vector3 inputDir = (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized;
        CollisionInfo wallCheck = physics.CheckDirection(inputDir, 0.1f);
        if (!wallCheck.hit || !wallCheck.IsWall(physics.maxGroundAngle)) return;

        // Verificar que la pared sea alta. Si es un escalón pequeño, no entramos a wallhug;
        // el movimiento normal debería poderlo manejar.
        Vector3 feetPos = physics.GetFeetPosition();
        Vector3 highProbe = new Vector3(
            wallCheck.point.x + wallCheck.normal.x * 0.05f,
            feetPos.y + wallhugMinWallHeight,
            wallCheck.point.z + wallCheck.normal.z * 0.05f
        );
        if (!Physics.Raycast(highProbe, -wallCheck.normal, 0.2f,
            physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        isWallhugging = true;
        wallNormal = wallCheck.normal;
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

    private void TryGrabLedge()
    {
        Vector3 horizontalVel = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 checkDir;
        if (isWallJumping)
            checkDir = -wallJumpNormal;
        else if (horizontalVel.sqrMagnitude > 0.01f)
            checkDir = horizontalVel.normalized;
        else
            checkDir = new Vector3(modelTransform.forward.x, 0f, modelTransform.forward.z).normalized;

        // Caso normal: cornisa a la altura de la cabeza en la dirección de movimiento
        if (TryDetectLedge(checkDir, out Vector3 ledgeTop, out Vector3 wallNorm))
        {
            AttachToLedge(ledgeTop, wallNorm);
            return;
        }

        // Auto-grab al caer de un bordillo: jugador sin saltar, con velocidad horizontal suficiente,
        // cayendo lentamente (ventana pequeña tras dejar el suelo). Cornisa detrás, a los pies.
        if (!isJumping
            && velocity.y < 0.5f && velocity.y > -4f
            && horizontalVel.magnitude >= falloffMinSpeed)
        {
            Vector3 backDir = -horizontalVel.normalized;
            if (TryDetectLedgeAtFeet(backDir, out ledgeTop, out wallNorm))
            {
                // Verificar que haya caída real por debajo. Si hay piso cercano,
                // es solo un escaloncito y no vale la pena agarrarse.
                if (Physics.Raycast(transform.position, Vector3.down, falloffMinDropHeight,
                    physics.collisionMask, QueryTriggerInteraction.Ignore))
                    return;

                AttachToLedge(ledgeTop, wallNorm);
            }
        }
    }

    private void AttachToLedge(Vector3 top, Vector3 norm)
    {
        isLedgeGrabbing = true;
        isWallJumping = false;
        isJumping = false;
        ledgeTopPoint = top;
        ledgeWallNormal = norm;
        // Si S ya estaba presionada al entrar, bloquear la salida por S hasta que se suelte.
        ledgeBackwardBlocked = moveInput.y < -wallhugExitThreshold;
        velocity = Vector3.zero;
        SnapToHangPosition();
    }

    private bool TryDetectLedgeAtFeet(Vector3 wallDirection, out Vector3 ledgeTopOut, out Vector3 wallNormalOut)
    {
        ledgeTopOut = Vector3.zero;
        wallNormalOut = Vector3.zero;

        Vector3 feetPos = physics.GetFeetPosition();
        Vector3 castOrigin = feetPos + Vector3.up * 0.15f;

        if (!Physics.Raycast(castOrigin, wallDirection, out RaycastHit wallHit,
            ledgeDetectionDistance, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        float angle = Vector3.Angle(wallHit.normal, Vector3.up);
        if (angle < physics.maxGroundAngle || angle >= 135f) return false;

        // Misma verificación de "no es pared alta" que TryDetectLedge
        Vector3 tallCheckOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) + wallHit.normal * 0.05f;
        if (Physics.Raycast(tallCheckOrigin, -wallHit.normal, 0.2f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        Vector3 searchOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) - wallHit.normal * 0.05f;
        if (!Physics.Raycast(searchOrigin, Vector3.down, out RaycastHit ledgeHit,
            ledgeTopSearchHeight + 0.5f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        // El top debe estar cerca de la altura de los pies (rango up/down separado)
        float feetY = feetPos.y;
        if (ledgeHit.point.y < feetY - ledgeMaxReachDown || ledgeHit.point.y > feetY + ledgeMaxReachUp)
            return false;

        ledgeTopOut = ledgeHit.point;
        wallNormalOut = wallHit.normal;
        return true;
    }

    private bool TryDetectLedge(Vector3 wallDirection, out Vector3 ledgeTopOut, out Vector3 wallNormalOut)
    {
        ledgeTopOut = Vector3.zero;
        wallNormalOut = Vector3.zero;

        Vector3 headPos = physics.GetHeadPosition();

        // 1. Buscar pared a la altura de la cabeza
        if (!Physics.Raycast(headPos, wallDirection, out RaycastHit wallHit, ledgeDetectionDistance, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        float angle = Vector3.Angle(wallHit.normal, Vector3.up);
        if (angle < physics.maxGroundAngle || angle >= 135f) return false;

        // 2. Rechazar paredes demasiado altas: si la pared sigue existiendo por encima del rango
        // de cornisa esperado, NO es una cornisa agarrable.
        Vector3 tallCheckOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) + wallHit.normal * 0.05f;
        if (Physics.Raycast(tallCheckOrigin, -wallHit.normal, 0.2f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        // 3. Encontrar el top casteando hacia abajo desde sobre el final de la pared.
        // El origen está en aire (la pared ya terminó arriba), así no hay raycasts desde dentro
        // de un collider que devuelvan resultados inesperados.
        Vector3 searchOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) - wallHit.normal * 0.05f;
        if (!Physics.Raycast(searchOrigin, Vector3.down, out RaycastHit ledgeHit,
            ledgeTopSearchHeight + 0.5f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        // 4. La cornisa debe estar al alcance de las manos del jugador (rango up/down separado)
        float headY = headPos.y;
        if (ledgeHit.point.y < headY - ledgeMaxReachDown || ledgeHit.point.y > headY + ledgeMaxReachUp)
            return false;

        ledgeTopOut = ledgeHit.point;
        wallNormalOut = wallHit.normal;
        return true;
    }

    private void SnapToHangPosition()
    {
        CapsuleCollider col = physics.Collider;
        Vector3 pos;
        pos.y = ledgeTopPoint.y - col.center.y - col.height * 0.5f;
        pos.x = ledgeTopPoint.x + ledgeWallNormal.x * col.radius;
        pos.z = ledgeTopPoint.z + ledgeWallNormal.z * col.radius;
        physics.SetPosition(pos);
    }

    private void TickLedgeGrab(Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput)
    {
        CapsuleCollider col = physics.Collider;

        // Input una sola vez
        bool hasInput = cardinalInput.sqrMagnitude > 0.01f;
        Vector3 inputDir3D = hasInput
            ? (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized
            : Vector3.zero;

        // Soltar con S solo si es una pulsación nueva (no heredada del momento de entrar al grab).
        if (ledgeBackwardBlocked)
        {
            if (cardinalInput.y >= -wallhugExitThreshold)
                ledgeBackwardBlocked = false; // S soltada — próxima pulsación ya puede soltar
        }
        else if (cardinalInput.y < -wallhugExitThreshold)
        {
            isLedgeGrabbing = false;
            return;
        }

        // Verificar que el borde sigue ahí; si no, caer.
        // Raycast horizontal a nivel del ledge top — más fiable que el CapsuleCast completo
        // en plataformas delgadas donde el cast cuelga por debajo de la cara de la plataforma.
        if (!IsLedgeEdgePresent())
        {
            isLedgeGrabbing = false;
            return;
        }

        // Movimiento lateral a lo largo de la cornisa
        Vector3 ledgeRight = Vector3.Cross(Vector3.up, ledgeWallNormal).normalized;
        float lateralInput = hasInput ? Vector3.Dot(inputDir3D, ledgeRight) : 0f;

        velocity = ledgeRight * (lateralInput * moveSpeed);
        velocity.y = 0f;

        if (velocity.sqrMagnitude > 0.001f)
        {
            Vector3 prevPos = transform.position;
            MoveResult moveResult = physics.Move(velocity * Time.fixedDeltaTime);

            if (moveResult.collided)
            {
                CollisionInfo? cornerWall = moveResult.GetWallCollision(physics.maxGroundAngle);
                if (cornerWall.HasValue && Vector3.Dot(cornerWall.Value.normal, ledgeWallNormal) < 0.9f)
                {
                    Vector3 searchOrigin = new Vector3(
                        cornerWall.Value.point.x - cornerWall.Value.normal.x * 0.05f,
                        ledgeTopPoint.y + ledgeMaxReachUp + 0.2f,
                        cornerWall.Value.point.z - cornerWall.Value.normal.z * 0.05f
                    );
                    float searchDepth = ledgeMaxReachUp + ledgeMaxReachDown + 0.4f;

                    if (Physics.Raycast(searchOrigin, Vector3.down, out RaycastHit ledgeHit,
                        searchDepth, physics.collisionMask, QueryTriggerInteraction.Ignore))
                    {
                        // Diferencia signed: positivo = arriba, negativo = abajo
                        float heightDiff = ledgeHit.point.y - ledgeTopPoint.y;
                        bool compatible = heightDiff >= 0f
                            ? heightDiff <= ledgeMaxReachUp
                            : -heightDiff <= ledgeMaxReachDown;
                        if (compatible)
                        {
                            // Cornisa compatible → transición
                            ledgeTopPoint   = ledgeHit.point;
                            ledgeWallNormal = cornerWall.Value.normal;
                            SnapToHangPosition();
                            ApplyWallFacing(ledgeWallNormal);
                            return;
                        }
                    }
                    // Incompatible o sin cornisa → revertir movimiento y quedarse colgado
                    physics.SetPosition(prevPos);
                    velocity = Vector3.zero;
                }
            }
            else
            {
                // Sin colisión: si tras moverse el borde ya no está, revertir (fin del ledge).
                if (!IsLedgeEdgePresent())
                {
                    physics.SetPosition(prevPos);
                    velocity = Vector3.zero;
                }
            }
        }

        // Mantener altura de cuelgue tras movimiento lateral
        Vector3 pos = transform.position;
        pos.y = ledgeTopPoint.y - col.center.y - col.height * 0.5f;
        physics.SetPosition(pos);

        ApplyWallFacing(ledgeWallNormal);
    }

    private bool IsLedgeEdgePresent()
    {
        // Raycast horizontal justo bajo el top del ledge, desde la posición XZ actual del jugador.
        // Detecta el borde incluso en plataformas delgadas donde el CapsuleCast de altura completa
        // cuelga por debajo de la cara de la plataforma y no la encuentra.
        Vector3 origin = new Vector3(transform.position.x, ledgeTopPoint.y - 0.05f, transform.position.z);
        return Physics.Raycast(origin, -ledgeWallNormal, physics.Collider.radius + 0.2f,
            physics.collisionMask, QueryTriggerInteraction.Ignore);
    }

    private void ApplyWallFacing(Vector3 wallNormal)
    {
        Vector3 faceDir = new Vector3(-wallNormal.x, 0f, -wallNormal.z);
        if (faceDir.sqrMagnitude < 0.01f) return;
        Quaternion targetRot = Quaternion.LookRotation(faceDir.normalized, Vector3.up);
        modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
    }

    private void StartLedgeClimb()
    {
        CapsuleCollider col = physics.Collider;

        // Posición final: de pie encima de la cornisa desde donde está AHORA (no del punto original del grab)
        Vector3 endPos;
        endPos.y = ledgeTopPoint.y - col.center.y + col.height * 0.5f + 0.05f;
        Vector3 inward = -ledgeWallNormal * (col.radius * 2f + 0.2f);
        endPos.x = transform.position.x + inward.x;
        endPos.z = transform.position.z + inward.z;

        // Verificar que haya espacio para pararse encima. Si no, cancelar y seguir colgado.
        Vector3 checkCenter = endPos + col.center;
        Vector3 capsuleBottom = checkCenter + Vector3.down * (col.height * 0.5f - col.radius);
        Vector3 capsuleTop    = checkCenter + Vector3.up   * (col.height * 0.5f - col.radius);
        if (Physics.CheckCapsule(capsuleBottom, capsuleTop, col.radius - 0.02f,
            physics.collisionMask, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        climbStartPos = transform.position;
        climbEndPos = endPos;
        isClimbingLedge = true;
        isLedgeGrabbing = false;
        climbTimer = 0f;
        velocity = Vector3.zero;
        dashCooldownTimer = dashCooldown;
    }

    private void TickLedgeClimb()
    {
        climbTimer += Time.fixedDeltaTime;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(climbTimer / ledgeClimbDuration));
        physics.SetPosition(Vector3.Lerp(climbStartPos, climbEndPos, t));

        if (climbTimer >= ledgeClimbDuration)
        {
            isClimbingLedge = false;
            velocity = Vector3.zero;
            physics.ResolveOverlaps();
        }
    }

    private void TickStepUp()
    {
        stepUpTimer += Time.fixedDeltaTime;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stepUpTimer / autoStepUpDuration));
        physics.SetPosition(Vector3.Lerp(climbStartPos, climbEndPos, t));

        if (stepUpTimer >= autoStepUpDuration)
        {
            isSteppingUp = false;
            velocity.y = groundedGravity;
            physics.ResolveOverlaps();
        }
    }

    public bool IsDashing => isDashing;
    public bool IsWallhugging => isWallhugging;
    public Vector3 WallNormal => wallNormal;
    public bool IsWallJumping => isWallJumping;
    public Vector3 WallJumpNormal => wallJumpNormal;
    public bool IsLedgeGrabbing => isLedgeGrabbing;
    public bool IsClimbingLedge => isClimbingLedge;
    public bool IsSteppingUp => isSteppingUp;
    public float DashCooldownNormalized => Mathf.Clamp01(dashCooldownTimer / dashCooldown);
    public void onItemEquip(OnItemEquipEvent e) => ChangeMoveSpeed(e.item.handWeightMultiplier);
    public void OnItemUnequip(OnItemUnequipEvent e) => ResetMoveSpeed();
    public void ChangeMoveSpeed(float multiplier) { moveSpeed = baseMoveSpeed * multiplier; runSpeed = baseRunSpeed * multiplier; }
    public void ResetMoveSpeed() { moveSpeed = baseMoveSpeed; runSpeed = baseRunSpeed; }
    public Vector2 MoveInput => moveInput;
    public Transform CameraTarget => cameraTarget;
}