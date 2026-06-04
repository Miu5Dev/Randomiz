using UnityEngine;

/// <summary>
/// Transitions when another part of the same boss is dead. Enables setups like a
/// hidden body that only activates once the head is destroyed.
/// </summary>
[CreateAssetMenu(fileName = "Cond_PartDead", menuName = "Enemies/Condition/Part Dead")]
public class SOCondition_PartDead : SOPhaseCondition
{
    [Tooltip("Name of the boss part that must be dead (as set in EnemyPartData.partName).")]
    public string partName;

    public override bool IsTrue(EnemyContext ctx)
    {
        if (ctx.bossGroup == null || string.IsNullOrEmpty(partName)) return false;
        // Dead = registered but no longer alive.
        return ctx.bossGroup.HasPart(partName) && !ctx.bossGroup.IsPartAlive(partName);
    }
}
