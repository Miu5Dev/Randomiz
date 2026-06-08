using UnityEngine;

/// <summary>
/// Rigidbody-based projectile launched by <see cref="SlingshotBehaviour"/>.
///
/// On spawn:
///   • Receives an initial velocity via <see cref="Launch"/> — gravity is handled
///     by the Rigidbody so the stone follows a natural arc.
///
/// On impact:
///   • Raises OnDamageDealtEvent against any object that has a root with a
///     collider on the first non-owner hit.
///   • Self-destructs after a short delay (0.1 s) to allow hit effects to play.
///
/// Requires: Rigidbody (gravity enabled), at least one Collider.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SlingshotStone : MonoBehaviour
{
    [Tooltip("Seconds after collision before the stone is destroyed. " +
             "Allows particle effects / audio to trigger on impact.")]
    [SerializeField] private float destroyDelay = 0.1f;

    [Tooltip("Maximum lifetime in seconds. Prevents stones lingering forever if they miss.")]
    [SerializeField] private float maxLifetime = 8f;

    private GameObject _owner;
    private float      _damage;
    private bool       _hasHit;
    private Rigidbody  _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Initialise and launch the stone with the given velocity.
    /// Must be called immediately after Instantiate.
    /// </summary>
    public void Launch(GameObject owner, Vector3 direction, float speed, float damage)
    {
        _owner  = owner;
        _damage = damage;

        if (_rb != null)
            _rb.linearVelocity = direction.normalized * speed;

        // Auto-destroy after maxLifetime if no collision occurs.
        Destroy(gameObject, maxLifetime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_hasHit) return;

        GameObject hit = collision.transform.root.gameObject;

        // Ignore the player who fired the stone.
        if (_owner != null && hit == _owner.transform.root.gameObject) return;

        _hasHit = true;

        // Deal damage via the EventBus so HealthSystem and other listeners react.
        EventBus.Raise(new OnDamageDealtEvent(_owner, hit, _damage));

        // Freeze the stone in place on impact (visually lands on the surface).
        if (_rb != null)
        {
            _rb.linearVelocity        = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic     = true;
        }

        Destroy(gameObject, destroyDelay);
    }
}
