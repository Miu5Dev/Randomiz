using UnityEngine;

/// <summary>
/// Bridges Animator animation events to the equipped weapon's <see cref="MeleeHitbox"/>.
/// Put this on the same GameObject as the character's Animator. In the attack clip,
/// add events calling <see cref="AnimHitStart"/> at the moment the blade should
/// start dealing damage and <see cref="AnimHitEnd"/> when the swing passes.
///
/// Works for the player (reads the equipped SOWeapon for damage) and for any
/// character — the damage source can also be pre-armed by the caller.
/// </summary>
public class WeaponAnimationRelay : MonoBehaviour
{
    [Tooltip("Root attributed as the attacker. Defaults to this transform's root.")]
    [SerializeField] private GameObject attackerRoot;

    [Tooltip("Where to look for the active weapon's MeleeHitbox. Defaults to this hierarchy.")]
    [SerializeField] private Transform searchRoot;

    private void Awake()
    {
        if (attackerRoot == null) attackerRoot = transform.root.gameObject;
        if (searchRoot   == null) searchRoot   = transform.root;
    }

    /// <summary>Animation event: start the damage window of the active weapon.</summary>
    public void AnimHitStart()
    {
        MeleeHitbox hb = FindHitbox();
        if (hb == null) return;

        // Make sure the hitbox knows attacker + damage even if Use() didn't pre-arm it.
        if (EquipHandler.Instance != null && EquipHandler.Instance.EquipedItem is SOWeapon w)
            hb.Arm(attackerRoot, w.damage);

        hb.BeginHit();
    }

    /// <summary>Animation event: end the damage window of the active weapon.</summary>
    public void AnimHitEnd()
    {
        FindHitbox()?.EndHit();
    }

    private MeleeHitbox FindHitbox()
    {
        // Only active weapon model in the hand carries an enabled MeleeHitbox.
        return searchRoot != null
            ? searchRoot.GetComponentInChildren<MeleeHitbox>()
            : GetComponentInChildren<MeleeHitbox>();
    }
}
