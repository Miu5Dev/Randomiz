using UnityEngine;

/// <summary>
/// Hand IK with two modes:
///   • Weapon grip - pins a hand to a grip target on the equipped weapon.
///   • Ledge hang  - while ledge-grabbing, pins BOTH hands onto the ledge edge.
///
/// Ledge mode takes priority while PlayerLedgeGrab is active. Requires the Animator
/// layer's "IK Pass" to be ON.
///
/// Enable Show Gizmos to see exactly where the hand targets are placed (yellow =
/// left, cyan = right) so the offsets can be tuned visually.
/// </summary>
public class HandIK : MonoBehaviour, IIKModule
{
    [Header("Weapon grip (off-hand)")]
    public AvatarIKGoal gripHand = AvatarIKGoal.LeftHand;
    [Range(0f, 1f)] public float gripPositionWeight = 1f;
    [Range(0f, 1f)] public float gripRotationWeight = 1f;
    public string gripChildName = "OffHandGrip";
    public Transform gripOverride;

    [Header("Ledge hang")]
    public bool ledgeHands = true;
    [Tooltip("Half the spacing between the two hands, along the ledge edge.")]
    public float handSpacing = 0.22f;
    [Tooltip("Offset from the edge toward the wall (+) or away (-).")]
    public float gripDepth = 0.0f;
    [Tooltip("Vertical offset of the grip relative to the ledge top point.")]
    public float gripHeight = 0.0f;
    [Tooltip("Rotate the hands so palms face the wall.")]
    public bool orientHands = true;
    [Tooltip("Max arm reach (shoulder to hand). Targets beyond this are pulled in so " +
             "the whole arm extends instead of only the wrist rotating.")]
    public float armReach = 0.6f;

    [Header("Smoothing")]
    public float weightSmooth = 12f;

    [Header("Debug")]
    public bool showGizmos = true;

    private Animator _anim;
    private PlayerMovement _pm;
    private PlayerLedgeGrab _ledge;
    private Transform _grip;
    private float _gripW;
    private float _ledgeW;

    // Debug
    private Vector3 _dbgLeft, _dbgRight;
    private bool _dbgActive;

    public int Priority => 10;          // hands override foot IK on the hand goals

    public void Init(PlayerIK owner)
    {
        _anim = owner.Animator;
        _pm = owner.Player;
        _ledge = owner.Ledge;
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

        // Keep the hands on the ledge both while hanging AND during the climb-up
        // (the climb pulls the body up; hands should stay on the edge until the end).
        bool hanging = ledgeHands && _ledge != null && _pm != null &&
                       (_pm.IsLedgeGrabbing || _pm.IsClimbingLedge);

        float ledgeTarget = hanging ? 1f : 0f;
        _ledgeW = Mathf.MoveTowards(_ledgeW, ledgeTarget, weightSmooth * Time.deltaTime);

        if (hanging && _ledgeW > 0.001f)
        {
            Vector3 normal = _ledge.LedgeWallNormal;             // points away from wall
            Vector3 toWall = -normal;
            Vector3 along  = Vector3.Cross(Vector3.up, normal).normalized;
            if (Vector3.Dot(along, transform.right) < 0f) along = -along;

            Vector3 ledge = _ledge.LedgeTopPoint;   // the real edge (reachable, by the chest)

            // Target ON the actual ledge: edge height, body's lateral slide position
            // (follows shimmy), pushed onto the wall, spread by handSpacing.
            Vector3 lateralPos = transform.position;            // tracks shimmy
            Vector3 center = new Vector3(lateralPos.x, ledge.y + gripHeight, lateralPos.z) + toWall * gripDepth;
            Vector3 lTarget = center - along * handSpacing;
            Vector3 rTarget = center + along * handSpacing;

            // Clamp each target to within the arm's reach of its shoulder, so the IK
            // ALWAYS extends the full arm toward the edge instead of only rotating
            // the wrist when the goal would be slightly out of reach.
            Transform lSh = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform rSh = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _dbgLeft  = ClampToReach(lSh, lTarget);
            _dbgRight = ClampToReach(rSh, rTarget);
            _dbgActive = true;

            Quaternion handRot = Quaternion.LookRotation(toWall, Vector3.up);

            SetHand(animator, AvatarIKGoal.LeftHand,  _dbgLeft,  handRot, _ledgeW, orientHands);
            SetHand(animator, AvatarIKGoal.RightHand, _dbgRight, handRot, _ledgeW, orientHands);
            return; // ledge overrides weapon grip
        }
        _dbgActive = false;

        // Clear any leftover ledge weights when not hanging.
        if (_ledgeW <= 0.001f)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            // RightHand cleared below only if it's not the grip hand.
            if (gripHand != AvatarIKGoal.RightHand)
            {
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            }
        }

        // ── Weapon grip (off-hand) ─────────────────────────────────────────
        // Yield the grip while wallhugging - WallhugIK owns the hands then.
        bool wallhug = _pm != null && _pm.IsWallhugging;
        float gripTarget = (_grip != null && !wallhug) ? 1f : 0f;
        _gripW = Mathf.MoveTowards(_gripW, gripTarget, weightSmooth * Time.deltaTime);

        animator.SetIKPositionWeight(gripHand, _gripW * gripPositionWeight);
        animator.SetIKRotationWeight(gripHand, _gripW * gripRotationWeight);
        if (_grip != null)
        {
            animator.SetIKPosition(gripHand, _grip.position);
            animator.SetIKRotation(gripHand, _grip.rotation);
        }
    }

    // Pulls 'target' to within armReach of the shoulder so the IK goal is always
    // reachable (the solver then extends the whole arm toward it).
    private Vector3 ClampToReach(Transform shoulder, Vector3 target)
    {
        if (shoulder == null) return target;
        Vector3 from = shoulder.position;
        Vector3 dir  = target - from;
        float dist = dir.magnitude;
        if (dist <= armReach || dist < 1e-4f) return target;
        return from + dir / dist * armReach;
    }

    private static void SetHand(Animator a, AvatarIKGoal goal, Vector3 pos, Quaternion rot, float w, bool rotate)
    {
        a.SetIKPositionWeight(goal, w);
        a.SetIKPosition(goal, pos);
        if (rotate)
        {
            a.SetIKRotationWeight(goal, w);
            a.SetIKRotation(goal, rot);
        }
        else a.SetIKRotationWeight(goal, 0f);
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

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showGizmos || !_dbgActive) return;
        Gizmos.color = Color.yellow; Gizmos.DrawSphere(_dbgLeft,  0.05f);
        Gizmos.color = Color.cyan;   Gizmos.DrawSphere(_dbgRight, 0.05f);
        if (_ledge != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_ledge.LedgeTopPoint, 0.04f);
        }
    }
#endif
}
