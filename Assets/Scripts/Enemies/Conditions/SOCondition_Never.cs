using UnityEngine;

/// <summary>Never transitions — the enemy stays in this phase for good.</summary>
[CreateAssetMenu(fileName = "Cond_Never", menuName = "Enemies/Condition/Never")]
public class SOCondition_Never : SOPhaseCondition
{
    public override bool IsTrue(EnemyContext ctx) => false;
}
