using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Combines several conditions with AND / OR logic. Lets you express rules like
/// "health &lt; 30% AND player within 4m" without writing new code.
/// </summary>
[CreateAssetMenu(fileName = "Cond_Combo", menuName = "Enemies/Condition/Combo (AND-OR)")]
public class SOCondition_Combo : SOPhaseCondition
{
    public enum Mode { All, Any }

    [Tooltip("All = every condition must be true (AND). Any = at least one (OR).")]
    public Mode mode = Mode.All;
    public List<SOPhaseCondition> conditions = new();

    public override bool IsTrue(EnemyContext ctx)
    {
        if (conditions.Count == 0) return false;

        if (mode == Mode.All)
        {
            for (int i = 0; i < conditions.Count; i++)
                if (conditions[i] == null || !conditions[i].IsTrue(ctx)) return false;
            return true;
        }

        for (int i = 0; i < conditions.Count; i++)
            if (conditions[i] != null && conditions[i].IsTrue(ctx)) return true;
        return false;
    }
}
