using UnityEngine;

/// <summary>Transitions when the player gets within a distance.</summary>
[CreateAssetMenu(fileName = "Cond_PlayerInRange", menuName = "Enemies/Condition/Player In Range")]
public class SOCondition_PlayerInRange : SOPhaseCondition
{
    public float distance = 3f;

    public override bool IsTrue(EnemyContext ctx) =>
        ctx.HasPlayer && ctx.FlatDistanceToPlayer <= distance;
}
