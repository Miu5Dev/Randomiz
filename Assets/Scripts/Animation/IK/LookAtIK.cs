using UnityEngine;

/// <summary>
/// Makes the head (and a bit of the chest/eyes) look at a target: the current
/// combat target when targeting, otherwise the movement direction. Uses Humanoid
/// SetLookAtPosition - smooth and natural, no extra rig.
///
/// Requires the Animator layer's "IK Pass" to be ON.
/// </summary>
public class LookAtIK : MonoBehaviour, IIKModule
{
    [Header("Weights")]
    [Range(0f, 1f)] public float weight = 1f;
    [Range(0f, 1f)] public float bodyWeight = 0.3f;
    [Range(0f, 1f)] public float headWeight = 0.8f;
    [Range(0f, 1f)] public float eyesWeight = 1f;
    [Range(0f, 1f)] public float clampWeight = 0.5f;

    [Header("Behaviour")]
    [Tooltip("How fast the look target eases (higher = snappier).")]
    public float followSpeed = 8f;
    [Tooltip("Max distance to look toward a movement direction point.")]
    public float lookAheadDistance = 5f;

    private Animator _anim;
    private PlayerMovement _pm;
    private TargetingSystem _targeting;
    private Vector3 _smoothTarget;
    private float _smoothWeight;

    public int Priority => 5;           // doesn't touch limb goals

    public void Init(PlayerIK owner)
    {
        _anim = owner.Animator;
        _pm = owner.Player;
        if (_pm != null) _targeting = _pm.targetingSystem;
        _smoothTarget = transform.position + transform.forward * lookAheadDistance;
    }

    public void ApplyIK(Animator animator)
    {
        if (animator == null) return;

        Vector3 desired;
        float targetWeight = weight;

        if (_targeting != null && _targeting.IsTargeting && _targeting.CurrentTarget != null)
        {
            desired = _targeting.CurrentTarget.position;
        }
        else
        {
            // Look toward the movement direction (or forward when idle).
            Vector3 dir = _pm != null ? new Vector3(_pm.velocity.x, 0f, _pm.velocity.z) : Vector3.zero;
            if (dir.sqrMagnitude < 0.04f) { dir = transform.forward; targetWeight = weight * 0.4f; }
            Vector3 head = animator.GetBoneTransform(HumanBodyBones.Head) != null
                ? animator.GetBoneTransform(HumanBodyBones.Head).position
                : transform.position + Vector3.up * 1.6f;
            desired = head + dir.normalized * lookAheadDistance;
        }

        _smoothTarget = Vector3.Lerp(_smoothTarget, desired, followSpeed * Time.deltaTime);
        _smoothWeight = Mathf.Lerp(_smoothWeight, targetWeight, followSpeed * Time.deltaTime);

        animator.SetLookAtWeight(_smoothWeight, bodyWeight, headWeight, eyesWeight, clampWeight);
        animator.SetLookAtPosition(_smoothTarget);
    }
}
