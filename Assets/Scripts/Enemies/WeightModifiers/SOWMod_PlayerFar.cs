using UnityEngine;

/// <summary>Boosts (or suppresses) a state when the player is beyond a distance.</summary>
[CreateAssetMenu(fileName = "WMod_PlayerFar", menuName = "Enemies/WeightModifier/Player Far")]
public class SOWMod_PlayerFar : SOWeightModifier
{
    public float distance = 5f;
    [Tooltip("Multiplier applied while the player is farther than the distance.")]
    public float multiplier = 1.5f;

    public override float Evaluate(EnemyContext ctx) =>
        !ctx.HasPlayer || ctx.FlatDistanceToPlayer > distance ? multiplier : 1f;
}
