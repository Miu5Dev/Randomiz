using UnityEngine;

/// <summary>
/// Perception configuration for a single enemy part. Drives the
/// Unaware → Engaged → LostSight cycle in <see cref="EnemyController"/>.
/// </summary>
[System.Serializable]
public class EnemyVision
{
    [Tooltip("Max distance the enemy can see the player (with line of sight).")]
    public float range = 8f;

    [Range(0f, 360f)]
    [Tooltip("Field-of-view cone width, centred on the enemy's forward.")]
    public float angle = 110f;

    [Tooltip("Radius within which the enemy senses the player even without a clear cone / LoS (hearing).")]
    public float alertRadius = 3f;

    [Tooltip("Seconds the enemy keeps chasing after losing sight before returning to idle.")]
    public float loseSightDelay = 2f;

    [Tooltip("Layers that block line of sight (walls). The player layer should NOT be here.")]
    public LayerMask blockMask = ~0;
}
