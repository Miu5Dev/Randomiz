using UnityEngine;

/// <summary>
/// Optional marker for the player's default respawn position. On Start it registers its
/// world position with the <see cref="CheckpointManager"/> as the fallback spawn used
/// when no checkpoint has been activated yet — so dying before reaching any checkpoint
/// still sends the player to a defined point instead of reviving where they fell.
///
/// An explicit spawn point always wins over the manager's auto-captured player start
/// position. Place one per level; the Game Setup Wizard can create it.
/// </summary>
public class PlayerSpawnPoint : MonoBehaviour
{
    private void Start()
    {
        // force: true — an authored spawn point overrides the auto-captured start position.
        CheckpointManager.Instance?.SetDefaultSpawn(transform.position, force: true);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.5f);
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
        Gizmos.DrawSphere(transform.position, 0.25f);
    }
#endif
}
