using UnityEngine;

/// <summary>
/// Weapon-grip hand IK: pins a hand (typically the off-hand) to a grip target on
/// the equipped weapon, so two-handed grips stay glued to the handle. Yields while
/// wallhugging / ledge-hanging (WallhugIK / LedgeHangIK own the hands then).
///
/// Requires the Animator layer's "IK Pass" to be ON.
/// </summary>
public class HandIK : MonoBehaviour, IIKModule
{
    [Header("Weapon grip")]
    public AvatarIKGoal gripHand = AvatarIKGoal.LeftHand;
    [Range(0f, 1f)] public float gripPositionWeight = 1f;
    [Range(0f, 1f)] public float gripRotationWeight = 1f;
    public string gripChildName = "OffHandGrip";
    public Transform gripOverride;

    [Header("Smoothing")]
    public float weightSmooth = 12f;

    public int Priority => 10;

    private Animator _anim;
    private PlayerMovement _pm;
    private Transform _grip;
    private float _gripW;

    public void Init(PlayerIK owner)
    {
        _anim = owner.Animator;
        _pm = owner.Player;
        EventBus.Subscribe<OnItemEquipEvent>(OnEquip);
        EventBus.Subscribe<OnItemUnequipEvent>(OnUnequip);
    }

    private void OnDestroy()
    {
        EventBus.Unsubscribe<OnItemEquipEvent>(OnEquip);
        EventBus.Unsubscribe<OnItemUnequipEvent>(OnUnequip);
    }

    private void OnEquip(OnItemEquipEvent e)
    {
        if (gripOverride != null) { _grip = gripOverride; return; }
        if (EquipHandler.Instance != null && EquipHandler.Instance.ItemsPivotPoint != null)
            _grip = FindDeep(EquipHandler.Instance.ItemsPivotPoint, gripChildName);
    }

    private void OnUnequip(OnItemUnequipEvent e) => _grip = null;

    public void ApplyIK(Animator animator)
    {
        if (animator == null) return;

        // Yield while wallhug / ledge own the hands.
        bool busy = _pm != null && (_pm.IsWallhugging || _pm.IsLedgeGrabbing || _pm.IsClimbingLedge);
        float target = (_grip != null && !busy) ? 1f : 0f;
        _gripW = Mathf.MoveTowards(_gripW, target, weightSmooth * Time.deltaTime);

        if (_gripW <= 0.001f)
        {
            animator.SetIKPositionWeight(gripHand, 0f);
            animator.SetIKRotationWeight(gripHand, 0f);
            return;
        }

        animator.SetIKPositionWeight(gripHand, _gripW * gripPositionWeight);
        animator.SetIKRotationWeight(gripHand, _gripW * gripRotationWeight);
        if (_grip != null)
        {
            animator.SetIKPosition(gripHand, _grip.position);
            animator.SetIKRotation(gripHand, _grip.rotation);
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindDeep(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}
