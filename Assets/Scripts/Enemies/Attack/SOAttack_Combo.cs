using UnityEngine;

/// <summary>
/// Chains several weapon swings in quick succession, then enters a longer
/// recovery cooldown. Creates a "flurry" feel versus the single Melee poke.
/// </summary>
[CreateAssetMenu(fileName = "Attack_Combo", menuName = "Enemies/Attack/Combo")]
public class SOAttack_Combo : SOAttackPattern
{
    public float attackRange = 1.5f;
    [Min(1)] public int strikes = 3;
    [Tooltip("Seconds between consecutive strikes in the combo.")]
    public float timeBetween = 0.35f;
    [Tooltip("Recovery cooldown after the whole combo finishes.")]
    public float cooldown = 2f;

    private class State { public float cd; public int index; public float timer; public bool active; }

    public override void Enter(EnemyContext ctx) => ctx.attackState = new State();

    public override void Tick(EnemyContext ctx)
    {
        if (!(ctx.attackState is State s)) { s = new State(); ctx.attackState = s; }
        if (s.cd > 0f) s.cd -= ctx.deltaTime;

        if (ctx.weapon == null) return;

        if (!s.active)
        {
            if (s.cd > 0f) return;
            if (ctx.FlatDistanceToPlayer > attackRange) return;
            s.active = true;
            s.index  = 0;
            s.timer  = 0f;
        }

        ctx.FacePlayer();
        s.timer -= ctx.deltaTime;
        if (s.timer > 0f) return;

        ctx.weapon.Use(ctx.self.gameObject);
        s.index++;

        if (s.index >= strikes)
        {
            s.active = false;
            s.cd     = cooldown;
        }
        else
        {
            s.timer = timeBetween;
        }
    }
}
