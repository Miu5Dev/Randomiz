using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One independently-controlled body of an enemy. A normal enemy has a single
/// part; a boss can have several (e.g. a flying head + a hiding body), each with
/// its own stats, perception, idle behaviour and phase list.
/// </summary>
[System.Serializable]
public class EnemyPartData
{
    [Header("Identity")]
    public string partName = "Body";

    [Header("Stats")]
    [Min(1)] public int   maxHearts = 3;
    public float          moveSpeed = 2.5f;
    public float          turnSpeed = 12f;

    [Header("Navigation")]
    [Tooltip("Flies freely in 3D with no gravity.")]
    public bool canFly;
    [Tooltip("Climbs walls it runs into (adds upward velocity).")]
    public bool canClimb;
    [Tooltip("Slides along walls it can't climb instead of stopping.")]
    public bool wallSteer = true;

    [Header("Combat")]
    [Tooltip("Item used by attack patterns (a copy is Instantiate()d at runtime).")]
    public SOItem weapon;

    [Header("Perception")]
    public EnemyVision vision = new();

    [Header("Behaviour")]
    [Tooltip("Movement used while the player has NOT been perceived (patrol, wander, idle...).")]
    public SOMovementPattern idleMovement;

    [Tooltip("Phases run once the enemy engages the player, in order.")]
    public List<SOEnemyPhase> phases = new();

    [Header("Dodge Reaction (reflex)")]
    [Tooltip("Movement run as an instant interrupt when the player attacks nearby. " +
             "Leave empty to disable reflex dodging. Independent of the weighted decision system.")]
    public SOMovementPattern dodgeReaction;
    [Range(0f, 1f)]
    [Tooltip("Chance to dodge when the player attacks within reaction distance.")]
    public float dodgeChance = 0.6f;
    [Tooltip("How long the dodge reaction overrides normal behaviour.")]
    public float dodgeReactionTime = 0.4f;
}

/// <summary>
/// Top-level enemy definition. Authored through the Enemy Creator window, which
/// embeds every pattern / condition / modifier as a sub-asset so a whole enemy
/// (boss or grunt) lives in a single file.
/// </summary>
[CreateAssetMenu(fileName = "Enemy", menuName = "Enemies/Enemy Data")]
public class SOEnemy : ScriptableObject
{
    public string enemyName = "Enemy";

    [Tooltip("One entry per independently-controlled body. 1 = normal enemy, N = multi-part boss.")]
    public List<EnemyPartData> parts = new() { new EnemyPartData() };
}
