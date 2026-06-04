using UnityEngine;

/// <summary>
/// Base class for a modular attack behaviour. Runs alongside a movement pattern.
/// Manages its own timing (windups, cooldowns, combo indices) through
/// <see cref="EnemyContext.attackState"/> and triggers damage by calling the
/// equipped weapon's <c>Use()</c> or raising <see cref="OnDamageDealtEvent"/>.
///
/// Patterns are SHARED assets — keep runtime state in the context, not fields.
/// </summary>
public abstract class SOAttackPattern : ScriptableObject
{
    /// <summary>Called once when this pattern becomes the active attack.</summary>
    public virtual void Enter(EnemyContext ctx) { }

    /// <summary>Called every FixedUpdate while active.</summary>
    public abstract void Tick(EnemyContext ctx);

#if UNITY_EDITOR
    /// <summary>Optional scene gizmo (e.g. AoE radius, projectile range).</summary>
    public virtual void DrawGizmos(EnemyContext ctx) { }
#endif
}
