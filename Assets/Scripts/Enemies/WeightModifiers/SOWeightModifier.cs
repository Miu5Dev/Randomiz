using UnityEngine;

/// <summary>
/// Base class for a contextual weight multiplier used by the decision system.
/// Each state in a phase has a base weight; every attached modifier returns a
/// multiplier (1 = no change, &gt;1 = more likely, &lt;1 = less likely) that is
/// folded into the final pick probability. Combining multiplicative modifiers
/// lets you express nuanced, RNG-flavoured behaviour without code.
/// </summary>
public abstract class SOWeightModifier : ScriptableObject
{
    /// <summary>Returns a non-negative multiplier for the state being scored.</summary>
    public abstract float Evaluate(EnemyContext ctx);
}
