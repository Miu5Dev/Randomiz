using UnityEngine;

/// <summary>
/// Circles the player while keeping a preferred distance — good for tense,
/// "sizing each other up" combat. Combine with a Melee attack for darting jabs.
/// </summary>
[CreateAssetMenu(fileName = "Move_Strafe", menuName = "Enemies/Movement/Strafe")]
public class SOMove_Strafe : SOMovementPattern
{
    [Min(0.1f)] public float speedMultiplier = 1f;
    [Tooltip("Preferred distance to orbit at.")]
    public float radius = 3f;
    [Tooltip("+1 clockwise, -1 counter-clockwise.")]
    public int direction = 1;

    public override void Tick(EnemyContext ctx)
    {
        Vector3 to = ctx.ToPlayerFlat;
        float dist = to.magnitude;
        if (dist < 0.001f) { ctx.StopHorizontal(); return; }

        Vector3 radial  = to / dist;
        Vector3 tangent = Vector3.Cross(Vector3.up, radial) * Mathf.Sign(direction == 0 ? 1 : direction);
        float   radialErr = Mathf.Clamp(dist - radius, -1f, 1f);

        Vector3 desired = (tangent + radial * radialErr).normalized;
        ctx.SetHorizontalVelocity(desired * ctx.moveSpeed * speedMultiplier);
        ctx.FacePlayer();
    }
}
