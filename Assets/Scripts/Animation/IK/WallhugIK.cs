using UnityEngine;

/// <summary>
/// Fully procedural wall-hug pose (no animation clip needed). While the player is
/// wallhugging it:
///   • faces the wall (rotates the model to look at it),
///   • pins both hands flat on the wall, spread to the sides,
///   • pins both feet to the wall, slightly lower and apart,
///   • when moving sideways, animates a subtle climbing/crawl cycle - hands and
///     feet alternate forward along the wall in sync with the lateral speed.
///
/// Layered on top of whatever base pose is playing (ideally a calm idle). Requires
/// the Animator layer's "IK Pass" to be ON. Add alongside PlayerIK.
/// </summary>
public class WallhugIK : MonoBehaviour, IIKModule
{
    [Header("Blend")]
    [Tooltip("How fast the pose fades in/out when entering/leaving wallhug.")]
    public float weightSmooth = 10f;

    [Header("Facing")]
    [Tooltip("How fast the model turns to face the wall.")]
    public float faceTurnSpeed = 12f;

    [Header("Hands")]
    public float handSpread   = 0.28f;   // sideways from body centre
    public float handHeight   = 1.25f;   // above the feet
    public float handForward  = 0.18f;   // distance to the wall surface

    [Header("Feet")]
    public float footSpread   = 0.16f;
    public float footHeight   = 0.05f;
    public float footForward  = 0.12f;

    [Header("Crawl cycle (when moving sideways)")]
    [Tooltip("Vertical bob of the limbs during the crawl, in metres.")]
    public float limbBob = 0.12f;
    [Tooltip("Forward/back reach of the limbs during the crawl, in metres.")]
    public float limbReach = 0.10f;
    [Tooltip("Crawl cycles per metre travelled along the wall.")]
    public float cyclesPerMetre = 1.5f;

    private Animator _anim;
    private PlayerMovement _pm;
    private float _w;
    private float _cyclePhase;
    private Vector3 _lastPos;

    public int Priority => 20;          // wallhug owns all limbs, overrides others

    public void Init(PlayerIK owner)
    {
        _anim = owner.Animator;
        _pm = owner.Player;
        _lastPos = transform.position;
    }

    private void Update()
    {
        // Advance the crawl phase by distance travelled along the wall (in Update
        // so it tracks real movement, OnAnimatorIK timing is irregular).
        if (_pm != null && _pm.IsWallhugging)
        {
            float dist = Vector3.Distance(transform.position, _lastPos);
            _cyclePhase += dist * cyclesPerMetre * Mathf.PI * 2f;
        }
        _lastPos = transform.position;
    }

    public void ApplyIK(Animator animator)
    {
        if (animator == null || _pm == null) return;

        bool active = _pm.IsWallhugging;
        _w = Mathf.MoveTowards(_w, active ? 1f : 0f, weightSmooth * Time.deltaTime);
        if (_w <= 0.001f)
        {
            ReleaseAll(animator);
            return;
        }

        Vector3 wallNormal = _pm.WallNormal;            // points away from wall toward player
        if (wallNormal.sqrMagnitude < 0.01f) wallNormal = -transform.forward;
        Vector3 toWall = -wallNormal;
        Vector3 along  = Vector3.Cross(Vector3.up, wallNormal).normalized;
        // Make 'along' agree with the character's right so left/right limbs don't
        // cross over (the cross product can point either way along the wall).
        if (Vector3.Dot(along, transform.right) < 0f) along = -along;

        // ── Face the wall ───────────────────────────────────────────────────
        Quaternion faceRot = Quaternion.LookRotation(toWall, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, faceRot,
                                              faceTurnSpeed * Time.deltaTime * _w);

        // Crawl offsets: limbs alternate. Left side uses +phase, right uses -phase,
        // scaled by how fast we move sideways (0 = still pose, no crawl).
        float speed01 = Mathf.Clamp01(new Vector2(_pm.velocity.x, _pm.velocity.z).magnitude /
                                      Mathf.Max(_pm.moveSpeed, 0.01f));
        float s = Mathf.Sin(_cyclePhase) * speed01;
        float c = Mathf.Cos(_cyclePhase) * speed01;

        Vector3 basePos = transform.position;

        // Hands: left/right spread, on the wall, with alternating bob+reach.
        Vector3 handBase = basePos + Vector3.up * handHeight + toWall * handForward;
        Vector3 lHand = handBase - along * handSpread + Vector3.up * (limbBob * s)  + along * (limbReach * c);
        Vector3 rHand = handBase + along * handSpread - Vector3.up * (limbBob * s)  - along * (limbReach * c);

        // Feet: opposite phase to the hands (diagonal crawl gait).
        Vector3 footBase = basePos + Vector3.up * footHeight + toWall * footForward;
        Vector3 lFoot = footBase - along * footSpread - Vector3.up * (limbBob * s) - along * (limbReach * c);
        Vector3 rFoot = footBase + along * footSpread + Vector3.up * (limbBob * s) + along * (limbReach * c);

        Quaternion handRot = Quaternion.LookRotation(toWall, Vector3.up);

        SetGoal(animator, AvatarIKGoal.LeftHand,  lHand, handRot, _w);
        SetGoal(animator, AvatarIKGoal.RightHand, rHand, handRot, _w);
        SetGoal(animator, AvatarIKGoal.LeftFoot,  lFoot, handRot, _w);
        SetGoal(animator, AvatarIKGoal.RightFoot, rFoot, handRot, _w);
    }

    private static void SetGoal(Animator a, AvatarIKGoal goal, Vector3 pos, Quaternion rot, float w)
    {
        a.SetIKPositionWeight(goal, w);
        a.SetIKRotationWeight(goal, w * 0.6f);
        a.SetIKPosition(goal, pos);
        a.SetIKRotation(goal, rot);
    }

    private static void ReleaseAll(Animator a)
    {
        a.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
        a.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
        a.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
        a.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
    }
}
