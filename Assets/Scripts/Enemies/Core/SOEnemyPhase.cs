using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A macro behaviour band for an enemy (e.g. "Calm", "Enraged"). While in a
/// phase, the decision system picks between <see cref="states"/>; when
/// <see cref="exitCondition"/> becomes true the controller advances to the next
/// phase in the part's list.
///
/// Created as an embedded sub-asset by the Enemy Creator window.
/// </summary>
public class SOEnemyPhase : ScriptableObject
{
    [Tooltip("Debug-friendly phase name.")]
    public string label = "Phase";

    [Tooltip("Seconds between decisions (random in [X, Y]).")]
    public Vector2 decisionInterval = new Vector2(1.5f, 3f);

    [Range(0f, 1f)]
    [Tooltip("Proportional weight jitter for natural variety (0.2 = ±20%).")]
    public float noise = 0.2f;

    [Tooltip("Candidate micro-behaviours for this phase.")]
    public List<EnemyStateEntry> states = new();

    [Tooltip("When true, advance to the next phase. Null / Never = stay forever.")]
    public SOPhaseCondition exitCondition;
}
