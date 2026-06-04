using UnityEngine;

/// <summary>Boosts (or suppresses) a state when health drops below a threshold.</summary>
[CreateAssetMenu(fileName = "WMod_LowHealth", menuName = "Enemies/WeightModifier/Low Health")]
public class SOWMod_LowHealth : SOWeightModifier
{
    [Range(0f, 1f)] public float threshold = 0.4f;
    [Tooltip("Multiplier applied while health is below the threshold.")]
    public float multiplier = 2f;

    public override float Evaluate(EnemyContext ctx) =>
        ctx.HealthNormalized < threshold ? multiplier : 1f;
}
