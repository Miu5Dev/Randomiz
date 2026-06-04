using UnityEngine;

/// <summary>
/// Pure randomness — returns a random multiplier in [min, max] each time the
/// state is scored. Sprinkle on states to make choices feel less mechanical.
/// </summary>
[CreateAssetMenu(fileName = "WMod_Random", menuName = "Enemies/WeightModifier/Random")]
public class SOWMod_Random : SOWeightModifier
{
    public float min = 0.5f;
    public float max = 1.5f;

    public override float Evaluate(EnemyContext ctx) => Random.Range(min, max);
}
