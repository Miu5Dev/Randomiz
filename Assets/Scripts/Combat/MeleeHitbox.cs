using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Animation-driven melee hitbox that lives on a weapon (or item) GameObject.
/// While "open" it scans its own collider's shape every FixedUpdate, so the
/// damage volume follows the weapon as the swing animation moves it — if the
/// animation visually connects, the hit lands. Each target root is damaged once
/// per swing window.
///
/// No Rigidbody required and no single-step tunneling: instead of relying on
/// trigger callbacks it actively overlaps the collider's volume each physics tick.
///
/// Driven either by:
///   • animation events through <see cref="WeaponAnimationRelay"/> (Arm → BeginHit/EndHit), or
///   • a code-timed window via <see cref="OpenTimed"/>.
/// </summary>
[RequireComponent(typeof(Collider))]
public class MeleeHitbox : MonoBehaviour
{
    [Tooltip("Layers this hitbox can damage (include the target's layer, exclude the wielder's).")]
    public LayerMask targetLayers = ~0;

    [Tooltip("Collider that defines the damage volume. Auto-found if left empty. Should be a trigger.")]
    [SerializeField] private Collider hitCollider;

    private enum Mode { Idle, Pending, Active }
    private Mode       _mode = Mode.Idle;
    private float      _timer;
    private float      _activeDuration;   // < 0 = stay open until EndHit/Close
    private GameObject _attacker;
    private float      _damage;

    private readonly HashSet<GameObject> _hitThisWindow = new();
    private static readonly Collider[]   _buffer = new Collider[32];

    private void Reset()  => hitCollider = GetComponent<Collider>();
    private void Awake()
    {
        if (hitCollider == null) hitCollider = GetComponent<Collider>();
        if (hitCollider != null) hitCollider.isTrigger = true;   // we never want physics pushes from it
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Store who is attacking and for how much (for animation-event mode).</summary>
    public void Arm(GameObject attacker, float damage)
    {
        _attacker = attacker;
        _damage   = damage;
    }

    /// <summary>Begin the damage window using the armed attacker/damage (animation event).</summary>
    public void BeginHit()
    {
        _hitThisWindow.Clear();
        _activeDuration = -1f;
        _mode = Mode.Active;
    }

    /// <summary>End the damage window (animation event).</summary>
    public void EndHit() => _mode = Mode.Idle;

    /// <summary>Open immediately for a fixed duration (code-driven, no animation events).</summary>
    public void OpenTimed(GameObject attacker, float damage, float delay, float duration)
    {
        Arm(attacker, damage);
        _hitThisWindow.Clear();
        _activeDuration = Mathf.Max(0.01f, duration);

        if (delay > 0f) { _mode = Mode.Pending; _timer = delay; }
        else            { _mode = Mode.Active;  _timer = _activeDuration; }
    }

    public void Close() => _mode = Mode.Idle;
    public bool IsOpen  => _mode == Mode.Active;

    // ─── Scan loop ───────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (_mode == Mode.Pending)
        {
            _timer -= Time.fixedDeltaTime;
            if (_timer <= 0f) { _mode = Mode.Active; _timer = _activeDuration; }
            return;
        }

        if (_mode != Mode.Active) return;

        Scan();

        if (_activeDuration > 0f)
        {
            _timer -= Time.fixedDeltaTime;
            if (_timer <= 0f) _mode = Mode.Idle;
        }
    }

    private void Scan()
    {
        if (hitCollider == null) return;

        int n = OverlapSelf();
        for (int i = 0; i < n; i++)
        {
            GameObject root = _buffer[i].transform.root.gameObject;
            if (root == _attacker) continue;
            if (!_hitThisWindow.Add(root)) continue;       // already hit this swing
            EventBus.Raise(new OnDamageDealtEvent(_attacker, root, _damage));
        }
    }

    /// <summary>Overlaps the world-space volume of this hitbox's collider.</summary>
    private int OverlapSelf()
    {
        Transform t = hitCollider.transform;

        switch (hitCollider)
        {
            case BoxCollider box:
            {
                Vector3 center = t.TransformPoint(box.center);
                Vector3 half   = Vector3.Scale(box.size * 0.5f, AbsScale(t.lossyScale));
                return Physics.OverlapBoxNonAlloc(center, half, _buffer, t.rotation, targetLayers, QueryTriggerInteraction.Ignore);
            }
            case SphereCollider sph:
            {
                Vector3 center = t.TransformPoint(sph.center);
                float   r      = sph.radius * MaxAxis(AbsScale(t.lossyScale));
                return Physics.OverlapSphereNonAlloc(center, r, _buffer, targetLayers, QueryTriggerInteraction.Ignore);
            }
            case CapsuleCollider cap:
            {
                GetCapsuleWorldPoints(cap, out Vector3 p0, out Vector3 p1, out float r);
                return Physics.OverlapCapsuleNonAlloc(p0, p1, r, _buffer, targetLayers, QueryTriggerInteraction.Ignore);
            }
            default:
            {
                Bounds b = hitCollider.bounds;   // AABB fallback
                return Physics.OverlapBoxNonAlloc(b.center, b.extents, _buffer, Quaternion.identity, targetLayers, QueryTriggerInteraction.Ignore);
            }
        }
    }

    private static Vector3 AbsScale(Vector3 s) => new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    private static float   MaxAxis(Vector3 s)  => Mathf.Max(s.x, Mathf.Max(s.y, s.z));

    private static void GetCapsuleWorldPoints(CapsuleCollider cap, out Vector3 p0, out Vector3 p1, out float radius)
    {
        Transform t = cap.transform;
        Vector3   scale = AbsScale(t.lossyScale);

        Vector3 axis = cap.direction == 0 ? t.right : cap.direction == 1 ? t.up : t.forward;
        float   radScale = cap.direction == 0 ? Mathf.Max(scale.y, scale.z)
                         : cap.direction == 1 ? Mathf.Max(scale.x, scale.z)
                         :                      Mathf.Max(scale.x, scale.y);
        float   axisScale = cap.direction == 0 ? scale.x : cap.direction == 1 ? scale.y : scale.z;

        radius = cap.radius * radScale;
        float half = Mathf.Max(0f, (cap.height * 0.5f * axisScale) - radius);

        Vector3 center = t.TransformPoint(cap.center);
        p0 = center + axis * half;
        p1 = center - axis * half;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var col = hitCollider != null ? hitCollider : GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = Application.isPlaying && IsOpen
            ? new Color(1f, 0.1f, 0.1f, 0.5f)
            : new Color(0.4f, 0.7f, 1f, 0.4f);
        Gizmos.matrix = Matrix4x4.TRS(col.transform.position, col.transform.rotation, col.transform.lossyScale);

        if (col is BoxCollider b)       Gizmos.DrawWireCube(b.center, b.size);
        else if (col is SphereCollider s) Gizmos.DrawWireSphere(s.center, s.radius);
        else                            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
