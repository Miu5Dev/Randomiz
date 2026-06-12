using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zone that kills or damages any entity with a HealthSystem that overlaps it.
/// Uses Physics.OverlapBox in Update — no Rigidbody required on the target.
///
/// Tracks physical presence across frames: an entity that dies inside the zone is
/// not re-triggered on respawn until it physically leaves and re-enters.
/// </summary>
[RequireComponent(typeof(Collider))]
public class InstakillZone : MonoBehaviour
{
    public enum HazardMode { Instakill, DamageOverTime }

    [SerializeField] private HazardMode _mode = HazardMode.Instakill;

    [Tooltip("Only affect objects with this tag. Leave empty to affect any entity with a HealthSystem.")]
    [SerializeField] private string _targetTag = "Player";

    [Tooltip("Physics layers to check. Restrict to the player's layer for performance.")]
    [SerializeField] private LayerMask _detectionLayers = ~0;

    [Header("Damage Over Time")]
    [SerializeField] private float _damageInterval = 0.5f;
    [SerializeField] private float _damagePerTick = 2f;

    private Collider _col;
    private float _nextDamageTime;
    private readonly Collider[] _overlapBuffer = new Collider[16];

    // Entities physically overlapping the zone right now (alive or dead).
    // A dead entity stays here until it physically leaves, so respawn inside
    // the zone does NOT count as a new entry.
    private readonly HashSet<HealthSystem> _insideZone = new();
    private readonly HashSet<HealthSystem> _currentFrame = new();

    private void Awake() => _col = GetComponent<Collider>();

    private void Update()
    {
        int count = Physics.OverlapBoxNonAlloc(
            _col.bounds.center, _col.bounds.extents, _overlapBuffer,
            Quaternion.identity, _detectionLayers, QueryTriggerInteraction.Ignore);

        // Build current-frame set regardless of alive status.
        _currentFrame.Clear();
        for (int i = 0; i < count; i++)
        {
            var hit = _overlapBuffer[i];
            if (hit == _col || !Matches(hit)) continue;
            var health = hit.GetComponentInParent<HealthSystem>();
            if (health != null) _currentFrame.Add(health);
        }

        bool tickNow = _mode == HazardMode.DamageOverTime && Time.time >= _nextDamageTime;
        bool didTick = false;

        foreach (var health in _currentFrame)
        {
            if (!health.IsAlive) continue;

            bool isNewEntry = !_insideZone.Contains(health);

            if (_mode == HazardMode.Instakill)
            {
                if (isNewEntry)
                    health.Damage(health.CurrentHealth + 1f, gameObject);
            }
            else // DamageOverTime
            {
                if (isNewEntry || tickNow)
                {
                    health.Damage(_damagePerTick, gameObject);
                    didTick = true;
                }
            }
        }

        if (didTick) _nextDamageTime = Time.time + _damageInterval;

        // Rebuild presence set from current physical overlaps (keep dead entities
        // so they are not re-triggered when health is restored before teleport).
        _insideZone.Clear();
        foreach (var h in _currentFrame) _insideZone.Add(h);
    }

    private bool Matches(Collider other)
        => string.IsNullOrEmpty(_targetTag) || other.CompareTag(_targetTag);
}
