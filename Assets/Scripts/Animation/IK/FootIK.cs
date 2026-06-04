using UnityEngine;

/// <summary>
/// Grounds the feet to terrain. For each foot it raycasts at the foot's current
/// (x,z), then pins the IK goal to the ground there - lifting the foot UP when the
/// ground is above the animated foot (so it never sinks through a step) and
/// dropping it DOWN to a lower surface, within a reach limit. Foot rotation aligns
/// to the slope.
///
/// The hips are NOT moved by default (hipFollow = 0): moving the body is what
/// caused the "sinks while walking" bug. Enable a small hipFollow only if you want
/// the body to dip slightly on big steps.
///
/// Requires the Animator layer's "IK Pass" to be ON. Auto-suspends in
/// wallhug / ledge / climb / airborne states.
/// </summary>
public class FootIK : MonoBehaviour, IIKModule
{
    [Header("Weights")]
    [Range(0f, 1f)] public float positionWeight = 1f;
    [Range(0f, 1f)] public float rotationWeight = 1f;

    [Header("Raycast (from the hip height down)")]
    [Tooltip("How far above the foot the ray starts.")]
    public float raycastUp = 0.6f;
    [Tooltip("How far below the foot the ray reaches.")]
    public float raycastDown = 0.6f;
    public LayerMask groundMask = ~0;

    [Header("Foot")]
    [Tooltip("Ankle-to-sole distance: keeps the visible foot above the ground point.")]
    public float footHeight = 0.12f;
    [Tooltip("How fast each foot eases to its grounded position (higher = snappier).")]
    public float footSmooth = 15f;
    [Tooltip("Max distance a foot may be pulled from where the animation puts it.")]
    public float maxReach = 0.5f;

    [Header("Hips (optional, off by default)")]
    [Range(0f, 1f)] public float hipFollow = 0f;
    public float hipSmooth = 10f;

    private Animator _anim;
    private PlayerMovement _pm;
    private float _lFootY, _rFootY;     // smoothed grounded foot heights
    private bool  _lInit, _rInit;
    private float _hipOffset;

    public int Priority => 0;           // base; wallhug/hand override on top

    public void Init(PlayerIK owner)
    {
        _anim = owner.Animator;
        _pm = owner.Player;
    }

    public void ApplyIK(Animator animator)
    {
        if (animator == null) return;

        bool suspend = _pm != null && (_pm.IsWallhugging || _pm.IsLedgeGrabbing ||
                                       _pm.IsClimbingLedge || _pm.isJumping);
        if (suspend)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            _lInit = _rInit = false;
            return;
        }

        float lDelta = SolveFoot(animator, AvatarIKGoal.LeftFoot,  ref _lFootY, ref _lInit);
        float rDelta = SolveFoot(animator, AvatarIKGoal.RightFoot, ref _rFootY, ref _rInit);

        // Optional hip dip: lower the body toward the foot that had to drop the most
        // (negative delta = ground below animated foot). Off unless hipFollow > 0.
        if (hipFollow > 0f)
        {
            float drop = Mathf.Min(0f, Mathf.Min(lDelta, rDelta));
            drop = Mathf.Max(drop, -maxReach);
            _hipOffset = Mathf.Lerp(_hipOffset, drop * hipFollow, hipSmooth * Time.deltaTime);
            animator.bodyPosition += Vector3.up * _hipOffset;
        }
    }

    /// <summary>
    /// Pins the foot to the ground beneath it. Returns the signed height delta
    /// (groundFootY - animatedFootY): negative = ground below, positive = above.
    /// </summary>
    private float SolveFoot(Animator animator, AvatarIKGoal goal, ref float smoothY, ref bool init)
    {
        Vector3 animFoot = animator.GetIKPosition(goal);
        // Start the ray from the HIP height above the foot so a step/curb that sits
        // ABOVE the animated foot is still detected (otherwise the ray starts below
        // the curb top and misses it -> the foot clips through the edge).
        float hipY = animator.bodyPosition.y;
        Vector3 origin = new Vector3(animFoot.x, hipY + raycastUp, animFoot.z);
        float maxDist  = (hipY + raycastUp) - (animFoot.y - raycastDown);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit rh,
                            maxDist, groundMask, QueryTriggerInteraction.Ignore))
        {
            float groundFootY = rh.point.y + footHeight;

            // Clamp how far we move the foot from the animation, so it never snaps
            // to far-away geometry.
            float clampedY = Mathf.Clamp(groundFootY, animFoot.y - maxReach, animFoot.y + maxReach);

            if (!init) { smoothY = clampedY; init = true; }
            else smoothY = Mathf.Lerp(smoothY, clampedY, footSmooth * Time.deltaTime);

            Vector3 footPos = new Vector3(animFoot.x, smoothY, animFoot.z);

            animator.SetIKPositionWeight(goal, positionWeight);
            animator.SetIKPosition(goal, footPos);

            Quaternion footRot = Quaternion.FromToRotation(Vector3.up, rh.normal) *
                                 animator.GetIKRotation(goal);
            animator.SetIKRotationWeight(goal, rotationWeight);
            animator.SetIKRotation(goal, footRot);

            return groundFootY - animFoot.y;
        }

        animator.SetIKPositionWeight(goal, 0f);
        animator.SetIKRotationWeight(goal, 0f);
        init = false;
        return 0f;
    }
}
