using UnityEngine;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PhysicsController))]
public class PlayerWallhug : MonoBehaviour
{
    [Header("Wallhug")]
    public float wallhugJumpForce = 6f;
    [Range(0f, 1f)] public float wallhugExitThreshold = 0.3f;
    [Range(0f, 1f)] public float wallhugSpeedMultiplier = 1f;
    [Tooltip("Minimum wall height required to enter wallhug. Lower steps are handled by auto step-up or normal movement.")]
    public float wallhugMinWallHeight = 1.0f;
    [Tooltip("Maximum reach above the head where a ledge is searched for when pressing cardinal-up during wallhug.")]
    public float wallhugLedgeJumpMaxReach = 2.0f;

    private PlayerMovement pm;
    private PhysicsController physics;

    private Vector3 wallNormal;
    private bool isWallJumping;
    private Vector3 wallJumpNormal;
    private bool wallhugUpBlocked;

    public Vector3 WallNormal => wallNormal;
    public bool IsWallJumping => isWallJumping;
    public Vector3 WallJumpNormal => wallJumpNormal;

    void Awake()
    {
        pm = GetComponent<PlayerMovement>();
        physics = GetComponent<PhysicsController>();
    }

    public bool CheckNear(GroundInfo ground, Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput, out Vector3 wallDir)
    {
        wallDir = Vector3.zero;

        if (cardinalInput.sqrMagnitude > 0.01f)
        {
            Vector3 diagDir = (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized;
            if (CheckNearInDirection(ground, diagDir)) { wallDir = diagDir; return true; }

            if (Mathf.Abs(cardinalInput.y) > 0.01f)
            {
                Vector3 fwdDir = forwardAxis * Mathf.Sign(cardinalInput.y);
                if (CheckNearInDirection(ground, fwdDir)) { wallDir = fwdDir; return true; }
            }

            if (Mathf.Abs(cardinalInput.x) > 0.01f)
            {
                Vector3 rightDir = rightAxis * Mathf.Sign(cardinalInput.x);
                if (CheckNearInDirection(ground, rightDir)) { wallDir = rightDir; return true; }
            }
        }
        else
        {
            Vector3[] dirs = { forwardAxis, -forwardAxis, rightAxis, -rightAxis };
            foreach (Vector3 dir in dirs)
                if (CheckNearInDirection(ground, dir)) { wallDir = dir; return true; }
        }

        return false;
    }

    private bool CheckNearInDirection(GroundInfo ground, Vector3 dir)
    {
        CollisionInfo wallCheck = physics.CheckDirection(dir, 0.1f);
        if (!wallCheck.hit || !wallCheck.IsWall(physics.maxGroundAngle)) return false;
        if (!ground.isGrounded && !HasWallSideGround(wallCheck.point, wallCheck.normal)) return false;

        CapsuleCollider col = physics.Collider;
        Vector3 feetPos = physics.GetFeetPosition();
        Vector3 heightCheckOrigin = new Vector3(pm.transform.position.x,
                                                feetPos.y + wallhugMinWallHeight,
                                                pm.transform.position.z);
        return Physics.Raycast(heightCheckOrigin, dir, col.radius + 0.25f,
            physics.collisionMask, QueryTriggerInteraction.Ignore);
    }

    public void TryEnter(GroundInfo ground, Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput)
    {
        PlayerDash dash = pm.Dash;
        if (dash != null && dash.IsDashing) return;
        if (!CheckNear(ground, forwardAxis, rightAxis, cardinalInput, out Vector3 wallDir)) return;

        CollisionInfo wallCheck = physics.CheckDirection(wallDir, 0.1f);
        pm.isWallhugging = true;
        wallNormal = wallCheck.normal;
        wallhugUpBlocked = cardinalInput.y > wallhugExitThreshold;
    }

    public void TickWallhug(GroundInfo ground, Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput)
    {
        CollisionInfo wallCheck = physics.CheckDirection(-wallNormal, 0.15f);
        if (!wallCheck.hit || !wallCheck.IsWall(physics.maxGroundAngle))
        {
            pm.isWallhugging = false;
            ApplyGravityAndMove(ground);
            return;
        }
        wallNormal = wallCheck.normal;

        bool isGrounded = ground.isGrounded || HasWallSideGround(wallCheck.point, wallNormal);

        if (!isGrounded && !pm.isJumping)
        {
            pm.isWallhugging = false;
            ApplyGravityAndMove(ground);
            return;
        }

        if (!pm.interactHeld)
        {
            pm.isWallhugging = false;
            ApplyGravityAndMove(ground);
            return;
        }

        if (wallhugUpBlocked)
        {
            if (cardinalInput.y <= wallhugExitThreshold) wallhugUpBlocked = false;
        }
        else if (cardinalInput.y > wallhugExitThreshold)
        {
            TryJumpToLedge();
            return;
        }

        bool hasInput = cardinalInput.sqrMagnitude > 0.01f;
        Vector3 inputDir3D = hasInput
            ? (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized
            : Vector3.zero;

        Vector3 wallRight = Vector3.Cross(Vector3.up, wallNormal).normalized;
        float lateralInput = hasInput ? Vector3.Dot(inputDir3D, wallRight) : 0f;

        float inputMagnitude = pm.MoveInput.magnitude;
        float targetSpeed = (inputMagnitude >= pm.runThreshold) ? pm.runSpeed : pm.moveSpeed;
        pm.currentSpeed = Mathf.MoveTowards(pm.currentSpeed, targetSpeed, pm.runAcceleration * Time.fixedDeltaTime);

        Vector3 moveVel = wallRight * (lateralInput * pm.currentSpeed * wallhugSpeedMultiplier);
        pm.velocity.x = moveVel.x;
        pm.velocity.z = moveVel.z;

        Vector3 faceWall = new Vector3(-wallNormal.x, 0f, -wallNormal.z);
        if (faceWall.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(faceWall.normalized, Vector3.up);
            pm.modelTransform.rotation = Quaternion.Slerp(pm.modelTransform.rotation, targetRot, pm.rotationSpeed * Time.fixedDeltaTime);
        }

        if (!isGrounded)
            pm.velocity.y += pm.gravity * Time.fixedDeltaTime;
        else if (pm.velocity.y < 0f)
            pm.velocity.y = pm.groundedGravity;

        MoveResult result = physics.Move(pm.velocity * Time.fixedDeltaTime);
        if (result.HitCeiling() && pm.velocity.y > 0f) pm.velocity.y = 0f;
    }

    public void StartJump()
    {
        pm.velocity.x = 0f;
        pm.velocity.z = 0f;
        pm.velocity.y = wallhugJumpForce;
        pm.isJumping = true;
        isWallJumping = true;
        wallJumpNormal = wallNormal;
        pm.isWallhugging = false;
        pm.Dash?.ResetCooldown();
    }

    public void TryJumpToLedge()
    {
        CapsuleCollider col = physics.Collider;
        Vector3 toWall = -wallNormal;
        Vector3 headPos = physics.GetHeadPosition();

        Vector3 scanXZ = new Vector3(pm.transform.position.x, 0f, pm.transform.position.z)
                       + new Vector3(toWall.x, 0f, toWall.z).normalized * (col.radius + 0.05f);
        Vector3 scanOrigin = new Vector3(scanXZ.x, headPos.y + wallhugLedgeJumpMaxReach, scanXZ.z);

        if (!Physics.Raycast(scanOrigin, Vector3.down, out RaycastHit hit,
            wallhugLedgeJumpMaxReach, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        if (Vector3.Angle(hit.normal, Vector3.up) > physics.maxGroundAngle) return;
        if (hit.point.y <= headPos.y - 0.1f) return;

        pm.isWallhugging = false;
        pm.interactHeld = false;
        pm.velocity.x = 0f;
        pm.velocity.z = 0f;
        pm.velocity.y = wallhugJumpForce;
        pm.isJumping = true;
        isWallJumping = true;
        wallJumpNormal = wallNormal;
    }

    public void HandleJumpLanding()
    {
        if (!isWallJumping) return;
        isWallJumping = false;
        CollisionInfo reentry = physics.CheckDirection(-wallJumpNormal, 0.15f);
        if (reentry.hit && reentry.IsWall(physics.maxGroundAngle))
        {
            pm.isWallhugging = true;
            wallNormal = reentry.normal;
        }
    }

    public void ClearWallJump()
    {
        isWallJumping = false;
    }

    // Ground detection alongside the wall (for curbs / narrow surfaces).
    public bool HasWallSideGround(Vector3 wallContactPoint, Vector3 wallNorm)
    {
        Vector3 feetPos = physics.GetFeetPosition();
        const float originHeight = 0.25f;
        float castDist = originHeight + physics.groundCheckDistance + 0.25f;

        // Primary probe: 3 cm in front of the wall surface.
        Vector3 nearWallXZ = wallContactPoint + wallNorm * 0.03f;
        Vector3 nearWallOrigin = new Vector3(nearWallXZ.x, feetPos.y + originHeight, nearWallXZ.z);
        if (Physics.Raycast(nearWallOrigin, Vector3.down, castDist,
            physics.collisionMask, QueryTriggerInteraction.Ignore))
            return true;

        // Backup probe: from the player XZ center, shifted toward the wall.
        Vector3 midXZ = new Vector3(pm.transform.position.x, 0f, pm.transform.position.z)
                      - new Vector3(wallNorm.x, 0f, wallNorm.z).normalized * (physics.Collider.radius * 0.4f);
        Vector3 midOrigin = new Vector3(midXZ.x, feetPos.y + originHeight, midXZ.z);
        return Physics.Raycast(midOrigin, Vector3.down, castDist,
            physics.collisionMask, QueryTriggerInteraction.Ignore);
    }

    private void ApplyGravityAndMove(GroundInfo ground)
    {
        if (!ground.isGrounded)
            pm.velocity.y += pm.gravity * Time.fixedDeltaTime;
        else if (pm.velocity.y < 0f)
            pm.velocity.y = pm.groundedGravity;

        MoveResult result = physics.Move(pm.velocity * Time.fixedDeltaTime);
        if (result.HitCeiling() && pm.velocity.y > 0f) pm.velocity.y = 0f;
    }
}
