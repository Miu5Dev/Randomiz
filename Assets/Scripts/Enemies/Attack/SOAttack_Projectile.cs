using UnityEngine;

/// <summary>
/// Fires a <see cref="Projectile"/> prefab toward the player on a cooldown.
/// Pair with Flee/Strafe movement for a kiting ranged enemy. (Misconfiguring a
/// melee enemy with this just makes it shoot harmlessly — by design.)
/// </summary>
[CreateAssetMenu(fileName = "Attack_Projectile", menuName = "Enemies/Attack/Projectile")]
public class SOAttack_Projectile : SOAttackPattern
{
    [Tooltip("Prefab with a Projectile component (or one is added at runtime).")]
    public GameObject projectilePrefab;
    [Tooltip("Max distance to start firing.")]
    public float fireRange = 10f;
    public float cooldown        = 1.5f;
    public float projectileSpeed = 12f;
    public float projectileDamage = 1f;
    public float projectileLife   = 5f;
    public float projectileRadius = 0.2f;
    [Tooltip("Spawn offset from the enemy origin (local-ish, applied in world up/forward).")]
    public Vector3 muzzleOffset = new Vector3(0f, 1f, 0.5f);
    public LayerMask hitMask = ~0;

    private class State { public float cd; }

    public override void Enter(EnemyContext ctx) => ctx.attackState = new State();

    public override void Tick(EnemyContext ctx)
    {
        if (!(ctx.attackState is State s)) { s = new State(); ctx.attackState = s; }
        if (s.cd > 0f) s.cd -= ctx.deltaTime;

        if (projectilePrefab == null || ctx.player == null) return;
        if (s.cd > 0f || ctx.FlatDistanceToPlayer > fireRange) return;

        ctx.FacePlayer();

        Vector3 spawn = ctx.self.position
                      + ctx.self.right   * muzzleOffset.x
                      + Vector3.up       * muzzleOffset.y
                      + ctx.self.forward * muzzleOffset.z;

        Vector3 dir = (ctx.player.position + Vector3.up * 0.5f) - spawn;

        GameObject go  = Object.Instantiate(projectilePrefab, spawn, Quaternion.identity);
        Projectile proj = go.GetComponent<Projectile>();
        if (proj == null) proj = go.AddComponent<Projectile>();
        proj.Launch(ctx.self.gameObject, dir, projectileSpeed, projectileDamage, hitMask, projectileLife, projectileRadius);

        s.cd = cooldown;
    }
}
