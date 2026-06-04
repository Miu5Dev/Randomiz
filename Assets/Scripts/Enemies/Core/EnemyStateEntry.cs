using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One micro-behaviour option inside a phase. The decision system weighted-picks
/// between the phase's states every few seconds. A state pairs a movement pattern
/// with an attack pattern, and tunes how likely / how long it runs.
/// </summary>
[System.Serializable]
public class EnemyStateEntry
{
    [Tooltip("Debug-friendly name, e.g. \"Aggressive\", \"Reposition\", \"Charge\".")]
    public string label = "State";

    public SOMovementPattern movement;
    public SOAttackPattern   attack;

    [Tooltip("Base probability before modifiers and noise are applied.")]
    public float baseWeight = 50f;

    [Tooltip("Min / max seconds to commit to this state once picked. " +
             "X = minimum (won't re-decide before this), Y = maximum (forces a re-decide).")]
    public Vector2 duration = new Vector2(1.5f, 3f);

    [Tooltip("Multiplicative weight modifiers evaluated when scoring this state.")]
    public List<SOWeightModifier> modifiers = new();

    /// <summary>
    /// Final weight for this state given the current context. Noise is applied as a
    /// proportional jitter (a fraction, e.g. 0.2 = ±20%) so it never lets a near-zero
    /// base weight randomly outvote a high one.
    /// </summary>
    public float ResolveWeight(EnemyContext ctx, float noise)
    {
        float w = baseWeight;
        for (int i = 0; i < modifiers.Count; i++)
            if (modifiers[i] != null)
                w *= modifiers[i].Evaluate(ctx);

        if (noise > 0f) w *= 1f + Random.Range(-noise, noise);
        return Mathf.Max(0f, w);
    }
}
