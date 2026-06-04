using UnityEngine;

/// <summary>Transitions when current health drops below a fraction of max.</summary>
[CreateAssetMenu(fileName = "Cond_HealthBelow", menuName = "Enemies/Condition/Health Below")]
public class SOCondition_HealthBelow : SOPhaseCondition
{
    [Range(0f, 1f)] public float threshold = 0.5f;

    public override bool IsTrue(EnemyContext ctx) => ctx.HealthNormalized < threshold;
}
