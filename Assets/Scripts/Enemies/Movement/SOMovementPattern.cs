using UnityEngine;

/// <summary>
/// Base class for a modular movement behaviour. A pattern only expresses the
/// DESIRED motion by writing <see cref="EnemyContext.velocity"/> (mostly the X/Z
/// components; Y too when the enemy can fly). The controller layers gravity and
/// navigation (climb / wall-steer) on top.
///
/// Patterns are SHARED assets — never store per-enemy runtime state in fields.
/// Use <see cref="EnemyContext.movementState"/> (allocated in Enter) instead.
/// </summary>
public abstract class SOMovementPattern : ScriptableObject
{
    /// <summary>Called once when this pattern becomes the active movement.</summary>
    public virtual void Enter(EnemyContext ctx) { }

    /// <summary>Called every FixedUpdate while active. Write ctx.velocity.</summary>
    public abstract void Tick(EnemyContext ctx);

#if UNITY_EDITOR
    /// <summary>Optional scene gizmo to visualise the pattern.</summary>
    public virtual void DrawGizmos(EnemyContext ctx) { }
#endif
}
