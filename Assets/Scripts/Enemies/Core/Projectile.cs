using UnityEngine;

/// <summary>
/// Lightweight projectile spawned by <see cref="SOAttack_Projectile"/>. Flies in
/// a straight line, deals damage to the first valid hit and self-destructs on
/// impact or after its lifetime. No Rigidbody needed.
/// </summary>
public class Projectile : MonoBehaviour
{
    private GameObject _owner;
    private Vector3    _dir;
    private float      _speed;
    private float      _damage;
    private LayerMask  _hitMask;
    private float      _life;
    private float      _radius;

    public void Launch(GameObject owner, Vector3 dir, float speed, float damage,
                       LayerMask hitMask, float life, float radius)
    {
        _owner   = owner;
        _dir     = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        _speed   = speed;
        _damage  = damage;
        _hitMask = hitMask;
        _life    = life;
        _radius  = Mathf.Max(0.01f, radius);
        transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
    }

    private void Update()
    {
        float dt   = Time.deltaTime;
        float step = _speed * dt;

        if (Physics.SphereCast(transform.position, _radius, _dir, out RaycastHit hit,
                               step, _hitMask, QueryTriggerInteraction.Ignore))
        {
            GameObject root = hit.transform.root.gameObject;
            if (root != _owner)
            {
                EventBus.Raise(new OnDamageDealtEvent(_owner, root, _damage));
                Destroy(gameObject);
                return;
            }
        }

        transform.position += _dir * step;

        _life -= dt;
        if (_life <= 0f) Destroy(gameObject);
    }
}
