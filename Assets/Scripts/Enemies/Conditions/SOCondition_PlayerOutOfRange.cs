using UnityEngine;

/// <summary>Transitions when the player moves beyond a distance.</summary>
[CreateAssetMenu(fileName = "Cond_PlayerOutOfRange", menuName = "Enemies/Condition/Player Out Of Range")]
public class SOCondition_PlayerOutOfRange : SOPhaseCondition
{
    public float distance = 8f;

    public override bool IsTrue(EnemyContext ctx) =>
        !ctx.HasPlayer || ctx.FlatDistanceToPlayer > distance;
}
