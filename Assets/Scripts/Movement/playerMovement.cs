using UnityEngine;

[RequireComponent(typeof(PhysicsController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    public Transform modelTransform;
    public Transform cameraTarget;
    public TargetingSystem targetingSystem;

    [Header("Movement")]
    public float moveSpeed = 4f;
    public float runSpeed = 8f;
    public float rotationSpeed = 15f;

    [Header("Run Acceleration")]
    public float runAcceleration = 5f;
    [Range(0.85f, 1f)] public float runThreshold = 0.9f;

    [Header("Gravity")]
    public float gravity = -20f;
    public float groundedGravity = -2f;

    [Header("Air Control")]
    [Range(0f, 1f)] public float airControlFactor = 0.1f;
    public float airDrag = 2f;

    [Header("Knockback")]
    [Tooltip("How quickly the knockback velocity decays while the player is hit-stunned.")]
    public float knockbackDrag = 5f;

    [Header("Auto Step Up")]
    [Tooltip("Maximum step height the player can climb automatically while walking. Higher steps require a jump / ledge grab.")]
    public float autoStepMaxHeight = 0.5f;
    [Tooltip("Duration of the step-up animation (seconds).")]
    public float autoStepUpDuration = 0.12f;

    // ─── Shared mutable state (written by sub-components) ─────────────────────
    [HideInInspector] public Vector3 velocity;
    [HideInInspector] public bool isJumping;
    [HideInInspector] public float currentSpeed;
    [HideInInspector] public bool interactHeld;

    // ─── Public locomotion flags (consumed by HUD, AI, TargetingSystem) ───────
    public bool isWallhugging;
    public bool isLedgeGrabbing;
    public bool nearWall;
    public bool isClimbingLedge;
    public bool isSteppingUp;

    // ─── Optional add-on components (null when not present) ──────────────────
    public PlayerDash Dash { get; private set; }
    public PlayerWallhug Wallhug { get; private set; }
    public PlayerLedgeGrab LedgeGrab { get; private set; }

    // ─── Auto step-up state ───────────────────────────────────────────────────
    private float stepUpTimer;
    private Vector3 stepStartPos;
    private Vector3 stepEndPos;

    // ─── Knockback / hit-stun state ─────────────────────────────────────────────
    private float knockbackTimer;
    public bool IsKnockedBack => knockbackTimer > 0f;

    // Authoritative facing held while targeting without an enemy.
    private Vector3 _targetFacing = Vector3.forward;
    private bool    _targetFacingSet;

    // Wallhug exit: face away from the wall when no movement input is given.
    private Vector3 _wallhugExitFacing;
    private bool    _hasWallhugExitFacing;
    private bool    _prevWallhugging;


    // ─── Singleton ─────────────────────────────────────────────────────────────
    public static PlayerMovement Instance { get; private set; }

    private PhysicsController physics;
    private Vector2 moveInput;
    private Vector2 lastTargetingInput;
    private Vector2 lastTargetingMoveDir;
    private bool dashPressed;
    private bool movementEnabled = true;
    private float baseMoveSpeed;
    private float baseRunSpeed;

    // ─── Locomotion event (cached, emitted only on transitions) ───────────────
    private readonly OnPlayerLocomotionStateEvent _locomotionEvt = new();
    private bool _lastEmittedWallhug;
    private bool _lastEmittedLedge;
    private bool _lastEmittedNearWall;
    private bool _hasEmittedLocomotion;

    void Awake()
    {
        if (Instance == null) Instance = this;

        physics   = GetComponent<PhysicsController>();
        Dash      = GetComponent<PlayerDash>();
        Wallhug   = GetComponent<PlayerWallhug>();
        LedgeGrab = GetComponent<PlayerLedgeGrab>();

        currentSpeed  = moveSpeed;
        baseMoveSpeed = moveSpeed;
        baseRunSpeed  = runSpeed;

        if (targetingSystem == null) TryGetComponent(out targetingSystem);
        if (modelTransform == null)
        {
            modelTransform = transform;
            Debug.LogWarning("[PlayerMovement] modelTransform not assigned.");
        }
        if (cameraTarget == null) Debug.LogError("[PlayerMovement] Assign CameraTarget.");

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnEnable()
    {
        EventBus.Subscribe<OnMoveInputEvent>(OnMoveInput);
        EventBus.Subscribe<OnInteractDodgeInputEvent>(OnInteractDodgeInput);
        EventBus.Subscribe<OnItemEquipEvent>(OnItemEquipped);
        EventBus.Subscribe<OnItemUnequipEvent>(OnItemUnequipped);
        EventBus.Subscribe<OnSetMovementEnabledEvent>(OnSetMovementEnabled);
    }

    void OnDisable()
    {
        EventBus.Unsubscribe<OnMoveInputEvent>(OnMoveInput);
        EventBus.Unsubscribe<OnInteractDodgeInputEvent>(OnInteractDodgeInput);
        EventBus.Unsubscribe<OnItemEquipEvent>(OnItemEquipped);
        EventBus.Unsubscribe<OnItemUnequipEvent>(OnItemUnequipped);
        EventBus.Unsubscribe<OnSetMovementEnabledEvent>(OnSetMovementEnabled);
        if (targetingSystem != null)
            targetingSystem.OnTargetingChanged -= OnTargetingChanged;
        if (isWallhugging) isWallhugging = false;
    }

    // True from the moment targeting turns on until it turns off (event-driven, so
    // it never flickers like the polled IsTargeting can during hold-to-target).
    private bool _targetingActive;
    public bool IsTargeting => _targetingActive;
    private void OnTargetingChanged(bool active)
    {
        _targetingActive = active;
        if (!active) _targetFacingSet = false;   // re-capture facing next time
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        EmitLocomotionStateIfChanged(force: true);

        // Subscribe here (not OnEnable) so targetingSystem - assigned in Awake via
        // TryGetComponent - is guaranteed to exist.
        if (targetingSystem != null)
        {
            targetingSystem.OnTargetingChanged += OnTargetingChanged;
            _targetingActive = targetingSystem.IsTargeting;
        }
    }

    void LateUpdate()
    {
        EmitLocomotionStateIfChanged(force: false);
    }

    // ─── Input handlers (auto-subscribed, no inspector EventBusListener needed) ─

    private void OnMoveInput(OnMoveInputEvent e)
    {
        moveInput = movementEnabled ? e.Direction : Vector2.zero;
    }

    // Same button drives both wallhug (interactHeld) and dash (dashPressed).
    // The Interactor cancels this event at priority 10 when near an interactable.
    private void OnInteractDodgeInput(OnInteractDodgeInputEvent e)
    {
        if (!movementEnabled) { interactHeld = false; dashPressed = false; return; }
        interactHeld = e.pressed;
        dashPressed  = e.pressed;
        if (!e.pressed && isWallhugging) isWallhugging = false;
    }

    private void OnSetMovementEnabled(OnSetMovementEnabledEvent e)
    {
        movementEnabled = e.enabled;
        if (!movementEnabled)
        {
            moveInput    = Vector2.zero;
            dashPressed  = false;
            interactHeld = false;
        }
    }

    // ─── FixedUpdate ───────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        GroundInfo ground = physics.Ground;

        // Capture wall-away facing on the first frame after exiting wallhug.
        if (_prevWallhugging && !isWallhugging && Wallhug != null)
        {
            Vector3 wn = Wallhug.WallNormal; wn.y = 0f;
            if (wn.sqrMagnitude > 0.001f) { _wallhugExitFacing = wn.normalized; _hasWallhugExitFacing = true; }
        }
        _prevWallhugging = isWallhugging;

        if (targetingSystem != null)
            targetingSystem.Locked = isLedgeGrabbing || isClimbingLedge;

        physics.autoSlopeHandling = !isWallhugging;

        Dash?.TickCooldown();
        LedgeGrab?.TickReleaseCooldown();

        // Hit-stun knockback overrides all other movement until it expires.
        if (knockbackTimer > 0f)
        {
            TickKnockback(ground);
            return;
        }

        if (isJumping && ground.isGrounded && velocity.y <= 0f)
        {
            isJumping = false;
            Wallhug?.HandleJumpLanding();
        }

        // Event-driven targeting flag (stable; never flickers during hold input).
        bool isTargeting = _targetingActive;

        ComputeMovementAxes(isTargeting, out Vector3 forwardAxis, out Vector3 rightAxis, out Vector2 cardinalInput);

        nearWall = !isWallhugging && !isJumping
                   && (Dash == null || !Dash.IsDashing)
                   && Wallhug != null
                   && Wallhug.CheckNear(ground, forwardAxis, rightAxis, cardinalInput, out _);

        // Climb animation blocks all input.
        if (isClimbingLedge)
        {
            dashPressed = false;
            LedgeGrab?.TickClimb();
            return;
        }

        // Step-up animation blocks all input.
        if (isSteppingUp)
        {
            TickStepUp();
            return;
        }

        // Dodge button priority: climb ledge > wallhug jump > dash / targeting jump.
        if (dashPressed && isLedgeGrabbing)
        {
            dashPressed = false;
            LedgeGrab?.StartClimb();
            return;
        }
        else if (dashPressed && isWallhugging)
        {
            dashPressed = false;
            Wallhug?.StartJump();
            physics.Move(velocity * Time.fixedDeltaTime);
            return;
        }
        else if (dashPressed && Dash != null && Dash.CanDash(ground))
        {
            dashPressed = false;
            if (isTargeting) Dash.StartTargetingJump();
            else             Dash.StartDash();
        }
        else if (dashPressed) dashPressed = false;

        if (Dash != null && Dash.TickDash(ground)) return;

        if (isWallhugging && Wallhug != null)
        {
            Wallhug.TickWallhug(ground, forwardAxis, rightAxis, cardinalInput);
            return;
        }

        if (!isJumping) TryAutoStep(ground, forwardAxis, rightAxis, cardinalInput);
        if (!isJumping && interactHeld && !isWallhugging)
            Wallhug?.TryEnter(ground, forwardAxis, rightAxis, cardinalInput);

        if (isLedgeGrabbing && LedgeGrab != null)
        {
            LedgeGrab.TickLedgeGrab(forwardAxis, rightAxis, cardinalInput);
            return;
        }

        if (!ground.isGrounded && (Dash == null || !Dash.IsDashing)
            && velocity.y > -8f && (LedgeGrab == null || LedgeGrab.ReleaseCooldown <= 0f))
            LedgeGrab?.TryGrabLedge();

        if (isLedgeGrabbing) return;

        // ── Normal locomotion ─────────────────────────────────────────────────

        Vector3 moveDir = forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x;

        float inputMag = moveInput.magnitude;
        float targetSpd = (inputMag >= runThreshold) ? runSpeed : moveSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpd, runAcceleration * Time.fixedDeltaTime);
        bool hasInput = moveInput.sqrMagnitude > 0.01f;

        bool wallJumping = Wallhug != null && Wallhug.IsWallJumping;
        if (wallJumping)
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

        if (wallJumping)
        {
            Vector3 wallJumpNorm = Wallhug.WallJumpNormal;
            Vector3 faceWall = new Vector3(-wallJumpNorm.x, 0f, -wallJumpNorm.z);
            if (faceWall.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(faceWall.normalized, Vector3.up);
                modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
            }
        }
        else if (!isTargeting)
        {
            HandleRotation(moveDir);
            // After wallhug exit with no movement: smoothly rotate to face away from the wall.
            if (_hasWallhugExitFacing && moveDir.sqrMagnitude <= 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(_wallhugExitFacing, Vector3.up);
                modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
                if (Quaternion.Angle(modelTransform.rotation, targetRot) < 2f) _hasWallhugExitFacing = false;
            }
            else if (moveDir.sqrMagnitude > 0.01f)
            {
                _hasWallhugExitFacing = false;
            }
        }

        if (!ground.isGrounded)
            velocity.y += gravity * Time.fixedDeltaTime;
        else if (velocity.y < 0f)
            velocity.y = groundedGravity;

        MoveResult result = physics.Move(velocity * Time.fixedDeltaTime);
        if (result.HitCeiling() && velocity.y > 0f) velocity.y = 0f;

        // ── Authoritative targeting facing ──────────────────────────────────
        // Applied LAST so nothing earlier (or any stray rotation) can leave the
        // model turned toward its movement. With an enemy: face it. Without one:
        // hold the facing captured when targeting began. Outside targeting: clear
        // the captured facing so it re-captures next time.
        if (isTargeting)
        {
            Vector3 face;
            if (targetingSystem != null && targetingSystem.CurrentTarget != null)
            {
                face = targetingSystem.CurrentTarget.position - transform.position;
                face.y = 0f;
                if (face.sqrMagnitude < 0.0001f) face = _targetFacing;
            }
            else
            {
                if (!_targetFacingSet)
                {
                    Vector3 f = modelTransform.forward; f.y = 0f;
                    _targetFacing = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
                    _targetFacingSet = true;
                }
                face = _targetFacing;
            }
            if (face.sqrMagnitude > 0.0001f)
                modelTransform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
        }
        else
        {
            _targetFacingSet = false;
        }
    }

    // ─── Knockback ───────────────────────────────────────────────────────────

    /// <summary>
    /// Shoves the player away from <paramref name="sourcePosition"/> and hit-stuns
    /// them for <paramref name="duration"/> seconds (movement input is ignored while
    /// the knockback decays). Cancels conflicting movement states.
    /// </summary>
    public void ApplyKnockback(Vector3 sourcePosition, float force, float up, float duration)
    {
        Vector3 dir = transform.position - sourcePosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            dir = -(modelTransform != null ? modelTransform.forward : transform.forward);
        dir.Normalize();

        velocity = dir * force + Vector3.up * up;
        knockbackTimer = Mathf.Max(0.01f, duration);

        // Drop any state that assumes normal control.
        isWallhugging = false;
        isLedgeGrabbing = false;
        isClimbingLedge = false;
        isSteppingUp = false;
        dashPressed = false;
        interactHeld = false;
    }

    private void TickKnockback(GroundInfo ground)
    {
        knockbackTimer -= Time.fixedDeltaTime;

        if (!ground.isGrounded)
            velocity.y += gravity * Time.fixedDeltaTime;
        else if (velocity.y < 0f)
            velocity.y = groundedGravity;

        float drag = knockbackDrag * Time.fixedDeltaTime;
        velocity.x = Mathf.Lerp(velocity.x, 0f, drag);
        velocity.z = Mathf.Lerp(velocity.z, 0f, drag);

        MoveResult result = physics.Move(velocity * Time.fixedDeltaTime);
        if (result.HitCeiling() && velocity.y > 0f) velocity.y = 0f;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private void ComputeMovementAxes(bool isTargeting, out Vector3 forwardAxis, out Vector3 rightAxis, out Vector2 cardinalInput)
    {
        if (isTargeting)
        {
            // Movement axes are relative to the model's facing. The model's rotation
            // itself is set authoritatively at the END of FixedUpdate (face target,
            // or hold the captured facing) - NOT here, so movement can never turn it.
            Vector3 facing;
            if (targetingSystem != null && targetingSystem.CurrentTarget != null)
            {
                facing = targetingSystem.CurrentTarget.position - transform.position;
                facing.y = 0f;
                if (facing.sqrMagnitude < 0.0001f) facing = modelTransform.forward;
            }
            else
            {
                facing = modelTransform.forward;
                facing.y = 0f;
            }
            forwardAxis = facing.sqrMagnitude > 0.0001f ? facing.normalized : modelTransform.forward;
            rightAxis   = Vector3.Cross(Vector3.up, forwardAxis);

            Vector2 rawInput = moveInput;
            bool xActive = Mathf.Abs(rawInput.x) > 0.01f;
            bool yActive = Mathf.Abs(rawInput.y) > 0.01f;

            if (!xActive && !yActive)     cardinalInput = Vector2.zero;
            else if (xActive && !yActive) cardinalInput = new Vector2(Mathf.Sign(rawInput.x), 0f);
            else if (!xActive && yActive) cardinalInput = new Vector2(0f, Mathf.Sign(rawInput.y));
            else
            {
                float deltaX = Mathf.Abs(rawInput.x - lastTargetingInput.x);
                float deltaY = Mathf.Abs(rawInput.y - lastTargetingInput.y);
                if (deltaX > deltaY)      cardinalInput = new Vector2(Mathf.Sign(rawInput.x), 0f);
                else if (deltaY > deltaX) cardinalInput = new Vector2(0f, Mathf.Sign(rawInput.y));
                else                      cardinalInput = lastTargetingMoveDir;
            }

            lastTargetingInput   = rawInput;
            lastTargetingMoveDir = cardinalInput;
        }
        else
        {
            float yaw = cameraTarget != null ? cameraTarget.eulerAngles.y : 0f;
            Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);
            forwardAxis   = yawOnly * Vector3.forward;
            rightAxis     = yawOnly * Vector3.right;
            cardinalInput = moveInput;
        }
    }

    private void HandleRotation(Vector3 moveDir)
    {
        if (moveDir.sqrMagnitude <= 0.01f) return;
        Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
        modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
    }

    private void EmitLocomotionStateIfChanged(bool force)
    {
        if (!force && _hasEmittedLocomotion
            && _lastEmittedWallhug  == isWallhugging
            && _lastEmittedLedge    == isLedgeGrabbing
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

    // ─── Public API ────────────────────────────────────────────────────────────

    public bool IsDashing               => Dash != null && Dash.IsDashing;
    public bool IsWallhugging           => isWallhugging;
    public Vector3 WallNormal           => Wallhug != null ? Wallhug.WallNormal : Vector3.zero;
    public bool IsWallJumping           => Wallhug != null && Wallhug.IsWallJumping;
    public Vector3 WallJumpNormal       => Wallhug != null ? Wallhug.WallJumpNormal : Vector3.zero;
    public bool IsLedgeGrabbing         => isLedgeGrabbing;
    public bool IsClimbingLedge         => isClimbingLedge;
    public bool IsSteppingUp            => isSteppingUp;
    /// <summary>True when standing on ground (from the physics ground check, not velocity).</summary>
    public bool IsGrounded              => physics != null && physics.Ground.isGrounded;
    public float DashCooldownNormalized => Dash != null ? Dash.DashCooldownNormalized : 0f;
    public Vector2 MoveInput            => moveInput;
    public Transform CameraTarget       => cameraTarget;

    private void OnItemEquipped(OnItemEquipEvent e)
    {
        if (e.item != null) ChangeMoveSpeed(e.item.handWeightMultiplier);
    }
    private void OnItemUnequipped(OnItemUnequipEvent e) => ResetMoveSpeed();

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

    // ─── Auto step-up ─────────────────────────────────────────────────────────

    private void TryAutoStep(GroundInfo ground, Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput)
    {
        if (!ground.isGrounded) return;
        if (Dash != null && Dash.IsDashing) return;
        if (cardinalInput.sqrMagnitude < 0.01f) return;

        CapsuleCollider col = physics.Collider;
        Vector3 inputDir = (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized;
        Vector3 feetPos  = physics.GetFeetPosition();

        // 1. Look for a wall in front at foot level.
        Vector3 lowProbe = feetPos + Vector3.up * 0.1f;
        if (!Physics.Raycast(lowProbe, inputDir, out RaycastHit wallHit,
            col.radius + 0.15f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        float wallAngle = Vector3.Angle(wallHit.normal, Vector3.up);
        if (wallAngle < physics.maxGroundAngle || wallAngle >= 135f) return;

        // 2. Find the top surface of the step.
        Vector3 highProbe = wallHit.point + Vector3.up * (autoStepMaxHeight + 0.1f) - wallHit.normal * 0.05f;
        if (!Physics.Raycast(highProbe, Vector3.down, out RaycastHit stepHit,
            autoStepMaxHeight + 0.2f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        // 3. Step height must be in range.
        float stepHeight = stepHit.point.y - feetPos.y;
        if (stepHeight <= 0.001f || stepHeight > autoStepMaxHeight) return;

        // 4. Top surface must be walkable.
        if (Vector3.Angle(stepHit.normal, Vector3.up) > physics.maxGroundAngle) return;

        // 5. Verify the capsule fits on top of the step.
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

        stepStartPos = transform.position;
        stepEndPos   = standingPos;
        stepUpTimer  = 0f;
        isSteppingUp = true;
    }

    private void TickStepUp()
    {
        stepUpTimer += Time.fixedDeltaTime;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stepUpTimer / autoStepUpDuration));
        physics.SetPosition(Vector3.Lerp(stepStartPos, stepEndPos, t));

        if (stepUpTimer >= autoStepUpDuration)
        {
            isSteppingUp = false;
            velocity.y   = groundedGravity;
            physics.ResolveOverlaps();
        }
    }
}
