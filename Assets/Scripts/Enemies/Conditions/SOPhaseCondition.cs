using UnityEngine;

/// <summary>
/// Base class for a phase transition rule. The controller evaluates the current
/// phase's exit condition every frame; when it returns true the enemy advances
/// to the next phase.
/// </summary>
public abstract class SOPhaseCondition : ScriptableObject
{
    public abstract bool IsTrue(EnemyContext ctx);
}
