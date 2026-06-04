using UnityEngine;

/// <summary>
/// Spikes a state's weight for a brief window right after the player presses
/// attack — the basis for reactive dodging. The <see cref="EnemyController"/>
/// also forces an immediate re-decision on a nearby player attack, so a state
/// using this modifier can trigger instantly instead of waiting for the next tick.
/// </summary>
[CreateAssetMenu(fileName = "WMod_PlayerAttacking", menuName = "Enemies/WeightModifier/Player Attacking")]
public class SOWMod_PlayerAttacking : SOWeightModifier
{
    [Tooltip("Seconds after the player's attack during which this state is boosted.")]
    public float window = 0.6f;
    [Tooltip("Multiplier applied inside the reaction window (e.g. 6 = strongly favour dodging).")]
    public float multiplier = 6f;

    public override float Evaluate(EnemyContext ctx) =>
        ctx.timeSincePlayerAttack <= window ? multiplier : 1f;
}
