using UnityEngine;

/// <summary>Sword weapon definition; supports an instant OverlapBox hit or a moving-hitbox window (driven by animation events or a timer) with configurable hitbox size, reach and target layers.</summary>
[CreateAssetMenu(fileName = "Weapon", menuName = "Items/Sword")]
public class SOSword : SOWeapon
{
    public enum DamageMode
    {
        OverlapBox,    // classic: a single OverlapBox at the swing frame (current behaviour)
        MovingHitbox,  // a MeleeHitbox collider on the weapon that follows the animation
    }

    [Header("Damage Mode")]
    [Tooltip("OverlapBox = instant box at the swing frame. MovingHitbox = a collider on the weapon " +
             "that deals damage on contact while it follows the swing animation.")]
    public DamageMode damageMode = DamageMode.OverlapBox;

    [Header("Moving Hitbox (when DamageMode = MovingHitbox)")]
    [Tooltip("If true, animation events (WeaponAnimationRelay) open/close the window. " +
             "If false (or no Animator), a timed window is used instead.")]
    public bool  hitboxUseAnimationEvents = true;
    [Tooltip("Timed mode: delay before the window opens (seconds).")]
    public float hitboxActiveDelay = 0.05f;
    [Tooltip("Timed mode: how long the window stays open (seconds).")]
    public float hitboxActiveDuration = 0.25f;

    [Header("Hitbox Settings (OverlapBox mode)")]
    public Vector3   hitboxSize  = new Vector3(1f, 1f, 1f);
    public float     hitboxReach = 1f;
    public LayerMask targetLayers;

    [Header("Hitbox Origin (optional)")]
    [Tooltip("Child transform name to project the hitbox from. If empty, uses the user's root transform.")]
    public string hitboxOriginName = "";   // child transform name, e.g. "WeaponPivot"

    [Header("Animation")]
    public string attackTrigger = "Attack";

    private bool     _hasHitThisSwing = false;
    private bool     _swingOpen       = false;
    private Animator _cachedAnimator  = null;
    private Transform _cachedOrigin   = null;
    private int       _cachedAttackHash = 0; // Animator parameter hash (faster than the string variant)

    public override void Use(GameObject user)
    {
        if (damageMode == DamageMode.MovingHitbox)
        {
            UseMovingHitbox(user);
            return;
        }

        if (_swingOpen)
        {
            TryHit(user);
            return;
        }

        _hasHitThisSwing = false;
        _swingOpen       = true;
        // Cache once per ScriptableObject lifetime — avoids GetComponent per swing.
        if (_cachedAnimator == null) _cachedAnimator = user.GetComponent<Animator>();

        if (_cachedAnimator != null && !string.IsNullOrEmpty(attackTrigger))
        {
            // Hash once — SetTrigger(int) is faster than the string overload.
            if (_cachedAttackHash == 0) _cachedAttackHash = Animator.StringToHash(attackTrigger);
            _cachedAnimator.SetTrigger(_cachedAttackHash);
        }
        else
        {
            TryHit(user);
            _swingOpen = false;
        }
    }

    /// <summary>
    /// MovingHitbox path: plays the swing animation and lets the weapon's
    /// <see cref="MeleeHitbox"/> deal damage on contact while it follows the blade.
    /// If animation events are configured, the relay opens/closes the window;
    /// otherwise a timed window is used.
    /// </summary>
    private void UseMovingHitbox(GameObject user)
    {
        MeleeHitbox hitbox = user.GetComponentInChildren<MeleeHitbox>();
        if (hitbox == null)
        {
            Debug.LogWarning($"[SOSword] '{itemName}' is MovingHitbox mode but no MeleeHitbox " +
                             $"was found under {user.name}. Add one to the weapon model.");
            return;
        }

        if (_cachedAnimator == null) _cachedAnimator = user.GetComponent<Animator>();
        bool hasAnimator = _cachedAnimator != null && !string.IsNullOrEmpty(attackTrigger);

        if (hasAnimator)
        {
            if (_cachedAttackHash == 0) _cachedAttackHash = Animator.StringToHash(attackTrigger);
            _cachedAnimator.SetTrigger(_cachedAttackHash);
        }

        if (hitboxUseAnimationEvents && hasAnimator)
        {
            // Pre-arm; WeaponAnimationRelay's events will Begin/End the window.
            hitbox.Arm(user, damage);
        }
        else
        {
            // No animator (or events disabled): drive a timed window directly.
            hitbox.OpenTimed(user, damage, hitboxActiveDelay, hitboxActiveDuration);
        }
    }

    public void OnSwingHitFrame(GameObject user) => TryHit(user);
    public void OnSwingEnd() => _swingOpen = false;

    private Transform GetOrigin(GameObject user)
    {
        // Cache the lookup — Transform.Find is a string-keyed scan over children.
        // Assumes a single user per SO instance (typical for the player's sword).
        if (_cachedOrigin != null) return _cachedOrigin;

        if (!string.IsNullOrEmpty(hitboxOriginName))
        {
            Transform t = user.transform.Find(hitboxOriginName);
            if (t != null) { _cachedOrigin = t; return t; }
            Debug.LogWarning($"[SOSword] '{hitboxOriginName}' not found under {user.name}. Falling back to root.");
        }
        _cachedOrigin = user.transform;
        return _cachedOrigin;
    }

    private void TryHit(GameObject user)
    {
        if (_hasHitThisSwing) return;

        Transform  origin   = GetOrigin(user);
        Vector3    center   = origin.position + origin.forward * hitboxReach;
        Quaternion rotation = origin.rotation;   // rota con el transform correcto

        Collider[] hits = Physics.OverlapBox(center, hitboxSize * 0.5f, rotation, targetLayers);

        // Resolve the victim by walking up to its HealthSystem, NOT transform.root.
        // Scene-placed enemies are frequently parented under a shared object (e.g. the
        // Terrain), so .root would resolve to that parent instead of the enemy — the
        // damage event would target the wrong GameObject and the hit would silently miss.
        HealthSystem attackerHealth = user.GetComponentInParent<HealthSystem>();
        foreach (Collider hit in hits)
        {
            HealthSystem victim = hit.GetComponentInParent<HealthSystem>();
            if (victim == null) continue;             // not a damageable entity (wall, prop…)
            if (victim == attackerHealth) continue;   // never hit ourselves

            _hasHitThisSwing = true;
            EventBus.Raise(new OnDamageDealtEvent(user, victim.gameObject, damage));
            break;
        }
    }

#if UNITY_EDITOR
    public void DrawGizmos(Transform user)
    {
        Transform origin = string.IsNullOrEmpty(hitboxOriginName)
            ? user
            : user.Find(hitboxOriginName) ?? user;

        Vector3 center = origin.position + origin.forward * hitboxReach;

        Gizmos.matrix = Matrix4x4.TRS(center, origin.rotation, Vector3.one);
        Gizmos.color  = _swingOpen
            ? new Color(1f, 0f, 0f, 0.5f)
            : new Color(0.5f, 0.5f, 0.5f, 0.3f);
        Gizmos.DrawWireCube(Vector3.zero, hitboxSize);
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}