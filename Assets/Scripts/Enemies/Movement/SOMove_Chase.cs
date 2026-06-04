using UnityEngine;

/// <summary>Moves straight toward the player, stopping at a set distance.</summary>
[CreateAssetMenu(fileName = "Move_Chase", menuName = "Enemies/Movement/Chase")]
public class SOMove_Chase : SOMovementPattern
{
    [Min(0.1f)] public float speedMultiplier = 1f;
    [Tooltip("How close the enemy gets before it stops advancing.")]
    public float stopDistance = 1.2f;

    public override void Tick(EnemyContext ctx)
    {
        Vector3 to = ctx.ToPlayerFlat;
        float dist = to.magnitude;

        if (dist > stopDistance)
        {
            Vector3 dir = to / Mathf.Max(dist, 0.0001f);
            ctx.SetHorizontalVelocity(dir * ctx.moveSpeed * speedMultiplier);
            ctx.FaceDirection(dir);
        }
        else
        {
            ctx.StopHorizontal();
            ctx.FacePlayer();
        }
    }
}
