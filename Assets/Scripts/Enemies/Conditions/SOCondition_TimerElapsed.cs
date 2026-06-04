using UnityEngine;

/// <summary>Transitions after spending a set number of seconds in the phase.</summary>
[CreateAssetMenu(fileName = "Cond_TimerElapsed", menuName = "Enemies/Condition/Timer Elapsed")]
public class SOCondition_TimerElapsed : SOPhaseCondition
{
    public float seconds = 8f;

    public override bool IsTrue(EnemyContext ctx) => ctx.phaseTimer >= seconds;
}
