using UnityEngine;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PhysicsController))]
public class PlayerLedgeGrab : MonoBehaviour
{
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

    private PlayerMovement pm;
    private PhysicsController physics;

    private Vector3 ledgeTopPoint;
    private Vector3 ledgeWallNormal;
    private bool ledgeBackwardBlocked;
    private float climbTimer;
    private Vector3 climbStartPos;
    private Vector3 climbEndPos;
    private float ledgeGrabReleaseCooldown;

    public float ReleaseCooldown => ledgeGrabReleaseCooldown;

    // Exposed for HandIK: where the grabbed ledge edge is, and the wall normal.
    public Vector3 LedgeTopPoint   => ledgeTopPoint;
    public Vector3 LedgeWallNormal => ledgeWallNormal;

    /// <summary>
    /// Lets PlayerAnimator match the script-driven climb motion to the climb clip
    /// length, so the body and the animation finish together (no snap at the end).
    /// </summary>
    public void SetClimbDuration(float seconds)
    {
        if (seconds > 0.05f) ledgeClimbDuration = seconds;
    }

    void Awake()
    {
        pm = GetComponent<PlayerMovement>();
        physics = GetComponent<PhysicsController>();
    }

    public void TickReleaseCooldown()
    {
        if (ledgeGrabReleaseCooldown > 0f) ledgeGrabReleaseCooldown -= Time.fixedDeltaTime;
    }

    public void TryGrabLedge()
    {
        Vector3 horizontalVel = new Vector3(pm.velocity.x, 0f, pm.velocity.z);
        PlayerWallhug wallhug = pm.Wallhug;
        Vector3 checkDir;
        if (wallhug != null && wallhug.IsWallJumping)
            checkDir = -wallhug.WallJumpNormal;
        else if (horizontalVel.sqrMagnitude > 0.01f)
            checkDir = horizontalVel.normalized;
        else
            checkDir = new Vector3(pm.modelTransform.forward.x, 0f, pm.modelTransform.forward.z).normalized;

        if (TryDetectLedge(checkDir, out Vector3 ledgeTop, out Vector3 wallNorm))
        {
            AttachToLedge(ledgeTop, wallNorm);
            return;
        }

        if (!pm.isJumping
            && pm.velocity.y < 0.5f && pm.velocity.y > -4f
            && horizontalVel.magnitude >= falloffMinSpeed)
        {
            Vector3 backDir = -horizontalVel.normalized;
            if (TryDetectLedgeAtFeet(backDir, out ledgeTop, out wallNorm))
            {
                if (Physics.Raycast(pm.transform.position, Vector3.down, falloffMinDropHeight,
                    physics.collisionMask, QueryTriggerInteraction.Ignore))
                    return;
                AttachToLedge(ledgeTop, wallNorm);
            }
        }
    }

    private void AttachToLedge(Vector3 top, Vector3 norm)
    {
        pm.isLedgeGrabbing = true;
        pm.Wallhug?.ClearWallJump();
        pm.isJumping = false;
        ledgeTopPoint = top;
        ledgeWallNormal = norm;
        ledgeBackwardBlocked = pm.MoveInput.y < -ExitThreshold();
        pm.velocity = Vector3.zero;
        SnapToHangPosition();
    }

    public void TickLedgeGrab(Vector3 forwardAxis, Vector3 rightAxis, Vector2 cardinalInput)
    {
        CapsuleCollider col = physics.Collider;
        float exitThreshold = ExitThreshold();

        bool hasInput = cardinalInput.sqrMagnitude > 0.01f;
        Vector3 inputDir3D = hasInput
            ? (forwardAxis * cardinalInput.y + rightAxis * cardinalInput.x).normalized
            : Vector3.zero;

        if (ledgeBackwardBlocked)
        {
            if (cardinalInput.y >= -exitThreshold) ledgeBackwardBlocked = false;
        }
        else if (cardinalInput.y < -exitThreshold)
        {
            pm.isLedgeGrabbing = false;
            ledgeGrabReleaseCooldown = 0.5f;
            return;
        }

        if (!IsLedgeEdgePresent())
        {
            pm.isLedgeGrabbing = false;
            return;
        }

        Vector3 ledgeRight = Vector3.Cross(Vector3.up, ledgeWallNormal).normalized;
        float lateralInput = hasInput ? Vector3.Dot(inputDir3D, ledgeRight) : 0f;

        pm.velocity = ledgeRight * (lateralInput * pm.moveSpeed * ledgeGrabSpeedMultiplier);
        pm.velocity.y = 0f;

        if (pm.velocity.sqrMagnitude > 0.001f)
        {
            Vector3 prevPos = pm.transform.position;
            MoveResult moveResult = physics.Move(pm.velocity * Time.fixedDeltaTime);

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
                        float heightDiff = ledgeHit.point.y - ledgeTopPoint.y;
                        bool compatible = heightDiff >= 0f
                            ? heightDiff <= ledgeMaxReachUp
                            : -heightDiff <= ledgeMaxReachDown;
                        if (compatible)
                        {
                            ledgeTopPoint   = ledgeHit.point;
                            ledgeWallNormal = cornerWall.Value.normal;
                            SnapToHangPosition();
                            ApplyWallFacing(ledgeWallNormal);
                            return;
                        }
                    }
                    physics.SetPosition(prevPos);
                    pm.velocity = Vector3.zero;
                }
            }
            else
            {
                if (!IsLedgeEdgePresent())
                {
                    physics.SetPosition(prevPos);
                    pm.velocity = Vector3.zero;
                }
            }
        }

        Vector3 pos = pm.transform.position;
        pos.y = ledgeTopPoint.y - col.center.y - col.height * 0.5f;
        physics.SetPosition(pos);
        ApplyWallFacing(ledgeWallNormal);
    }

    public void StartClimb()
    {
        CapsuleCollider col = physics.Collider;

        Vector3 endPos;
        endPos.y = ledgeTopPoint.y - col.center.y + col.height * 0.5f + 0.05f;
        Vector3 inward = -ledgeWallNormal * (col.radius * 2f + 0.2f);
        endPos.x = pm.transform.position.x + inward.x;
        endPos.z = pm.transform.position.z + inward.z;

        Vector3 checkCenter = endPos + col.center;
        Vector3 capsuleBottom = checkCenter + Vector3.down * (col.height * 0.5f - col.radius);
        Vector3 capsuleTop    = checkCenter + Vector3.up   * (col.height * 0.5f - col.radius);
        if (Physics.CheckCapsule(capsuleBottom, capsuleTop, col.radius - 0.02f,
            physics.collisionMask, QueryTriggerInteraction.Ignore))
            return;

        climbStartPos = pm.transform.position;
        climbEndPos = endPos;
        pm.isClimbingLedge = true;
        pm.isLedgeGrabbing = false;
        climbTimer = 0f;
        pm.velocity = Vector3.zero;
        pm.Dash?.ResetCooldown();
    }

    // Optional: when >= 0, the body follows the ANIMATION's progress (0..1) instead
    // of its own timer, so the climb motion matches the clip's non-linear curve.
    // PlayerAnimator feeds this from the ClimbUp state's normalizedTime.
    private float _animClimbProgress = -1f;
    public void SetClimbProgress(float t01) => _animClimbProgress = Mathf.Clamp01(t01);

    public void TickClimb()
    {
        float t;
        if (_animClimbProgress >= 0f)
        {
            // Drive the body by the clip's own progress (matches its motion curve).
            t = _animClimbProgress;
            physics.SetPosition(Vector3.Lerp(climbStartPos, climbEndPos, t));
            if (t >= 0.999f)
            {
                pm.isClimbingLedge = false;
                pm.velocity = Vector3.zero;
                physics.ResolveOverlaps();
                _animClimbProgress = -1f;
            }
            return;
        }

        // Fallback: timer-driven (no animator hooked up).
        climbTimer += Time.fixedDeltaTime;
        t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(climbTimer / ledgeClimbDuration));
        physics.SetPosition(Vector3.Lerp(climbStartPos, climbEndPos, t));

        if (climbTimer >= ledgeClimbDuration)
        {
            pm.isClimbingLedge = false;
            pm.velocity = Vector3.zero;
            physics.ResolveOverlaps();
        }
    }

    private float ExitThreshold()
    {
        PlayerWallhug wallhug = pm.Wallhug;
        return wallhug != null ? wallhug.wallhugExitThreshold : 0.3f;
    }

    private bool IsLedgeEdgePresent()
    {
        float dist = physics.Collider.radius + 0.2f;
        Vector3 toWall = -ledgeWallNormal;

        // There must be a wall just below the ledge top (real edge).
        Vector3 below = new Vector3(pm.transform.position.x, ledgeTopPoint.y - 0.05f, pm.transform.position.z);
        if (!Physics.Raycast(below, toWall, dist, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        // There must NOT be a wall just above — if there is, it's a solid block, not a hangable edge.
        Vector3 above = new Vector3(pm.transform.position.x, ledgeTopPoint.y + 0.05f, pm.transform.position.z);
        if (Physics.Raycast(above, toWall, dist, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

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

    private void ApplyWallFacing(Vector3 wallNorm)
    {
        Vector3 faceDir = new Vector3(-wallNorm.x, 0f, -wallNorm.z);
        if (faceDir.sqrMagnitude < 0.01f) return;
        Quaternion targetRot = Quaternion.LookRotation(faceDir.normalized, Vector3.up);
        pm.modelTransform.rotation = Quaternion.Slerp(pm.modelTransform.rotation, targetRot, pm.rotationSpeed * Time.fixedDeltaTime);
    }

    private bool TryDetectLedge(Vector3 wallDirection, out Vector3 ledgeTopOut, out Vector3 wallNormalOut)
    {
        ledgeTopOut = Vector3.zero;
        wallNormalOut = Vector3.zero;

        Vector3 headPos = physics.GetHeadPosition();
        if (!Physics.Raycast(headPos, wallDirection, out RaycastHit wallHit,
            ledgeDetectionDistance, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        float angle = Vector3.Angle(wallHit.normal, Vector3.up);
        if (angle < physics.maxGroundAngle || angle >= 135f) return false;

        // Reject walls that continue above the expected ledge range.
        Vector3 tallCheckOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) + wallHit.normal * 0.05f;
        if (Physics.Raycast(tallCheckOrigin, -wallHit.normal, 0.2f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        Vector3 searchOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) - wallHit.normal * 0.05f;
        if (!Physics.Raycast(searchOrigin, Vector3.down, out RaycastHit ledgeHit,
            ledgeTopSearchHeight + 0.5f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        float headY = headPos.y;
        if (ledgeHit.point.y < headY - ledgeMaxReachDown || ledgeHit.point.y > headY + ledgeMaxReachUp)
            return false;

        ledgeTopOut = ledgeHit.point;
        wallNormalOut = wallHit.normal;
        return true;
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

        Vector3 tallCheckOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) + wallHit.normal * 0.05f;
        if (Physics.Raycast(tallCheckOrigin, -wallHit.normal, 0.2f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        Vector3 searchOrigin = wallHit.point + Vector3.up * (ledgeTopSearchHeight + 0.1f) - wallHit.normal * 0.05f;
        if (!Physics.Raycast(searchOrigin, Vector3.down, out RaycastHit ledgeHit,
            ledgeTopSearchHeight + 0.5f, physics.collisionMask, QueryTriggerInteraction.Ignore))
            return false;

        float feetY = feetPos.y;
        if (ledgeHit.point.y < feetY - ledgeMaxReachDown || ledgeHit.point.y > feetY + ledgeMaxReachUp)
            return false;

        ledgeTopOut = ledgeHit.point;
        wallNormalOut = wallHit.normal;
        return true;
    }
}
