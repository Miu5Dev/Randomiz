using UnityEngine;

/// <summary>Boosts (or suppresses) a state when the player is within a distance.</summary>
[CreateAssetMenu(fileName = "WMod_PlayerNear", menuName = "Enemies/WeightModifier/Player Near")]
public class SOWMod_PlayerNear : SOWeightModifier
{
    public float distance = 3f;
    [Tooltip("Multiplier applied while the player is nearer than the distance.")]
    public float multiplier = 1.5f;

    public override float Evaluate(EnemyContext ctx) =>
        ctx.HasPlayer && ctx.FlatDistanceToPlayer <= distance ? multiplier : 1f;
}
