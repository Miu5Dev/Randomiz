using UnityEngine;

/// <summary>
/// A ring-shaped collider that expands radially outward from a fist's impact point.
/// Spawned only in phase 2 when the fist hits the ground.
///
/// Setup: attach to a GameObject with a CapsuleCollider (radius 0, height 1) or a
/// custom trigger collider representing the ring cross-section. The script scales
/// the transform's XZ uniformly to simulate expansion.
/// </summary>
public class BossShockwave : MonoBehaviour
{
    [Header("Expansion")]
    [Tooltip("Outward expansion speed (units/s).")]
    [SerializeField] private float shockwaveSpeed = 8f;
    [Tooltip("Total lifetime before the object destroys itself (seconds).")]
    [SerializeField] private float duration = 1.2f;
    [Tooltip("Maximum radius the ring reaches before it dies.")]
    [SerializeField] private float maxRadius = 10f;

    [Header("Damage")]
    [Tooltip("Damage dealt to the player on contact.")]
    [SerializeField] private float damage = 10f;
    [Tooltip("Upward force passed to PlayerMovement.ApplyKnockback.")]
    [SerializeField] private float knockbackForce = 8f;
    [Tooltip("Seconds the player is hit-stunned after the knockback.")]
    [SerializeField] private float knockbackDuration = 0.35f;

    // ── Runtime ──────────────────────────────────────────────────────────────

    private float   _currentRadius;
    private float   _elapsed;
    private bool    _playerHit; // prevent hitting the player more than once per shockwave

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // PhysicsController has no Rigidbody — kinematic Rigidbody on this volume
        // is required for OnTriggerEnter to fire against the player's static collider.
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;

        // Expand the ring.
        _currentRadius += shockwaveSpeed * Time.deltaTime;
        float scale = _currentRadius > 0f ? _currentRadius : 0.01f;
        transform.localScale = new Vector3(scale, transform.localScale.y, scale);

        // Destroy when radius limit or time limit is reached.
        if (_elapsed >= duration || _currentRadius >= maxRadius)
            Destroy(gameObject);
    }

    // ── Trigger ───────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_playerHit) return;
        if (PlayerMovement.Instance == null) return;
        if (other.gameObject != PlayerMovement.Instance.gameObject) return;

        _playerHit = true;

        // Deal damage through the event bus so HealthSystem processes it normally.
        EventBus.Raise(new OnDamageDealtEvent(
            attacker: gameObject,
            target:   other.gameObject,
            damage:   damage));

        // Apply upward knockback through the custom physics system.
        PlayerMovement.Instance?.ApplyKnockback(transform.position, 0f, knockbackForce, knockbackDuration);
    }
}
