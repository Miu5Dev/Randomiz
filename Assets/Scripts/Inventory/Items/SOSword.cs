using UnityEngine;

[CreateAssetMenu(fileName = "Weapon", menuName = "Items/Sword")]
public class SOSword : SOWeapon
{
    [Header("Hitbox Settings")]
    public Vector3   hitboxSize  = new Vector3(1f, 1f, 1f);
    public float     hitboxReach = 1f;
    public LayerMask targetLayers;

    [Header("Hitbox Origin (opcional)")]
    [Tooltip("Transform desde donde se proyecta la hitbox. " +
             "Si es null, usa el transform del usuario.")]
    public string hitboxOriginName = "";   // nombre del child transform, ej: "WeaponPivot"

    [Header("Animation")]
    public string attackTrigger = "Attack";

    private bool     _hasHitThisSwing = false;
    private bool     _swingOpen       = false;
    private Animator _cachedAnimator  = null;

    public override void Use(GameObject user)
    {
        if (_swingOpen)
        {
            TryHit(user);
            return;
        }

        _hasHitThisSwing = false;
        _swingOpen       = true;
        _cachedAnimator  = user.GetComponent<Animator>();

        if (_cachedAnimator != null && !string.IsNullOrEmpty(attackTrigger))
            _cachedAnimator.SetTrigger(attackTrigger);
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
        if (!string.IsNullOrEmpty(hitboxOriginName))
        {
            Transform t = user.transform.Find(hitboxOriginName);
            if (t != null) return t;
            Debug.LogWarning($"[SOSword] No se encontró '{hitboxOriginName}' en {user.name}. Usando raíz.");
        }
        return user.transform;
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