using UnityEngine;

/// <summary>
/// Swings the equipped weapon when the player is within range, respecting a
/// cooldown. Delegates the actual hit to the weapon's own hitbox via Use(),
/// so it reuses the exact same damage logic the player's sword uses.
/// </summary>
[CreateAssetMenu(fileName = "Attack_Melee", menuName = "Enemies/Attack/Melee")]
public class SOAttack_Melee : SOAttackPattern
{
    [Tooltip("Distance at which the enemy attempts a swing. Keep ≤ the weapon's hitbox reach.")]
    public float attackRange = 1.5f;
    [Tooltip("Seconds between swings.")]
    public float cooldown = 1.2f;

    private class State { public float cd; }

    public override void Enter(EnemyContext ctx) => ctx.attackState = new State();

    public override void Tick(EnemyContext ctx)
    {
        if (!(ctx.attackState is State s)) { s = new State(); ctx.attackState = s; }
        if (s.cd > 0f) s.cd -= ctx.deltaTime;

        if (ctx.weapon == null) return;
        if (s.cd > 0f) return;
        if (ctx.FlatDistanceToPlayer > attackRange) return;

        ctx.FacePlayer();
        ctx.weapon.Use(ctx.self.gameObject);
        s.cd = cooldown;
    }
}
