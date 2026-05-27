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
    [Range(0f, 1f)] public float wallhugSpeedMultiplier = 1f;
    [Tooltip("Minimum wall height required to enter wallhug. Lower steps are handled by auto step-up or normal movement.")]
    public float wallhugMinWallHeight = 1.0f;
    [Tooltip("Maximum reach above the head where a ledge is searched for when pressing cardinal-up during wallhug.")]
    public float wallhugLedgeJumpMaxReach = 2.0f;

    [Header("Auto Step Up")]
    [Tooltip("Maximum step height the player can climb automatically while walking. Higher steps require a jump / ledge grab.")]
    public float autoStepMaxHeight = 0.5f;
    [Tooltip("Duration of the step-up animation (seconds).")]
    public float autoStepUpDuration = 0.12f;

    [Header("Ledge Grab")]
    [Range(0f, 1f)] public float ledgeGrabSpeedMultiplier = 1f;
    public float ledgeDetectionDistance = 0.6f;
    public float ledgeTopSearchHeight = 0.5f;
    public float ledgeClimbDuration = 0.25f;
    [Tooltip("Maximum step UP the player can reach when ledge-grabbing.")]
    public float ledgeMaxReachUp = 0.3f;
    [Tooltip("Maximum step DOWN the player can reach when ledge-grabbing.")]
    public float ledgeMaxReachDown = 0.5f;

    [Header("Ledge Grab - Falloff (auto-grab when falling)")]
    [Tooltip("Minimum drop below the player to activate auto-grab. If there's floor nearby it's just a step and won't grab.")]
    public float falloffMinDropHeight = 1.5f;
    [Tooltip("Minimum horizontal speed required to activate auto-grab when falling off a ledge.")]
    public float falloffMinSpeed = 1.0f;

    private PhysicsController physics;
    private Vector3 velocity;
    private float currentSpeed;
    private float baseMoveSpeed;
    private float baseRunSpeed;

    private Vector2 moveInput;
    private Vector2 lastTargetingInput;     // For last-pressed-key detection
    private Vector2 lastTargetingMoveDir;   // Last cardinal direction selected
    private bool dashPressed;

    private bool isDashing;
    private float dashTimer;
    private float dashCooldownTimer;
    private Vector3 dashDirection;
    private bool isJumping;

    public bool isWallhugging;
    private Vector3 wallNormal;
    private bool isWallJumping;
    private Vector3 wallJumpNormal;

    public bool isLedgeGrabbing;
    private Vector3 ledgeTopPoint;
    private Vector3 ledgeWallNormal;
    private bool ledgeBackwardBlocked; // S was held on entry; must be released before it can drop again
    private bool isClimbingLedge;
    private float climbTimer;
    private Vector3 climbStartPos;
    private Vector3 climbEndPos;

    private bool isSteppingUp;
    private float stepUpTimer;

    private bool interactHeld;
    private float ledgeGrabReleaseCooldown;
    private bool wallhugUpBlocked; // W was held on entry; must be released before jump is allowed
    public bool nearWall;          // pared agarrable enfrente — bloquea el dash aunque no se active wallhug

    // Singleton accessor — lets other systems (HUD, AI, etc.) reach the player
    // without paying for FindObjectByType. Set in Awake, cleared in OnDestroy.
    public static PlayerMovement Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;

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

    // ─── Movement-enabled flag (toggled by OnSetMovementEnabledEvent) ──────
    private bool movementEnabled = true;

    // ─── Locomotion-state event (cached + last-emitted tracking) ───────────
    // Emits OnPlayerLocomotionStateEvent only when isWallhugging / isLedgeGrabbing
    // / nearWall actually transition. Lets UI react without per-frame polling.
    private readonly OnPlayerLocomotionStateEvent _locomotionEvt = new();
    private bool _lastEmittedWallhug;
    private bool _lastEmittedLedge;
    private bool _lastEmittedNearWall;
    private bool _hasEmittedLocomotion;

    private void OnEnable()
    {
        // All input wiring lives here — no inspector EventBusListener required.
        EventBus.Subscribe<OnMoveInputEvent>(OnMoveInput);
        EventBus.Subscribe<OnInteractDodgeInputEvent>(OnInteractDodgeInput);
        EventBus.Subscribe<OnItemEquipEvent>(OnItemEquipped);
        EventBus.Subscribe<OnItemUnequipEvent>(OnItemUnequipped);
        EventBus.Subscribe<OnSetMovementEnabledEvent>(OnSetMovementEnabled);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnMoveInputEvent>(OnMoveInput);
        EventBus.Unsubscribe<OnInteractDodgeInputEvent>(OnInteractDodgeInput);
        EventBus.Unsubscribe<OnItemEquipEvent>(OnItemEquipped);
        EventBus.Unsubscribe<OnItemUnequipEvent>(OnItemUnequipped);
        EventBus.Unsubscribe<OnSetMovementEnabledEvent>(OnSetMovementEnabled);
        if (isWallhugging) isWallhugging = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        // Push initial locomotion state so listeners (HUD, AI…) sync without polling.
        EmitLocomotionStateIfChanged(force: true);
    }

    private void LateUpdate()
    {
        // Cheap transition check — only raises when one of the tracked flags changed.
        EmitLocomotionStateIfChanged(force: false);
    }

    private void EmitLocomotionStateIfChanged(bool force)
    {
        if (!force && _hasEmittedLocomotion
            && _lastEmittedWallhug == isWallhugging
            && _lastEmittedLedge   == isLedgeGrabbing
            && _lastEmittedNearWall == nearWall) return;

        _lastEmittedWallhug   = isWallhugging;
        _lastEmittedLedge     = isLedgeGrabbing;
        _lastEmittedNearWall  = nearWall;
        _hasEmittedLocomotion = true;

        _locomotionEvt.isWallhugging   = isWallhugging;
        _locomotionEvt.isLedgeGrabbing = isLedgeGrabbing;
        _locomotionEvt.nearWall        = nearWall;
        EventBus.Raise(_locomotionEvt);
    }

    // ─── Input handlers (auto-subscribed) ──────────────────────────────────

    private void OnMoveInput(OnMoveInputEvent e)
    {
        // When movement is globally disabled (cutscene, dialog…), refuse new input.
        moveInput = movementEnabled ? e.Direction : Vector2.zero;
    }

    /// <summary>
    /// Interact/dodge is a shared button. The same press both toggles wallhug
    /// (interactHeld) and triggers a dash this frame (dashPressed).
    /// </summary>
    private void OnInteractDodgeInput(OnInteractDodgeInputEvent e)
    {
        if (!movementEnabled) { interactHeld = false; dashPressed = false; return; }
        interactHeld = e.pressed;
        dashPressed = e.pressed;
        if (!e.pressed && isWallhugging) isWallhugging = false;
    }

    private void OnSetMovementEnabled(OnSetMovementEnabledEvent e)
    {
        movementEnabled = e.enabled;
        if (!movementEnabled)
        {
            // Zero out pending inputs so the player stops next frame.
            moveInput = Vector2.zero;
            dashPressed = false;
            interactHeld = false;
        }
    }

    void FixedUpdate()
    {
        GroundInfo ground = physics.Ground;

        // Lock targeting during ledge grab / climb.
        if (targetingSystem != null)
            targetingSystem.Locked = isLedgeGrabbing || isClimbingLedge;

        // Disable slope handling while wallhugging so the curb isn't treated as a ramp.
        physics.autoSlopeHandling = !isWallhugging;

        if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.fixedDeltaTime;
        if (ledgeGrabReleaseCooldown > 0f) ledgeGrabReleaseCooldown -= Time.fixedDeltaTime;
        if (isJumping && ground.isGrounded && velocity.y <= 0f)
        {
            isJumping = false;
            if (isWallJumping)
            {
                isWallJumping = false;
                // If the wall is still there on landing, re-enter wallhug.
                CollisionInfo reentry = physics.CheckDirection(-wallJumpNormal, 0.15f);
                if (reentry.hit && reentry.IsWall(physics.maxGroundAngle))
                {
                    isWallhugging = true;
                    wallNormal = reentry.normal;
                }
            }
        }

        // ── Movement axes (required before the dash and wallhug checks) ──────────
        bool isTargeting = targetingSystem != null && targetingSystem.IsTargeting;

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

        Vector2 rawInput = moveInput;
        Vector2 cardinalInput = Vector2.zero;

        if (isTargeting)
        {
            bool xActive = Mathf.Abs(rawInput.x) > 0.01f;
            bool yActive = Mathf.Abs(rawInput.y) > 0.01f;

            if (!xActive && !yActive)
                cardinalInput = Vector2.zero;
            else if (xActive && !yActive)
                cardinalInput = new Vector2(Mathf.Sign(rawInput.x), 0f);
            else if (!xActive && yActive)
                cardinalInput = new Vector2(0f, Mathf.Sign(rawInput.y));
            else
            {
                float deltaX = Mathf.Abs(rawInput.x - lastTargetingInput.x);
                float deltaY = Mathf.Abs(rawInput.y - lastTargetingInput.y);
                if (deltaX > deltaY)
                    cardinalInput = new Vector2(Mathf.Sign(rawInput.x), 0f);
                else if (deltaY > deltaX)
                    cardinalInput = new Vector2(0f, Mathf.Sign(rawInput.y));
                else
                    cardinalInput = lastTargetingMoveDir;
            }

            lastTargetingInput = rawInput;
            lastTargetingMoveDir = cardinalInput;
        }
        else
        {
            cardinalInput = rawInput;
        }

        // nearWall: same detection as TryEnterWallhug but without activating the state.
        // Blocks the dash when the player is pressed against a wallhuggable wall.
        nearWall = !isWallhugging && !isJumping && !isDashing
                   && CheckNearWall(ground, forwardAxis, rightAxis, cardinalInput, out _);

        // Climb-up animation — blocks all input.
        if (isClimbingLedge)
        {
            dashPressed = false;
            TickLedgeClimb();
            return;
        }

        // Step-up animation — blocks all input.
        if (isSteppingUp)
        {
            TickStepUp();
            return;
        }

        // Climb up a ledge (ledge grab → climb).
        if (dashPressed && isLedgeGrabbing)
        {
            dashPressed = false;
            StartLedgeClimb();
            return;
        }
        // Wallhug jump takes priority (pure upward push, zero horizontal velocity).
        else if (dashPressed && isWallhugging)
        {
            dashPressed = false;
            StartWallhugJump();
            physics.Move(velocity * Time.fixedDeltaTime);
            return;
        }
        // Start dash / targeting jump — blocked if a wallhuggable wall is in front.
        else if (dashPressed && !isDashing && dashCooldownTimer <= 0f && ground.isGrounded && !isJumping && !isWallhugging && !nearWall)
        {
            dashPressed = false;
            if (targetingSystem != null && targetingSystem.IsTargeting)
                StartTargetingJump();
            else
                StartDash();
        }
        else if (dashPressed) dashPressed = false;

        // While dashing.
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

        // Wallhug tick (returns early, manages its own Move call).
        if (isWallhugging)
        {
            TickWallhug(ground, forwardAxis, rightAxis, cardinalInput);
            return;
        }
        // Auto step-up: small steps are climbed automatically before any wallhug attempt.
        if (!isJumping) TryAutoStepUp(ground, forwardAxis, rightAxis, cardinalInput);
        // Wallhug: only enters when the player holds the interact key against a wall.
        if (!isJumping && interactHeld && !isWallhugging)
            TryEnterWallhug(ground, forwardAxis, rightAxis, cardinalInput);

        // Ledge grab tick
        if (isLedgeGrabbing)
        {
            TickLedgeGrab(forwardAxis, rightAxis, cardinalInput);
            return;
        }
        // Detect ledge while airborne (briefly suppressed after a deliberate release).
        if (!ground.isGrounded && !isDashing && velocity.y > -8f && ledgeGrabReleaseCooldown <= 0f)
            TryGrabLedge();

        // If TryGrabLedge latched this frame, skip normal movement.
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
        // Verify the wall is still there.
        CollisionInfo wallCheck = physics.CheckDirection(-wallNormal, 0.15f);
        if (!wallCheck.hit || !wallCheck.IsWall(physics.maxGroundAngle))
        {
            isWallhugging = false;
            ApplyGravityAndMove(ground);
            return;
        }
        wallNormal = wallCheck.normal;

        // Curb / narrow-surface detection next to the wall.
        bool isGrounded = ground.isGrounded || HasWallSideGround(wallCheck.point, wallNormal);

        // Exit if the player slipped off the edge (neither jumping nor grounded).
        if (!isGrounded && !isJumping)
        {
            isWallhugging = false;
            ApplyGravityAndMove(ground);
            return;
        }

        // Exit on release of the interact key.
        if (!interactHeld)
        {
            isWallhugging = false;
            ApplyGravityAndMove(ground);
            return;
        }

        // Cardinal up: try to jump and grab a ledge above the wall.
        // Requires releasing W and re-pressing if wallhug was entered with W already held.
        if (wallhugUpBlocked)
        {
            if (cardinalInput.y <= wallhugExitThreshold) wallhugUpBlocked = false;
        }
        else if (cardinalInput.y > wallhugExitThreshold)
        {
            TryWallhugJumpToLedge();
            return;
        }

        // Lateral movement along the wall (same system as ledge grab).
        bool hasInput = cardinalInput.sqrMagnitude > 0.01f;
        Vector3 inputDir3D = hasInput
            ? (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized
            : Vector3.zero;

        Vector3 wallRight = Vector3.Cross(Vector3.up, wallNormal).normalized;
        float lateralInput = hasInput ? Vector3.Dot(inputDir3D, wallRight) : 0f;

        float inputMagnitude = moveInput.magnitude;
        float targetSpeed = (inputMagnitude >= runThreshold) ? runSpeed : moveSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, runAcceleration * Time.fixedDeltaTime);

        Vector3 moveVel = wallRight * (lateralInput * currentSpeed * wallhugSpeedMultiplier);
        velocity.x = moveVel.x;
        velocity.z = moveVel.z;

        // Face the wall.
        Vector3 faceWall = new Vector3(-wallNormal.x, 0f, -wallNormal.z);
        if (faceWall.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(faceWall.normalized, Vector3.up);
            modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
        }

        // Apply gravity using extended ground detection (not ApplyGravityAndMove to keep isGrounded).
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

        // 1. Look for a wall in front at foot level.
        Vector3 lowProbe = feetPos + Vector3.up * 0.1f;
        float probeDistance = col.radius + 0.15f;
        if (!Physics.Raycast(lowProbe, inputDir, out RaycastHit wallHit,
            probeDistance, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        float wallAngle = Vector3.Angle(wallHit.normal, Vector3.up);
        if (wallAngle < physics.maxGroundAngle || wallAngle >= 135f) return;

        // 2. Find the top surface of the step (cast downward from above).
        Vector3 highProbe = wallHit.point + Vector3.up * (autoStepMaxHeight + 0.1f) - wallHit.normal * 0.05f;
        if (!Physics.Raycast(highProbe, Vector3.down, out RaycastHit stepHit,
            autoStepMaxHeight + 0.2f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        // 3. Step height must be in range (positive and ≤ autoStepMaxHeight).
        float stepHeight = stepHit.point.y - feetPos.y;
        if (stepHeight <= 0.001f || stepHeight > autoStepMaxHeight) return;

        // 4. The top surface must be walkable.
        float stepTopAngle = Vector3.Angle(stepHit.normal, Vector3.up);
        if (stepTopAngle > physics.maxGroundAngle) return;

        // 5. Verify the player capsule fits standing on top of the step.
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

        // 6. Begin the climb-up animation.
        climbStartPos = transform.position;
        climbEndPos = standingPos;
        stepUpTimer = 0f;
        isSteppingUp = true;
    }

    private bool CheckNearWall(GroundInfo ground, Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput, out Vector3 wallDir)
    {
        wallDir = Vector3.zero;

        if (cardinalInput.sqrMagnitude > 0.01f)
        {
            Vector3 diagDir = (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized;
            if (CheckNearWallInDirection(ground, diagDir)) { wallDir = diagDir; return true; }

            if (Mathf.Abs(cardinalInput.y) > 0.01f)
            {
                Vector3 fwdDir = forwardAxis * Mathf.Sign(cardinalInput.y);
                if (CheckNearWallInDirection(ground, fwdDir)) { wallDir = fwdDir; return true; }
            }

            if (Mathf.Abs(cardinalInput.x) > 0.01f)
            {
                Vector3 rightDir = rightAxis * Mathf.Sign(cardinalInput.x);
                if (CheckNearWallInDirection(ground, rightDir)) { wallDir = rightDir; return true; }
            }
        }
        else
        {
            // No directional input — scan the 4 camera-relative cardinals.
            Vector3[] dirs = { forwardAxis, -forwardAxis, rightAxis, -rightAxis };
            foreach (Vector3 dir in dirs)
            {
                if (CheckNearWallInDirection(ground, dir)) { wallDir = dir; return true; }
            }
        }

        return false;
    }

    private bool CheckNearWallInDirection(GroundInfo ground, Vector3 dir)
    {
        CollisionInfo wallCheck = physics.CheckDirection(dir, 0.1f);
        if (!wallCheck.hit || !wallCheck.IsWall(physics.maxGroundAngle)) return false;

        if (!ground.isGrounded && !HasWallSideGround(wallCheck.point, wallCheck.normal)) return false;

        CapsuleCollider col = physics.Collider;
        Vector3 feetPos = physics.GetFeetPosition();
        Vector3 heightCheckOrigin = new Vector3(transform.position.x,
                                                feetPos.y + wallhugMinWallHeight,
                                                transform.position.z);
        return Physics.Raycast(heightCheckOrigin, dir, col.radius + 0.25f,
            physics.collisionMask, QueryTriggerInteraction.Ignore);
    }

    private void TryEnterWallhug(GroundInfo ground, Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput)
    {
        if (isDashing) return;
        if (!CheckNearWall(ground, forwardAxis, rightAxis, cardinalInput, out Vector3 wallDir)) return;

        CollisionInfo wallCheck = physics.CheckDirection(wallDir, 0.1f);

        isWallhugging = true;
        wallNormal = wallCheck.normal;
        wallhugUpBlocked = cardinalInput.y > wallhugExitThreshold;
    }

    /// <summary>
    /// Ground detection alongside the wall (for curbs / narrow surfaces).
    /// Uses the actual CapsuleCast contact point to position the probes right in front
    /// of the wall surface — guaranteeing the origin never falls inside the wall
    /// collider (which would make raycasts return nothing).
    /// </summary>
    private bool HasWallSideGround(Vector3 wallContactPoint, Vector3 wallNorm)
    {
        Vector3 feetPos = physics.GetFeetPosition();
        const float originHeight = 0.25f;
        float castDist = originHeight + physics.groundCheckDistance + 0.25f;

        // Primary probe: 3 cm in front of the wall surface.
        // wallContactPoint is on the surface; +wallNorm * 0.03 nudges it into open air.
        Vector3 nearWallXZ = wallContactPoint + wallNorm * 0.03f;
        Vector3 nearWallOrigin = new Vector3(nearWallXZ.x, feetPos.y + originHeight, nearWallXZ.z);
        if (Physics.Raycast(nearWallOrigin, Vector3.down, castDist,
            physics.collisionMask, QueryTriggerInteraction.Ignore))
            return true;

        // Backup probe: from the player XZ center, shifted toward the wall.
        // Covers wider curbs where the primary probe lands over empty space.
        Vector3 midXZ = new Vector3(transform.position.x, 0f, transform.position.z)
                      - new Vector3(wallNorm.x, 0f, wallNorm.z).normalized * (physics.Collider.radius * 0.4f);
        Vector3 midOrigin = new Vector3(midXZ.x, feetPos.y + originHeight, midXZ.z);
        if (Physics.Raycast(midOrigin, Vector3.down, castDist,
            physics.collisionMask, QueryTriggerInteraction.Ignore))
            return true;

        return false;
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

    private void TryWallhugJumpToLedge()
    {
        // TryDetectLedge fails from a standing position: the wall continues above
        // the head and ledgeMaxReachUp is too small. Use a custom scan instead:
        // raycast downward from (head + maxReach) along the wall face until we hit
        // a flat surface — if any exists, that's a grabbable ledge.
        CapsuleCollider col = physics.Collider;
        Vector3 toWall = -wallNormal;
        Vector3 headPos = physics.GetHeadPosition();

        // XZ just in front of the wall face (radius + a small margin).
        Vector3 scanXZ = new Vector3(transform.position.x, 0f, transform.position.z)
                       + new Vector3(toWall.x, 0f, toWall.z).normalized * (col.radius + 0.05f);
        Vector3 scanOrigin = new Vector3(scanXZ.x, headPos.y + wallhugLedgeJumpMaxReach, scanXZ.z);

        if (!Physics.Raycast(scanOrigin, Vector3.down, out RaycastHit hit,
            wallhugLedgeJumpMaxReach, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        // Must be a walkable surface, not a sloped wall.
        if (Vector3.Angle(hit.normal, Vector3.up) > physics.maxGroundAngle) return;

        // Must be above the current head height (if it's at floor level it's just ground).
        if (hit.point.y <= headPos.y - 0.1f) return;

        isWallhugging = false;
        interactHeld = false;
        velocity.x = 0f;
        velocity.z = 0f;
        velocity.y = wallhugJumpForce;
        isJumping = true;
        isWallJumping = true;   // zeroes horizontal velocity every frame until AttachToLedge clears it
        wallJumpNormal = wallNormal;
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

        // Normal case: ledge at head height in the movement direction.
        if (TryDetectLedge(checkDir, out Vector3 ledgeTop, out Vector3 wallNorm))
        {
            AttachToLedge(ledgeTop, wallNorm);
            return;
        }

        // Auto-grab when falling off a curb: player not jumping, with enough horizontal
        // speed and falling slowly (small window after leaving ground). Ledge behind, at feet.
        if (!isJumping
            && velocity.y < 0.5f && velocity.y > -4f
            && horizontalVel.magnitude >= falloffMinSpeed)
        {
            Vector3 backDir = -horizontalVel.normalized;
            if (TryDetectLedgeAtFeet(backDir, out ledgeTop, out wallNorm))
            {
                // Verify there's an actual drop below. If there's floor nearby,
                // it's just a small step — not worth grabbing.
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
        // If S was already pressed on entry, block the S-release until the player lets go.
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

        // Same "not a tall wall" check as TryDetectLedge.
        Vector3 tallCheckOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) + wallHit.normal * 0.05f;
        if (Physics.Raycast(tallCheckOrigin, -wallHit.normal, 0.2f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        Vector3 searchOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) - wallHit.normal * 0.05f;
        if (!Physics.Raycast(searchOrigin, Vector3.down, out RaycastHit ledgeHit,
            ledgeTopSearchHeight + 0.5f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        // The top must be near foot height (separate up/down ranges).
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

        // 1. Look for a wall at head height.
        if (!Physics.Raycast(headPos, wallDirection, out RaycastHit wallHit, ledgeDetectionDistance, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        float angle = Vector3.Angle(wallHit.normal, Vector3.up);
        if (angle < physics.maxGroundAngle || angle >= 135f) return false;

        // 2. Reject walls that are too tall: if the wall continues above the expected
        // ledge range, it is NOT a grabbable ledge.
        Vector3 tallCheckOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) + wallHit.normal * 0.05f;
        if (Physics.Raycast(tallCheckOrigin, -wallHit.normal, 0.2f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        // 3. Find the top by casting downward from above the wall's end.
        // The origin is in mid-air (the wall has ended above), so we never raycast
        // from inside a collider and get unexpected results.
        Vector3 searchOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) - wallHit.normal * 0.05f;
        if (!Physics.Raycast(searchOrigin, Vector3.down, out RaycastHit ledgeHit,
            ledgeTopSearchHeight + 0.5f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        // 4. The ledge must be within hand reach of the player (separate up/down range).
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

        // Only release via S if it's a fresh press (not the one held when entering the grab).
        if (ledgeBackwardBlocked)
        {
            if (cardinalInput.y >= -wallhugExitThreshold)
                ledgeBackwardBlocked = false; // S released — next press is allowed to drop.
        }
        else if (cardinalInput.y < -wallhugExitThreshold)
        {
            isLedgeGrabbing = false;
            ledgeGrabReleaseCooldown = 0.5f;
            return;
        }

        // Verify the edge is still there; if not, fall.
        // Horizontal raycast at the ledge top — more reliable than a full CapsuleCast
        // on thin platforms where the cast hangs below the platform face.
        if (!IsLedgeEdgePresent())
        {
            isLedgeGrabbing = false;
            return;
        }

        // Lateral movement along the ledge.
        Vector3 ledgeRight = Vector3.Cross(Vector3.up, ledgeWallNormal).normalized;
        float lateralInput = hasInput ? Vector3.Dot(inputDir3D, ledgeRight) : 0f;

        velocity = ledgeRight * (lateralInput * moveSpeed * ledgeGrabSpeedMultiplier);
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
                        // Signed height difference: positive = up, negative = down.
                        float heightDiff = ledgeHit.point.y - ledgeTopPoint.y;
                        bool compatible = heightDiff >= 0f
                            ? heightDiff <= ledgeMaxReachUp
                            : -heightDiff <= ledgeMaxReachDown;
                        if (compatible)
                        {
                            // Compatible ledge → transition.
                            ledgeTopPoint   = ledgeHit.point;
                            ledgeWallNormal = cornerWall.Value.normal;
                            SnapToHangPosition();
                            ApplyWallFacing(ledgeWallNormal);
                            return;
                        }
                    }
                    // Incompatible or no ledge → revert movement and stay hanging.
                    physics.SetPosition(prevPos);
                    velocity = Vector3.zero;
                }
            }
            else
            {
                // No collision: if after moving the edge is gone, revert (end of ledge).
                if (!IsLedgeEdgePresent())
                {
                    physics.SetPosition(prevPos);
                    velocity = Vector3.zero;
                }
            }
        }

        // Maintain hang height after lateral movement.
        Vector3 pos = transform.position;
        pos.y = ledgeTopPoint.y - col.center.y - col.height * 0.5f;
        physics.SetPosition(pos);

        ApplyWallFacing(ledgeWallNormal);
    }

    private bool IsLedgeEdgePresent()
    {
        float dist = physics.Collider.radius + 0.2f;
        Vector3 toWall = -ledgeWallNormal;

        // There must be a wall just below the ledge top (real edge).
        Vector3 below = new Vector3(transform.position.x, ledgeTopPoint.y - 0.05f, transform.position.z);
        if (!Physics.Raycast(below, toWall, dist, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        // There must NOT be a wall just above the ledge: if there is, it's a solid block
        // continuation, not a hangable edge. The player shouldn't be able to slide there.
        Vector3 above = new Vector3(transform.position.x, ledgeTopPoint.y + 0.05f, transform.position.z);
        if (Physics.Raycast(above, toWall, dist, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        return true;
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

        // Final position: standing on top of the ledge from the CURRENT location (not the grab origin).
        Vector3 endPos;
        endPos.y = ledgeTopPoint.y - col.center.y + col.height * 0.5f + 0.05f;
        Vector3 inward = -ledgeWallNormal * (col.radius * 2f + 0.2f);
        endPos.x = transform.position.x + inward.x;
        endPos.z = transform.position.z + inward.z;

        // Verify there's room to stand on top. If not, cancel and stay hanging.
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
    // Equip/unequip handlers (auto-subscribed in OnEnable above).
    private void OnItemEquipped(OnItemEquipEvent e)
    {
        if (e.item != null) ChangeMoveSpeed(e.item.handWeightMultiplier);
    }
    private void OnItemUnequipped(OnItemUnequipEvent e) => ResetMoveSpeed();

    public void ChangeMoveSpeed(float multiplier) { moveSpeed = baseMoveSpeed * multiplier; runSpeed = baseRunSpeed * multiplier; }
    public void ResetMoveSpeed() { moveSpeed = baseMoveSpeed; runSpeed = baseRunSpeed; }
    public Vector2 MoveInput => moveInput;
    public Transform CameraTarget => cameraTarget;
}