using UnityEngine;

/// <summary>No attack — a purely passive state (repositioning, taunting, hiding).</summary>
[CreateAssetMenu(fileName = "Attack_None", menuName = "Enemies/Attack/None")]
public class SOAttack_None : SOAttackPattern
{
    public override void Tick(EnemyContext ctx) { }
}
