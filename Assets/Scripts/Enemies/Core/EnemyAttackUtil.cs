using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared damage helpers for attack patterns that hit an area directly (Stomp,
/// Charge contact, etc.) rather than going through a weapon's own hitbox.
/// Reuses buffers to stay allocation-free and dedups by collider root so an
/// entity with multiple colliders only takes one hit.
/// </summary>
public static class EnemyAttackUtil
{
    private static readonly Collider[]        _buffer = new Collider[32];
    private static readonly HashSet<GameObject> _hitRoots = new();

    /// <summary>OverlapSphere damage. Raises <see cref="OnDamageDealtEvent"/> once per target root.</summary>
    public static void SphereDamage(GameObject attacker, Vector3 center, float radius,
                                    LayerMask mask, float damage)
    {
        int n = Physics.OverlapSphereNonAlloc(center, radius, _buffer, mask, QueryTriggerInteraction.Ignore);
        DispatchHits(attacker, n, damage);
    }

    /// <summary>OverlapBox damage. Raises <see cref="OnDamageDealtEvent"/> once per target root.</summary>
    public static void BoxDamage(GameObject attacker, Vector3 center, Vector3 halfExtents,
                                 Quaternion rotation, LayerMask mask, float damage)
    {
        int n = Physics.OverlapBoxNonAlloc(center, halfExtents, _buffer, rotation, mask, QueryTriggerInteraction.Ignore);
        DispatchHits(attacker, n, damage);
    }

    private static void DispatchHits(GameObject attacker, int count, float damage)
    {
        _hitRoots.Clear();
        // Resolve victims by their HealthSystem (not transform.root): scene-placed entities
        // are often nested under a shared parent, which a .root lookup would resolve to.
        HealthSystem attackerHealth = attacker != null ? attacker.GetComponentInParent<HealthSystem>() : null;
        for (int i = 0; i < count; i++)
        {
            HealthSystem victim = _buffer[i].GetComponentInParent<HealthSystem>();
            if (victim == null) continue;
            if (victim == attackerHealth) continue;             // don't damage the attacker
            if (!_hitRoots.Add(victim.gameObject)) continue;    // already damaged this swing
            EventBus.Raise(new OnDamageDealtEvent(attacker, victim.gameObject, damage));
        }
    }
}
