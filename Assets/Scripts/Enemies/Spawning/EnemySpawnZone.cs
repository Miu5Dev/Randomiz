using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Trigger-based enemy spawner. When the player enters this zone for the first time
/// (or after it resets), enemies are distributed across spawn points and instantiated
/// with a staggered delay.
///
/// Lifecycle:
///   • Player ENTERS: if <c>canSpawn</c> is true AND no live enemies remain, begin spawning.
///   • Player IN ZONE: no new spawns; enemies remain active.
///   • Player EXITS mid-fight: live enemies stay; player fights the same wave on re-entry.
///   • All enemies DEFEATED: if <c>respawnOnReentry</c> true → ready to spawn next entry;
///     if false → zone permanently cleared.
///   • Scene RELOADS: all zones reset and can spawn again.
///
/// Optional persistence: stores cleared state via SaveManager (if <c>zoneId</c> is set).
/// </summary>
[RequireComponent(typeof(Collider))]
public class EnemySpawnZone : MonoBehaviour
{
    [Header("Enemy Setup")]
    [SerializeField] private SOEnemy[] enemyTypes;
    [SerializeField] private int[] spawnCounts;
    [Tooltip("If empty, enemies spawn randomly within the collider bounds.")]
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float spawnRadius = 2f;

    [Header("Spawn Behavior")]
    [SerializeField] private float spawnDelay = 0.5f;
    [SerializeField] private bool respawnOnReentry = true;

    [Header("Persistence")]
    [SerializeField] private string zoneId;

    // ─── Runtime ─────────────────────────────────────────────────────────────
    private Collider _zoneCollider;
    private List<EnemyController> _liveEnemies = new List<EnemyController>();
    private bool _canSpawn = true;
    private bool _isCleared = false;
    private bool _playerInZone = false;
    private Coroutine _spawnCoroutine;

    private const string CLEARED_KEY_PREFIX = "EnemyZone_Cleared_";

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        // PhysicsController has no Rigidbody — kinematic Rigidbody on this volume
        // is required for OnTriggerEnter to fire against the player's static collider.
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        _zoneCollider = GetComponent<Collider>();

        // Load cleared state from save if persistence is enabled.
        if (!string.IsNullOrEmpty(zoneId))
        {
            string key = CLEARED_KEY_PREFIX + zoneId;
            // Example: if your SaveManager has a GetBool(key) method, use it here.
            // For now, we'll assume no initial clearing state unless SaveManager sets it.
            _isCleared = false; // TODO: load from SaveManager if available
        }

        // Initialize with cleared state.
        if (_isCleared)
            _canSpawn = false;
    }

    private void OnTriggerEnter(Collider collision)
    {
        if (!IsPlayer(collision)) return;

        _playerInZone = true;

        // Prune stale refs before checking live count.
        _liveEnemies.RemoveAll(e => e == null || !e.IsAlive);

        // Only spawn if allowed AND no enemies from a previous wave are still alive.
        if (_canSpawn && !_isCleared && _liveEnemies.Count == 0)
        {
            _spawnCoroutine = StartCoroutine(SpawnEnemiesCoroutine());
            _canSpawn = false;
        }
    }

    private void OnTriggerExit(Collider collision)
    {
        if (!IsPlayer(collision)) return;

        _playerInZone = false;

        // Stop mid-spawn if the player exits before all enemies have appeared.
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }

        // Do NOT re-enable spawning here: if enemies are still alive they remain
        // in the world and the player should fight the same ones on re-entry.
        // Respawn readiness is handled in FixedUpdate once the wave is fully cleared.
    }

    // ─── Spawning ─────────────────────────────────────────────────────────────

    private IEnumerator SpawnEnemiesCoroutine()
    {
        // Build a spawn queue from the enemy types and counts.
        List<SOEnemy> spawnQueue = new List<SOEnemy>();
        for (int i = 0; i < enemyTypes.Length; i++)
        {
            if (enemyTypes[i] == null) continue;
            int count = (i < spawnCounts.Length) ? spawnCounts[i] : 1;
            for (int j = 0; j < count; j++)
            {
                spawnQueue.Add(enemyTypes[i]);
            }
        }

        // Shuffle the queue to randomize spawn order.
        ShuffleList(spawnQueue);

        // Spawn each enemy at a staggered interval.
        foreach (SOEnemy enemyType in spawnQueue)
        {
            SpawnSingleEnemy(enemyType);
            yield return new WaitForSeconds(spawnDelay);
        }

        _spawnCoroutine = null;
    }

    private void SpawnSingleEnemy(SOEnemy enemyType)
    {
        if (enemyType == null) return;

        // Resolve which prefab to use.
        GameObject prefab = enemyType.prefab;
        if (prefab == null)
        {
            Debug.LogWarning($"Enemy type '{enemyType.name}' has no prefab assigned.", this);
            return;
        }

        // Pick a spawn location.
        Vector3 spawnPos = PickSpawnLocation();

        // Instantiate and register.
        GameObject instance = Instantiate(prefab, spawnPos, Quaternion.identity);
        EnemyController controller = instance.GetComponent<EnemyController>();

        if (controller != null)
        {
            _liveEnemies.Add(controller);
            // Subscribe to the enemy's death event (handled via EventBus).
            // The EnemyController already dispatches OnDieEvent on death.
        }
        else
        {
            Debug.LogWarning($"Spawned enemy '{instance.name}' has no EnemyController component.", instance);
        }
    }

    private Vector3 PickSpawnLocation()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            // Use a designated spawn point with random offset.
            Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];
            return point.position + RandomOffset();
        }

        // Fallback: pick a random point within the collider bounds.
        return RandomPointInCollider() + RandomOffset();
    }

    private Vector3 RandomPointInCollider()
    {
        if (_zoneCollider is BoxCollider box)
        {
            Vector3 localMin = -box.size * 0.5f + box.center;
            Vector3 localMax = box.size * 0.5f + box.center;
            Vector3 randomLocal = new Vector3(
                Random.Range(localMin.x, localMax.x),
                Random.Range(localMin.y, localMax.y),
                Random.Range(localMin.z, localMax.z)
            );
            return transform.TransformPoint(randomLocal);
        }

        if (_zoneCollider is SphereCollider sphere)
        {
            Vector3 randomDir = Random.insideUnitSphere;
            float randomDist = Random.Range(0f, sphere.radius);
            return transform.TransformPoint(sphere.center + randomDir * randomDist);
        }

        // Fallback for other collider types.
        return transform.position;
    }

    private Vector3 RandomOffset()
    {
        // Random offset within spawnRadius.
        Vector2 xz = Random.insideUnitCircle * spawnRadius;
        float y = Random.Range(-spawnRadius * 0.5f, spawnRadius * 0.5f);
        return new Vector3(xz.x, y, xz.y);
    }

    // ─── Enemy Tracking ───────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        // Prune dead enemies from the tracking list.
        _liveEnemies.RemoveAll(enemy => enemy == null || !enemy.IsAlive);

        // Wave fully cleared: all spawned enemies are dead and no spawn is in progress.
        // This fires regardless of whether the player is in the zone or not.
        if (_liveEnemies.Count == 0 && !_isCleared && !_canSpawn && _spawnCoroutine == null)
        {
            if (respawnOnReentry)
                _canSpawn = true;  // ready for next entry
            else
                ClearZone();       // permanently cleared
        }
    }

    private void ClearZone()
    {
        _isCleared = true;
        _canSpawn = false;

        // Persist cleared state if a zone ID is set.
        if (!string.IsNullOrEmpty(zoneId))
        {
            string key = CLEARED_KEY_PREFIX + zoneId;
            // TODO: SaveManager.SetBool(key, true);
        }
    }

    // ─── Utility ──────────────────────────────────────────────────────────────

    private bool IsPlayer(Collider collision)
    {
        // Check for PlayerMovement component (adjust if your player detection differs).
        return collision.GetComponent<PlayerMovement>() != null;
    }

    private void ShuffleList<T>(List<T> list)
    {
        // Fisher-Yates shuffle.
        for (int i = list.Count - 1; i > 0; i--)
        {
            int randomIdx = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[randomIdx];
            list[randomIdx] = temp;
        }
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (_zoneCollider == null)
            _zoneCollider = GetComponent<Collider>();

        if (_zoneCollider == null) return;

        // Determine color based on state.
        Color gizmoColor;
        if (_isCleared)
            gizmoColor = new Color(0f, 1f, 0f, 0.3f);  // Green: cleared
        else if (_playerInZone)
            gizmoColor = new Color(1f, 0f, 0f, 0.3f);  // Red: player in zone
        else if (_canSpawn)
            gizmoColor = new Color(1f, 0.9f, 0f, 0.3f); // Yellow: can spawn
        else
            gizmoColor = new Color(1f, 0.5f, 0f, 0.3f); // Orange: waiting

        Gizmos.color = gizmoColor;

        // Draw collider representation.
        if (_zoneCollider is BoxCollider box)
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.DrawCube(box.center, box.size);
        }
        else if (_zoneCollider is SphereCollider sphere)
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.DrawSphere(sphere.center, sphere.radius);
        }

        Gizmos.matrix = Matrix4x4.identity;

        // Draw spawn points if assigned.
        if (spawnPoints != null)
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.8f);
            foreach (Transform point in spawnPoints)
            {
                if (point != null)
                    Gizmos.DrawWireSphere(point.position, 0.25f);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (_zoneCollider == null)
            _zoneCollider = GetComponent<Collider>();

        if (_zoneCollider == null) return;

        // When selected, draw spawn radius preview.
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            foreach (Transform point in spawnPoints)
            {
                if (point != null)
                    Gizmos.DrawWireSphere(point.position, spawnRadius);
            }
        }
    }
#endif
}
