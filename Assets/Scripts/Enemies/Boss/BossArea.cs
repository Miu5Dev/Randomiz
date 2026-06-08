using UnityEngine;

/// <summary>
/// Trigger volume that starts a boss encounter the first time the player enters.
///
/// On entry it activates the boss (an in-scene object enabled, or a prefab instantiated),
/// fetches its <see cref="IBossEncounter"/> and calls <see cref="IBossEncounter.BeginEncounter"/>,
/// then raises <see cref="OnBossEncounterStartedEvent"/> so <see cref="BossIntroPopupUI"/>
/// shows the centered name banner. Fires once; optionally skips entirely if the boss is
/// already recorded defeated in <see cref="BossTracker"/>.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BossArea : MonoBehaviour
{
    [Header("Boss identity (optional — falls back to the boss's own values)")]
    [SerializeField] private string bossName;
    [SerializeField] private string bossId;

    [Header("Activation — assign ONE")]
    [Tooltip("A boss already in the scene that starts disabled; it is enabled on entry.")]
    [SerializeField] private GameObject bossToActivate;
    [Tooltip("Or a boss prefab instantiated on entry.")]
    [SerializeField] private GameObject bossPrefab;
    [Tooltip("Where the prefab spawns. Defaults to this area's position.")]
    [SerializeField] private Transform bossSpawnPoint;

    [Header("Persistence")]
    [Tooltip("If the boss is already recorded defeated in BossTracker, never trigger.")]
    [SerializeField] private bool skipIfDefeated = true;

    private bool _consumed;

    private void Awake()
    {
        // Keep a pre-placed boss hidden until the player walks in.
        if (bossToActivate != null)
            bossToActivate.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_consumed) return;
        if (other.GetComponentInParent<PlayerMovement>() == null && !other.CompareTag("Player")) return;

        // Already beaten this run → don't re-spawn it.
        if (skipIfDefeated && !string.IsNullOrEmpty(bossId) &&
            BossTracker.Instance != null && BossTracker.Instance.IsBossDefeated(bossId))
        {
            _consumed = true;
            var c = GetComponent<Collider>();
            if (c != null) c.enabled = false;
            return;
        }

        _consumed = true;
        StartEncounter();
    }

    private void StartEncounter()
    {
        GameObject bossGO = ResolveBoss();
        if (bossGO == null)
        {
            Debug.LogWarning("[BossArea] No boss to start — assign 'Boss To Activate' or 'Boss Prefab'.", this);
            return;
        }

        var encounter = bossGO.GetComponent<IBossEncounter>();

        // Prefer the area's overrides; otherwise use the boss's own identity.
        string nameToShow = !string.IsNullOrEmpty(bossName) ? bossName
                          : (encounter != null ? encounter.BossName : bossGO.name);
        string idToShow   = !string.IsNullOrEmpty(bossId) ? bossId
                          : (encounter != null ? encounter.BossId : null);

        EventBus.Raise(new OnBossEncounterStartedEvent { bossName = nameToShow, bossId = idToShow });

        if (encounter != null) encounter.BeginEncounter();
        else Debug.LogWarning($"[BossArea] '{bossGO.name}' has no IBossEncounter — popup shown but nothing started.", this);

        // One-shot.
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    private GameObject ResolveBoss()
    {
        if (bossToActivate != null)
        {
            bossToActivate.SetActive(true);
            return bossToActivate;
        }
        if (bossPrefab != null)
        {
            Vector3    pos = bossSpawnPoint != null ? bossSpawnPoint.position : transform.position;
            Quaternion rot = bossSpawnPoint != null ? bossSpawnPoint.rotation : Quaternion.identity;
            return Instantiate(bossPrefab, pos, rot);
        }
        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.8f, 0.2f, 0.85f, 0.22f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        if (col is BoxCollider box)         Gizmos.DrawCube(box.center, box.size);
        else if (col is SphereCollider sph) Gizmos.DrawSphere(sph.center, sph.radius);
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
