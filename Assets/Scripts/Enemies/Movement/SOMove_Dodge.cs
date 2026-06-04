using UnityEngine;

/// <summary>
/// A short evasive burst — sideways and/or away from the player. Pair with a
/// state heavily weighted by <see cref="SOWMod_PlayerAttacking"/> and a short
/// duration so the enemy hops out of the way when the player swings.
/// </summary>
[CreateAssetMenu(fileName = "Move_Dodge", menuName = "Enemies/Movement/Dodge")]
public class SOMove_Dodge : SOMovementPattern
{
    public enum DodgeDir { Backward, Sideways, AwaySideways }

    public DodgeDir direction = DodgeDir.AwaySideways;
    public float dodgeSpeed = 11f;
    [Tooltip("How long the burst lasts. Usually shorter than the state's min duration.")]
    public float burstTime = 0.35f;

    private class State { public Vector3 dir; public float timer; }

    public override void Enter(EnemyContext ctx)
    {
        var s = new State { timer = burstTime };

        Vector3 to = ctx.ToPlayerFlat;
        Vector3 toDir = to.sqrMagnitude > 0.0001f ? to.normalized : ctx.self.forward;
        Vector3 away  = -toDir;
        Vector3 right = Vector3.Cross(Vector3.up, toDir) * (Random.value < 0.5f ? 1f : -1f);

        s.dir = direction switch
        {
            DodgeDir.Backward     => away,
            DodgeDir.Sideways     => right,
            _                     => (away + right).normalized,
        };

        ctx.movementState = s;
    }

    public override void Tick(EnemyContext ctx)
    {
        if (!(ctx.movementState is State s)) { Enter(ctx); s = (State)ctx.movementState; }

        ctx.FacePlayer();
        if (s.timer > 0f)
        {
            ctx.SetHorizontalVelocity(s.dir * dodgeSpeed);
            s.timer -= ctx.deltaTime;
        }
        else
        {
            ctx.StopHorizontal();
        }
    }
}
