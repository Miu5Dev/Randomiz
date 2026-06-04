using UnityEngine;

/// <summary>
/// Sways left and right relative to the player without closing in — a floating
/// head bobbing side to side, for example. Faces the player throughout.
/// </summary>
[CreateAssetMenu(fileName = "Move_Oscillate", menuName = "Enemies/Movement/Oscillate")]
public class SOMove_Oscillate : SOMovementPattern
{
    [Tooltip("Sideways speed at the peak of the sway.")]
    public float amplitude = 2f;
    [Tooltip("Sway cycles per second.")]
    public float frequency = 1f;

    public override void Tick(EnemyContext ctx)
    {
        Vector3 to = ctx.ToPlayerFlat;
        if (to.sqrMagnitude < 0.0001f) { ctx.StopHorizontal(); return; }

        Vector3 dir   = to.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        float   s     = Mathf.Sin(ctx.stateTimer * frequency * Mathf.PI * 2f);

        ctx.SetHorizontalVelocity(right * (s * amplitude));
        ctx.FaceDirection(dir);
    }
}
