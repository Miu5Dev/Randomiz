using UnityEngine;

/// <summary>
/// Advances toward the player along a serpentine path, making the approach hard
/// to predict / hit. Stops weaving once inside <see cref="stopDistance"/>.
/// </summary>
[CreateAssetMenu(fileName = "Move_Zigzag", menuName = "Enemies/Movement/Zigzag")]
public class SOMove_Zigzag : SOMovementPattern
{
    [Min(0.1f)] public float speedMultiplier = 1f;
    [Tooltip("Sideways weave speed.")]
    public float width = 2f;
    [Tooltip("Weave cycles per second.")]
    public float frequency = 1.5f;
    public float stopDistance = 1.2f;

    public override void Tick(EnemyContext ctx)
    {
        Vector3 to = ctx.ToPlayerFlat;
        float dist = to.magnitude;
        if (dist <= stopDistance) { ctx.StopHorizontal(); ctx.FacePlayer(); return; }

        Vector3 dir   = to / dist;
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        float   s     = Mathf.Sin(ctx.stateTimer * frequency * Mathf.PI * 2f);

        Vector3 vel = dir * (ctx.moveSpeed * speedMultiplier) + right * (s * width);
        ctx.SetHorizontalVelocity(vel);
        ctx.FaceDirection(dir);
    }
}
