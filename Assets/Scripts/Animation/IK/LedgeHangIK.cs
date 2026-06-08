using UnityEngine;

/// <summary>
/// Fully procedural ledge hang + shimmy (no animation clip).
///
/// Design for robustness (no floating hands, no backward legs):
///   • HANDS are anchored to each SHOULDER projected onto the wall plane, then
///     raised to the ledge height. Because the target starts at the shoulder it is
///     ALWAYS within arm reach, and because it's projected onto the wall plane it
///     ALWAYS sits on the wall. A hand-over-hand offset is layered on top for the
///     shimmy (limbs alternately reach in the move direction), scaled by speed.
///   • FEET only get IK when a wall actually exists below (a single raycast that
///     starts outside the wall). With no wall the foot weight is 0, so the legs
///     simply hang with the base pose instead of being forced into a bad bend.
///
/// The wall plane is taken exactly from the ledge data (LedgeTopPoint +
/// LedgeWallNormal); no fragile multi-raycast edge finding.
///
/// Add alongside PlayerIK. Highest limb priority while hanging. Needs IK Pass ON.
/// </summary>
public class LedgeHangIK : MonoBehaviour, IIKModule
{
    [Header("Hands")]
    [Tooltip("Vertical offset from the ledge top edge (raise/lower the grip).")]
    public float handHeight = 0.0f;
    [Tooltip("Push onto (+) or off (-) the wall face.")]
    public float handDepth = 0.03f;
    [Tooltip("Max shoulder->hand reach. Hard cap so the arm never flings out.")]
    public float armReach = 0.55f;
    [Range(0f,1f)] public float handRotationWeight = 0.5f;

    [Header("Feet")]
    [Tooltip("How far below the ledge to look for / place a foothold.")]
    public float footDrop = 1.2f;
    [Tooltip("Half spacing between the feet.")]
    public float footSpacing = 0.13f;
    [Tooltip("Push the planted foot into the wall.")]
    public float footDepth = 0.03f;
    [Tooltip("Probe length toward the wall (starts this far outside it).")]
    public float footProbe = 0.35f;
    // No layer setup needed: reuses the player's own collision mask (same one the
    // ledge/movement system uses), so it works on any project without layers.

    [Header("Shimmy gait")]
    [Tooltip("How far each hand slides along the edge during its step (keep small).")]
    public float gaitReach = 0.08f;
    [Tooltip("How far a limb bobs UP along the wall mid-step.")]
    public float gaitLift = 0.05f;
    [Tooltip("Gait cycles per metre travelled along the wall.")]
    public float gaitFrequency = 2.2f;

    [Header("Facing & blend")]
    public float faceTurnSpeed = 14f;
    public float weightSmooth = 14f;

    [Header("Debug")]
    public bool showGizmos = true;

    private Animator _anim;
    private PlayerMovement _pm;
    private PlayerLedgeGrab _ledge;
    private PhysicsController _physics;
    private float _w;
    private float _gaitPhase;

    private Vector3 _gLH, _gRH, _gLF, _gRF;
    private bool _lfPlanted, _rfPlanted, _gActive;

    public int Priority => 25;

    public void Init(PlayerIK owner)
    {
        _anim  = owner.Animator;
        _pm    = owner.Player;
        _ledge = owner.Ledge;
        if (_pm != null) _physics = _pm.GetComponent<PhysicsController>();
    }

    public void ApplyIK(Animator animator)
    {
        if (animator == null || _pm == null || _ledge == null) { _gActive = false; return; }

        bool hanging = _pm.IsLedgeGrabbing || _pm.IsClimbingLedge;
        _w = Mathf.MoveTowards(_w, hanging ? 1f : 0f, weightSmooth * Time.deltaTime);
        if (_w <= 0.001f) { _gActive = false; return; }

        // ── Exact wall basis from the ledge data ────────────────────────────
        Vector3 ledgeTop = _ledge.LedgeTopPoint;
        Vector3 wallN = _ledge.LedgeWallNormal;            // away from the wall
        if (wallN.sqrMagnitude < 0.01f) wallN = -transform.forward;
        wallN.Normalize();
        Vector3 toWall = -wallN;
        Vector3 along  = Vector3.Cross(Vector3.up, wallN).normalized;
        if (Vector3.Dot(along, transform.right) < 0f) along = -along;

        // ── Face the wall ───────────────────────────────────────────────────
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(toWall, Vector3.up), faceTurnSpeed * Time.deltaTime * _w);

        // ── Gait phase advances with distance travelled along the wall ──────
        float lateralVel = Vector3.Dot(_pm.velocity, along);
        float moveAmt  = Mathf.Clamp01(Mathf.Abs(lateralVel) / Mathf.Max(_pm.moveSpeed * 0.5f, 0.01f));
        float moveSign = Mathf.Abs(lateralVel) > 0.02f ? Mathf.Sign(lateralVel) : 0f;
        _gaitPhase += Mathf.Abs(lateralVel) * Time.deltaTime * gaitFrequency * Mathf.PI * 2f;

        // Hand-over-hand: the two limbs are half a cycle out of phase. Each "reaches"
        // (forward along the move dir + lifts off the wall) only on the positive half
        // of its swing; the other limb stays planted. Diagonal foot pairing.
        float swA = Mathf.Max(0f, Mathf.Sin(_gaitPhase));            // left hand / right foot
        float swB = Mathf.Max(0f, Mathf.Sin(_gaitPhase + Mathf.PI)); // right hand / left foot

        // ── HANDS: shoulder projected on wall, raised to the ledge ──────────
        _gLH = HandGrip(animator, HumanBodyBones.LeftUpperArm,  ledgeTop, wallN, toWall, along, moveSign, swA * moveAmt);
        _gRH = HandGrip(animator, HumanBodyBones.RightUpperArm, ledgeTop, wallN, toWall, along, moveSign, swB * moveAmt);

        Quaternion handRot = Quaternion.LookRotation(toWall, Vector3.up);
        SetHand(animator, AvatarIKGoal.LeftHand,  _gLH, handRot, _w);
        SetHand(animator, AvatarIKGoal.RightHand, _gRH, handRot, _w);

        // ── FEET: plant on a wall below, else hang free ─────────────────────
        Vector3 hipOnWall = ProjectOnWall(transform.position, ledgeTop, wallN);
        Vector3 footCenter = hipOnWall; footCenter.y = ledgeTop.y - footDrop;
        Vector3 lfBase = footCenter - along * footSpacing + along * (moveSign * gaitReach * 0.6f * swB) - wallN * (gaitLift * swB);
        Vector3 rfBase = footCenter + along * footSpacing + along * (moveSign * gaitReach * 0.6f * swA) - wallN * (gaitLift * swA);

        _lfPlanted = TryPlantFoot(animator, AvatarIKGoal.LeftFoot,  lfBase, wallN, toWall, out _gLF);
        _rfPlanted = TryPlantFoot(animator, AvatarIKGoal.RightFoot, rfBase, wallN, toWall, out _gRF);

        _gActive = true;
    }

    // Hand grip: take the shoulder, project it onto the wall plane, lift it to the
    // ledge height, push to the wall, then add the gait reach/lift. Always on the
    // wall and always within reach (starts from the shoulder).
    private Vector3 HandGrip(Animator a, HumanBodyBones shoulderBone, Vector3 ledgeTop,
                             Vector3 wallN, Vector3 toWall, Vector3 along, float moveSign, float swing)
    {
        Transform sh = a.GetBoneTransform(shoulderBone);
        Vector3 shoulder = sh != null ? sh.position : transform.position + Vector3.up * 1.4f;

        // Base grip: shoulder projected on the wall, raised to the ledge edge, on
        // the wall face. This is the planted position - on the wall, under reach.
        Vector3 onWall = ProjectOnWall(shoulder, ledgeTop, wallN);
        onWall.y = ledgeTop.y + handHeight;
        onWall += toWall * handDepth;

        // Gait, kept ON the wall: slide a little along the edge in the move dir and
        // bob slightly UP - never push along the wall normal (that throws the hand
        // out into the air, which is what looked wrong). Reach stays small.
        onWall += along * (moveSign * gaitReach * swing);
        onWall += Vector3.up * (gaitLift * swing);

        // Clamp to the shoulder's reach so the arm can never fully fling out toward
        // an unreachable point - it always stays a believable grip near the wall.
        Vector3 dir = onWall - shoulder;
        float d = dir.magnitude;
        if (d > armReach && d > 1e-4f) onWall = shoulder + dir / d * armReach;
        return onWall;
    }

    // Projects a world point onto the wall plane (plane through ledgeTop, normal wallN).
    private static Vector3 ProjectOnWall(Vector3 p, Vector3 ledgeTop, Vector3 wallN)
        => p - wallN * Vector3.Dot(p - ledgeTop, wallN);

    // Plants the foot if a wall is below; else releases the goal (foot hangs).
    private bool TryPlantFoot(Animator a, AvatarIKGoal goal, Vector3 footOnWall,
                              Vector3 wallN, Vector3 toWall, out Vector3 result)
    {
        // Reuse the player's collision mask (no per-project layer setup needed).
        LayerMask mask = _physics != null ? _physics.collisionMask : ~0;
        Vector3 origin = footOnWall + wallN * footProbe;   // start outside the wall
        if (Physics.Raycast(origin, toWall, out RaycastHit hit, footProbe * 2f,
                            mask, QueryTriggerInteraction.Ignore))
        {
            result = hit.point + wallN * footDepth;
            a.SetIKPositionWeight(goal, _w);
            a.SetIKPosition(goal, result);
            a.SetIKRotationWeight(goal, 0f);   // no forced rotation -> no backward bend
            return true;
        }
        a.SetIKPositionWeight(goal, 0f);
        a.SetIKRotationWeight(goal, 0f);
        result = footOnWall;
        return false;
    }

    private void SetHand(Animator a, AvatarIKGoal goal, Vector3 pos, Quaternion rot, float w)
    {
        a.SetIKPositionWeight(goal, w);
        a.SetIKPosition(goal, pos);
        a.SetIKRotationWeight(goal, w * handRotationWeight);
        a.SetIKRotation(goal, rot);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showGizmos || !_gActive) return;
        Gizmos.color = Color.yellow; Gizmos.DrawSphere(_gLH, 0.04f);
        Gizmos.color = Color.cyan;   Gizmos.DrawSphere(_gRH, 0.04f);
        Gizmos.color = _lfPlanted ? Color.green : Color.gray; Gizmos.DrawSphere(_gLF, 0.04f);
        Gizmos.color = _rfPlanted ? Color.green : Color.gray; Gizmos.DrawSphere(_gRF, 0.04f);
        if (_ledge != null) { Gizmos.color = Color.red; Gizmos.DrawWireSphere(_ledge.LedgeTopPoint, 0.04f); }
    }
#endif
}
