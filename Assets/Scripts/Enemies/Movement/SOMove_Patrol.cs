using UnityEngine;

/// <summary>
/// Walks through the controller's patrol waypoints. Typically used as the idle
/// (unaware) movement. Supports looping or ping-pong traversal.
/// </summary>
[CreateAssetMenu(fileName = "Move_Patrol", menuName = "Enemies/Movement/Patrol")]
public class SOMove_Patrol : SOMovementPattern
{
    [Min(0.1f)] public float speedMultiplier = 0.6f;
    public float reachDistance = 0.4f;
    [Tooltip("Ping-pong reverses at the ends; otherwise it loops back to the first point.")]
    public bool pingPong = false;

    private class State { public int index; public int dir = 1; }

    public override void Enter(EnemyContext ctx) => ctx.movementState = new State();

    public override void Tick(EnemyContext ctx)
    {
        var pts = ctx.patrolPoints;
        if (pts == null || pts.Length == 0) { ctx.StopHorizontal(); return; }
        if (!(ctx.movementState is State s)) { s = new State(); ctx.movementState = s; }

        s.index = Mathf.Clamp(s.index, 0, pts.Length - 1);
        Transform target = pts[s.index];
        if (target == null) { ctx.StopHorizontal(); return; }

        Vector3 to = target.position - ctx.self.position;
        to.y = 0f;

        if (to.magnitude <= reachDistance)
        {
            Advance(s, pts.Length);
            ctx.StopHorizontal();
            return;
        }

        Vector3 dir = to.normalized;
        ctx.SetHorizontalVelocity(dir * ctx.moveSpeed * speedMultiplier);
        ctx.FaceDirection(dir);
    }

    private void Advance(State s, int count)
    {
        if (pingPong)
        {
            if (s.index + s.dir < 0 || s.index + s.dir >= count) s.dir = -s.dir;
            s.index += s.dir;
        }
        else
        {
            s.index = (s.index + 1) % count;
        }
    }
}
