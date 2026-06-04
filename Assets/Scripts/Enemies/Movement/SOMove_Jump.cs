using UnityEngine;

/// <summary>
/// Leaps toward the player in arcs (frog / pouncer style). Builds up on the
/// ground between hops, then launches with horizontal + vertical impulse.
/// Ignored for flying parts.
/// </summary>
[CreateAssetMenu(fileName = "Move_Jump", menuName = "Enemies/Movement/Jump")]
public class SOMove_Jump : SOMovementPattern
{
    public float jumpForce = 8f;
    public float horizontalForce = 5f;
    [Tooltip("Seconds waited on the ground between leaps.")]
    public float jumpInterval = 1.5f;
    [Tooltip("Stop leaping once this close to the player.")]
    public float stopDistance = 1f;

    private class State { public float timer; }

    public override void Enter(EnemyContext ctx) => ctx.movementState = new State { timer = jumpInterval };

    public override void Tick(EnemyContext ctx)
    {
        if (!(ctx.movementState is State s)) { s = new State(); ctx.movementState = s; }

        // Mid-air: keep current horizontal motion, just face the player.
        if (!ctx.ground.isGrounded)
        {
            ctx.FacePlayer();
            return;
        }

        Vector3 to = ctx.ToPlayerFlat;
        float dist = to.magnitude;
        ctx.StopHorizontal();
        ctx.FacePlayer();

        if (dist <= stopDistance) return;

        s.timer -= ctx.deltaTime;
        if (s.timer <= 0f)
        {
            Vector3 dir = to / Mathf.Max(dist, 0.0001f);
            ctx.velocity = dir * horizontalForce + Vector3.up * jumpForce;
            s.timer = jumpInterval;
        }
    }
}
