using UnityEngine;

/// <summary>
/// Wanders to random points around the spawn position, pausing/repicking on a
/// timer. Useful idle behaviour for ambient creatures that aren't patrolling
/// a fixed route.
/// </summary>
[CreateAssetMenu(fileName = "Move_Random", menuName = "Enemies/Movement/Random Wander")]
public class SOMove_Random : SOMovementPattern
{
    [Min(0.1f)] public float speedMultiplier = 0.5f;
    [Tooltip("Radius around the spawn point to wander within.")]
    public float wanderRadius = 5f;
    [Tooltip("Max seconds before picking a new target even if not reached.")]
    public float changeInterval = 3f;
    public float reachDistance = 0.5f;

    private class State { public Vector3 target; public float timer; public bool has; }

    public override void Enter(EnemyContext ctx) => ctx.movementState = new State();

    public override void Tick(EnemyContext ctx)
    {
        if (!(ctx.movementState is State s)) { s = new State(); ctx.movementState = s; }

        s.timer -= ctx.deltaTime;
        Vector3 toTarget = s.target - ctx.self.position;
        toTarget.y = 0f;

        if (!s.has || s.timer <= 0f || toTarget.magnitude <= reachDistance)
        {
            Vector2 r = Random.insideUnitCircle * wanderRadius;
            s.target = ctx.spawnPosition + new Vector3(r.x, 0f, r.y);
            s.timer  = changeInterval;
            s.has    = true;
            toTarget = s.target - ctx.self.position;
            toTarget.y = 0f;
        }

        if (toTarget.magnitude <= reachDistance) { ctx.StopHorizontal(); return; }

        Vector3 dir = toTarget.normalized;
        ctx.SetHorizontalVelocity(dir * ctx.moveSpeed * speedMultiplier);
        ctx.FaceDirection(dir);
    }
}
