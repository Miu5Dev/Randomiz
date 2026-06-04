using UnityEngine;

/// <summary>
/// Telegraphed area attack centred on the enemy: winds up (giving the player a
/// window to escape), then deals damage to everything inside a radius. Roots the
/// enemy in place during the move.
/// </summary>
[CreateAssetMenu(fileName = "Attack_Stomp", menuName = "Enemies/Attack/Stomp")]
public class SOAttack_Stomp : SOAttackPattern
{
    [Tooltip("The player must be within this distance to start a stomp.")]
    public float triggerRange = 2.5f;
    public float radius      = 3f;
    public float damage      = 1f;
    public float windupTime  = 0.7f;
    public float recoverTime = 0.4f;
    public float cooldown    = 3f;
    [Tooltip("Vertical offset of the AoE centre.")]
    public float heightOffset = 0.5f;
    public LayerMask hitMask = ~0;

    private enum Phase { Ready, Windup, Recover }
    private class State { public Phase phase = Phase.Ready; public float timer; public float cd; }

    public override void Enter(EnemyContext ctx) => ctx.attackState = new State();

    public override void Tick(EnemyContext ctx)
    {
        if (!(ctx.attackState is State s)) { s = new State(); ctx.attackState = s; }
        if (s.cd > 0f) s.cd -= ctx.deltaTime;

        switch (s.phase)
        {
            case Phase.Ready:
                if (s.cd <= 0f && ctx.FlatDistanceToPlayer <= triggerRange)
                {
                    s.phase = Phase.Windup;
                    s.timer = windupTime;
                }
                break;

            case Phase.Windup:
                ctx.StopHorizontal();
                ctx.FacePlayer();
                s.timer -= ctx.deltaTime;
                if (s.timer <= 0f)
                {
                    Vector3 center = ctx.self.position + Vector3.up * heightOffset;
                    EnemyAttackUtil.SphereDamage(ctx.self.gameObject, center, radius, hitMask, damage);
                    s.phase = Phase.Recover;
                    s.timer = recoverTime;
                }
                break;

            case Phase.Recover:
                ctx.StopHorizontal();
                s.timer -= ctx.deltaTime;
                if (s.timer <= 0f) { s.phase = Phase.Ready; s.cd = cooldown; }
                break;
        }
    }
}
