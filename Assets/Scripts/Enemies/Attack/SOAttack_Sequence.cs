using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs a list of other attack patterns in order, each for a fixed time slice,
/// then loops or stops. Lets you author boss combos like Charge → Stomp → Combo
/// as a single reusable attack.
///
/// Each sub-attack gets its own private runtime state, swapped in and out of the
/// shared context around every call.
/// </summary>
[CreateAssetMenu(fileName = "Attack_Sequence", menuName = "Enemies/Attack/Sequence")]
public class SOAttack_Sequence : SOAttackPattern
{
    [System.Serializable]
    public class Step
    {
        public SOAttackPattern attack;
        [Tooltip("Seconds this step runs before advancing to the next.")]
        public float duration = 1f;
    }

    public List<Step> steps = new();
    [Tooltip("Restart from the first step after the last one finishes.")]
    public bool loop = true;

    private class State
    {
        public int      index = -1;
        public float    timer;
        public object[] subStates;
        public bool     done;
    }

    public override void Enter(EnemyContext ctx)
    {
        var s = new State { subStates = new object[steps.Count] };
        ctx.attackState = s;
        if (steps.Count > 0) StartStep(ctx, s, 0);
    }

    public override void Tick(EnemyContext ctx)
    {
        if (!(ctx.attackState is State s) || steps.Count == 0) return;
        if (s.done) return;

        s.timer -= ctx.deltaTime;
        if (s.timer <= 0f)
        {
            int next = s.index + 1;
            if (next >= steps.Count)
            {
                if (!loop) { s.done = true; return; }
                next = 0;
            }
            StartStep(ctx, s, next);
        }

        // Run the active sub-attack with its own state slot swapped in.
        Step step = steps[s.index];
        if (step?.attack != null)
        {
            ctx.attackState = s.subStates[s.index];
            step.attack.Tick(ctx);
            s.subStates[s.index] = ctx.attackState;
            ctx.attackState = s;                 // restore sequence state
        }
    }

    private void StartStep(EnemyContext ctx, State s, int index)
    {
        s.index = index;
        s.timer = steps[index].duration;

        SOAttackPattern atk = steps[index].attack;
        if (atk != null)
        {
            ctx.attackState = null;
            atk.Enter(ctx);
            s.subStates[index] = ctx.attackState;
        }
        ctx.attackState = s;                     // restore sequence state
    }
}
