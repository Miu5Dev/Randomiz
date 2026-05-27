using UnityEngine;

[CreateAssetMenu(fileName = "Weapon", menuName = "Items/Sword")]
public class SOSword : SOWeapon
{
    [Header("Hitbox Settings")]
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

        foreach (Collider hit in hits)
        {
            if (hit.gameObject == user) continue;

            _hasHitThisSwing = true;
            EventBus.Raise(new OnDamageDealtEvent(user, hit.transform.root.gameObject, damage));
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