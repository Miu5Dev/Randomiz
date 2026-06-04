using UnityEngine;

/// <summary>
/// Doesn't move. Optionally keeps facing the player (turrets, a hiding boss body,
/// a charging windup that roots the enemy in place).
/// </summary>
[CreateAssetMenu(fileName = "Move_Stationary", menuName = "Enemies/Movement/Stationary")]
public class SOMove_Stationary : SOMovementPattern
{
    public bool facePlayer = true;

    public override void Tick(EnemyContext ctx)
    {
        ctx.StopHorizontal();
        if (facePlayer) ctx.FacePlayer();
    }
}
