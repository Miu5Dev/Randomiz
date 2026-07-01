using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Rigidbody-based projectile launched by <see cref="SlingshotBehaviour"/>.
///
/// On spawn (<see cref="Launch"/>):
///   • Forces the Rigidbody into a launch-ready state (non-kinematic, gravity on,
///     continuous collision) so the stone always flies regardless of how the
///     prefab's Rigidbody was authored — this fixes "stone spawns but never moves".
///   • Ignores collisions with the firing player so it doesn't stall against the
///     player's own capsule on spawn.
///
/// On impact (collision OR trigger):
///   • Raises OnDamageDealtEvent against the first non-owner root hit.
///   • Freezes and self-destructs after a short delay.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SlingshotStone : MonoBehaviour
{
    [Tooltip("Seconds after collision before the stone is destroyed. " +
             "Allows particle effects / audio to trigger on impact.")]
    [SerializeField] private float destroyDelay = 0.1f;

    [Tooltip("Maximum lifetime in seconds. Prevents stones lingering forever if they miss.")]
    [SerializeField] private float maxLifetime = 8f;

    [Header("Trail")]
    [SerializeField] private float trailTime       = 0.22f;
    [SerializeField] private float trailStartWidth = 0.07f;
    [SerializeField] private float trailEndWidth   = 0f;
    [SerializeField] private Color trailStartColor = new(1f, 0.92f, 0.7f, 0.85f);
    [SerializeField] private Color trailEndColor   = new(0.9f, 0.6f, 0.2f, 0f);
    [Tooltip("Assign a particle/unlit material. If null a basic Sprites/Default material is used.")]
    [SerializeField] private Material trailMaterial;

    private GameObject _owner;
    private float      _damage;
    private bool       _hasHit;
    private Rigidbody  _rb;
    private TrailRenderer _trail;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        SetupTrail();
    }

    private void SetupTrail()
    {
        // Pick up an existing TrailRenderer (designed in the prefab) or add one with defaults.
        _trail = GetComponent<TrailRenderer>();
        if (_trail == null)
            _trail = gameObject.AddComponent<TrailRenderer>();

        _trail.time            = trailTime;
        _trail.startWidth      = trailStartWidth;
        _trail.endWidth        = trailEndWidth;
        _trail.minVertexDistance = 0.02f;
        _trail.numCornerVertices = 4;
        _trail.numCapVertices    = 2;
        _trail.shadowCastingMode = ShadowCastingMode.Off;
        _trail.receiveShadows    = false;
        _trail.generateLightingData = false;

        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new(trailStartColor, 0f), new(trailEndColor, 1f) },
            new GradientAlphaKey[] { new(trailStartColor.a, 0f), new(trailEndColor.a, 1f) }
        );
        _trail.colorGradient = gradient;

        if (trailMaterial != null)
        {
            _trail.material = trailMaterial;
        }
        else
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Sprites/Default")
                      ?? Shader.Find("Unlit/Color");
            if (shader != null) _trail.material = new Material(shader);
        }
    }

    /// <summary>
    /// Initialise and launch the stone with the given velocity.
    /// Must be called immediately after Instantiate.
    /// </summary>
    public void Launch(GameObject owner, Vector3 direction, float speed, float damage)
    {
        _owner  = owner;
        _damage = damage;
        _hasHit = false;

        if (_rb == null) _rb = GetComponent<Rigidbody>();

        // Force a launch-ready Rigidbody. A kinematic body (or one with gravity off)
        // is the usual reason a spawned stone just sits still — normalise it here so
        // the prefab setup can't break the shot.
        if (_rb != null)
        {
            _rb.isKinematic            = false;
            _rb.useGravity             = true;
            _rb.interpolation          = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.linearVelocity         = direction.normalized * speed;
            _rb.angularVelocity        = Vector3.zero;
        }

        // Guarantee there's a collider to register impacts; without one the stone
        // would fly through everything and never deal damage.
        if (GetComponentInChildren<Collider>() == null)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.radius = 0.1f;
        }

        IgnoreOwnerCollisions();

        // Auto-destroy after maxLifetime if no collision occurs.
        Destroy(gameObject, maxLifetime);
    }

    // Prevents the stone from colliding with the player who fired it (it spawns at
    // the hand, inside the player's capsule, so without this it stalls immediately).
    private void IgnoreOwnerCollisions()
    {
        if (_owner == null) return;
        var myCols    = GetComponentsInChildren<Collider>();
        var ownerCols = _owner.transform.root.GetComponentsInChildren<Collider>();
        foreach (var a in myCols)
            foreach (var b in ownerCols)
                if (a != null && b != null)
                    Physics.IgnoreCollision(a, b, true);
    }

    private void OnCollisionEnter(Collision collision) => TryHit(collision.transform);
    private void OnTriggerEnter(Collider other)        => TryHit(other.transform);

    private void TryHit(Transform other)
    {
        if (_hasHit) return;

        // Resolve the victim by HealthSystem, not transform.root — a target nested under a
        // shared scene object (e.g. the Terrain) would otherwise resolve to that parent.
        HealthSystem victim      = other.GetComponentInParent<HealthSystem>();
        HealthSystem ownerHealth = _owner != null ? _owner.GetComponentInParent<HealthSystem>() : null;

        // Ignore the player who fired the stone.
        if (victim != null && victim == ownerHealth) return;

        _hasHit = true;

        // Stop trail emitting so it doesn't stretch while the stone is frozen.
        if (_trail != null) _trail.emitting = false;

        // Deal damage via the EventBus so HealthSystem and other listeners react.
        if (victim != null)
            EventBus.Raise(new OnDamageDealtEvent(_owner, victim.gameObject, _damage));

        // Freeze the stone in place on impact (visually lands on the surface).
        if (_rb != null)
        {
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            // Reset to Discrete before going kinematic — ContinuousDynamic on a
            // kinematic body logs a warning.
            _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _rb.isKinematic     = true;
        }

        Destroy(gameObject, destroyDelay);
    }
}
