using UnityEngine;

/// <summary>
/// Simple marker for a potential enemy spawn location within an EnemySpawnZone.
/// This component is purely positional and optional — spawn points can be auto-generated
/// within the zone's collider bounds if this is not used.
///
/// Optional override: if you assign an <see cref="SOEnemy"/> here, this specific point
/// will spawn that enemy type instead of the zone's default types.
/// </summary>
public class EnemySpawnPoint : MonoBehaviour
{
    [Tooltip("If set, this spawn point will spawn this enemy type instead of the zone's default.")]
    [SerializeField] private SOEnemy overrideEnemyType;

    /// <summary>
    /// Returns the enemy type to spawn at this point, or null if using the zone's default.
    /// </summary>
    public SOEnemy OverrideEnemyType => overrideEnemyType;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Draw a small sphere to mark the spawn point in the editor.
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.8f);
        Gizmos.DrawSphere(transform.position, 0.3f);
    }
#endif
}
