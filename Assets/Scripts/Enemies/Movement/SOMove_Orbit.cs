using UnityEngine;

/// <summary>
/// Pure circular orbit around the player at a fixed radius and constant angular
/// speed. Unlike Strafe it doesn't bias toward/away — it just rings the target.
/// </summary>
[CreateAssetMenu(fileName = "Move_Orbit", menuName = "Enemies/Movement/Orbit")]
public class SOMove_Orbit : SOMovementPattern
{
    [Min(0.1f)] public float speedMultiplier = 1f;
    public float radius = 4f;
    [Tooltip("+1 clockwise, -1 counter-clockwise.")]
    public int direction = 1;
    [Tooltip("How hard it corrects back toward the orbit radius.")]
    public float radiusStiffness = 1f;

    public override void Tick(EnemyContext ctx)
    {
        Vector3 to = ctx.ToPlayerFlat;
        float dist = to.magnitude;
        if (dist < 0.001f) { ctx.StopHorizontal(); return; }

        Vector3 radial  = to / dist;
        Vector3 tangent = Vector3.Cross(Vector3.up, radial) * Mathf.Sign(direction == 0 ? 1 : direction);
        float   correction = Mathf.Clamp((dist - radius) * radiusStiffness, -1f, 1f);

        Vector3 desired = (tangent + radial * correction).normalized;
        ctx.SetHorizontalVelocity(desired * ctx.moveSpeed * speedMultiplier);
        ctx.FacePlayer();
    }
}
