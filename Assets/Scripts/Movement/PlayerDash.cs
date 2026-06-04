using UnityEngine;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PhysicsController))]
public class PlayerDash : MonoBehaviour
{
    [Header("Dash / Roll")]
    public float dashSpeed = 18f;
    public float dashDuration = 0.2f;
    public float dashCooldown = 0.8f;
    public AnimationCurve dashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Targeting Jump")]
    public float targetingJumpForce = 8f;
    public float backflipHorizontalForce = 6f;
    public float targetingForwardJumpForce = 4f;

    private PlayerMovement pm;
    private PhysicsController physics;

    private bool isDashing;
    private float dashTimer;
    private float dashCooldownTimer;
    private Vector3 dashDirection;

    public bool IsDashing => isDashing;
    public float DashCooldownNormalized => Mathf.Clamp01(dashCooldownTimer / dashCooldown);

    void Awake()
    {
        pm = GetComponent<PlayerMovement>();
        physics = GetComponent<PhysicsController>();
    }

    public void TickCooldown()
    {
        if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.fixedDeltaTime;
    }

    public bool CanDash(GroundInfo ground)
    {
        return !isDashing && dashCooldownTimer <= 0f
               && ground.isGrounded && !pm.isJumping
               && !pm.isWallhugging && !pm.nearWall;
    }

    public void StartDash()
    {
        float yaw = pm.cameraTarget != null ? pm.cameraTarget.eulerAngles.y : 0f;
        Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);
        Vector2 moveInput = pm.MoveInput;
        if (moveInput.sqrMagnitude > 0.01f)
        {
            Vector3 camForward = yawOnly * Vector3.forward;
            Vector3 camRight = yawOnly * Vector3.right;
            dashDirection = (camForward * moveInput.y + camRight * moveInput.x).normalized;
        }
        else dashDirection = pm.modelTransform.forward;
        pm.modelTransform.rotation = Quaternion.LookRotation(dashDirection, Vector3.up);
        isDashing = true;
        dashTimer = 0f;
        pm.velocity.y = 0f;
    }

    public void StartTargetingJump()
    {
        Vector2 moveInput = pm.MoveInput;
        Vector3 direction;
        if (moveInput.magnitude < 0.1f) direction = pm.modelTransform.forward;
        else
        {
            Vector2 cardinal = Mathf.Abs(moveInput.x) > Mathf.Abs(moveInput.y)
                ? new Vector2(Mathf.Sign(moveInput.x), 0f)
                : new Vector2(0f, Mathf.Sign(moveInput.y));
            direction = (pm.modelTransform.forward * cardinal.y + pm.modelTransform.right * cardinal.x).normalized;
        }
        Vector3 horizontal = direction * dashSpeed;
        pm.velocity.x = horizontal.x;
        pm.velocity.z = horizontal.z;
        pm.velocity.y = targetingJumpForce;
        pm.isJumping = true;
        dashCooldownTimer = dashCooldown;
    }

    // Returns true if the dash consumed this frame (FixedUpdate should return early).
    public bool TickDash(GroundInfo ground)
    {
        if (!isDashing) return false;
        dashTimer += Time.fixedDeltaTime;
        float t = Mathf.Clamp01(dashTimer / dashDuration);
        float speedMul = dashCurve.Evaluate(t);
        pm.velocity.x = dashDirection.x * dashSpeed * speedMul;
        pm.velocity.z = dashDirection.z * dashSpeed * speedMul;
        pm.velocity.y = ground.isGrounded ? pm.groundedGravity : pm.velocity.y;
        if (dashTimer >= dashDuration)
        {
            isDashing = false;
            dashCooldownTimer = dashCooldown;
        }
        physics.Move(pm.velocity * Time.fixedDeltaTime);
        return true;
    }

    public void ResetCooldown()
    {
        dashCooldownTimer = dashCooldown;
    }
}
