using UnityEngine;

/// <summary>
/// Suppresses a state for a while right after it was used, preventing the enemy
/// from spamming the same move. The penalty fades back to neutral over the cooldown.
/// </summary>
[CreateAssetMenu(fileName = "WMod_AfterUsed", menuName = "Enemies/WeightModifier/After Used")]
public class SOWMod_AfterUsed : SOWeightModifier
{
    [Tooltip("Seconds of suppression after the state was last picked.")]
    public float cooldown = 3f;
    [Tooltip("Multiplier immediately after use (ramps back to 1 over the cooldown).")]
    [Range(0f, 1f)] public float minMultiplier = 0.1f;

    public override float Evaluate(EnemyContext ctx)
    {
        if (cooldown <= 0f) return 1f;
        float t = Mathf.Clamp01(ctx.evalTimeSinceUsed / cooldown);
        // t=0 (just used) → minMultiplier, t=1 (cooled down) → 1.
        return Mathf.Lerp(minMultiplier, 1f, t);
    }
}
