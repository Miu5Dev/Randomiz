using UnityEngine;

/// <summary>
/// Runs directly away from the player until a safe distance is reached. Great
/// for cowardly enemies or "kiting" ranged casters paired with a Projectile attack.
/// </summary>
[CreateAssetMenu(fileName = "Move_Flee", menuName = "Enemies/Movement/Flee")]
public class SOMove_Flee : SOMovementPattern
{
    [Min(0.1f)] public float speedMultiplier = 1f;
    [Tooltip("Stops fleeing once the player is at least this far away.")]
    public float safeDistance = 6f;
    [Tooltip("Keep facing the player while backing away (e.g. to keep shooting).")]
    public bool faceWhileFleeing = true;

    public override void Tick(EnemyContext ctx)
    {
        Vector3 to = ctx.ToPlayerFlat;
        float dist = to.magnitude;

        if (dist >= safeDistance || dist < 0.001f)
        {
            ctx.StopHorizontal();
            if (faceWhileFleeing) ctx.FacePlayer();
            return;
        }

        Vector3 away = -(to / dist);
        ctx.SetHorizontalVelocity(away * ctx.moveSpeed * speedMultiplier);
        if (faceWhileFleeing) ctx.FacePlayer();
        else ctx.FaceDirection(away);
    }
}
