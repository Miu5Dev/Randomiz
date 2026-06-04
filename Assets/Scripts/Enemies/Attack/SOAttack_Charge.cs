using UnityEngine;

/// <summary>
/// Telegraphed rush: winds up in place, then dashes toward the player's position
/// dealing contact damage. Overrides movement velocity while dashing, so it pairs
/// with any movement pattern. Great signature boss move.
/// </summary>
[CreateAssetMenu(fileName = "Attack_Charge", menuName = "Enemies/Attack/Charge")]
public class SOAttack_Charge : SOAttackPattern
{
    [Tooltip("Distance at which the charge can trigger.")]
    public float triggerRange = 6f;
    public float windupTime  = 0.6f;
    public float chargeSpeed  = 14f;
    public float chargeDuration = 0.5f;
    public float recoverTime  = 0.5f;
    public float cooldown     = 4f;

    [Header("Contact Damage")]
    public float damage    = 1f;
    public float hitRadius = 0.8f;
    public LayerMask hitMask = ~0;

    private enum Phase { Ready, Windup, Dashing, Recover }
    private class State { public Phase phase = Phase.Ready; public float timer; public float cd; public Vector3 dir; public bool hit; }

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
                    Vector3 to = ctx.ToPlayerFlat;
                    s.dir   = to.sqrMagnitude > 0.0001f ? to.normalized : ctx.self.forward;
                    s.phase = Phase.Dashing;
                    s.timer = chargeDuration;
                    s.hit   = false;
                }
                break;

            case Phase.Dashing:
                ctx.SetHorizontalVelocity(s.dir * chargeSpeed);
                if (!s.hit && ctx.DistanceToPlayer <= hitRadius * 2f)
                {
                    Vector3 center = ctx.self.position + s.dir * hitRadius + Vector3.up * 0.5f;
                    EnemyAttackUtil.SphereDamage(ctx.self.gameObject, center, hitRadius, hitMask, damage);
                    s.hit = true;   // contact damage lands once per charge
                }
                s.timer -= ctx.deltaTime;
                if (s.timer <= 0f) { s.phase = Phase.Recover; s.timer = recoverTime; }
                break;

            case Phase.Recover:
                ctx.StopHorizontal();
                s.timer -= ctx.deltaTime;
                if (s.timer <= 0f) { s.phase = Phase.Ready; s.cd = cooldown; }
                break;
        }
    }
}
