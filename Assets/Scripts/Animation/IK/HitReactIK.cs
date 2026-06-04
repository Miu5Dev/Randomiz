using UnityEngine;

/// <summary>
/// Dynamic hit reaction layered on top of the base animation: on a landed hit it
/// bends the spine/chest away from the attacker for a short, decaying impulse.
/// Subtle and procedural - complements (doesn't replace) the Hit animation state.
///
/// Requires the Animator layer's "IK Pass" to be ON.
/// </summary>
public class HitReactIK : MonoBehaviour, IIKModule
{
    [Header("Reaction")]
    [Tooltip("Peak bend angle (degrees) away from the impact.")]
    public float maxAngle = 18f;
    [Tooltip("How quickly the reaction decays back to neutral.")]
    public float decay = 4f;
    [Tooltip("Bones to bend - more bones = a softer, fuller body reaction.")]
    public bool bendSpine = true;
    public bool bendChest = true;

    private Animator _anim;
    private PlayerMovement _pm;
    private float   _intensity;     // 0..1, decays over time
    private Vector3 _impactDir;     // world-space, away from attacker

    public int Priority => 5;           // bends spine, doesn't touch limb goals

    public void Init(PlayerIK owner)
    {
        _anim = owner.Animator;
        _pm = owner.Player;
        EventBus.Subscribe<OnDamagedEvent>(OnDamaged);
    }

    private void OnDestroy() => EventBus.Unsubscribe<OnDamagedEvent>(OnDamaged);

    private void OnDamaged(OnDamagedEvent e)
    {
        if (_pm != null && e.victim != _pm.gameObject) return;
        if (e.attacker == null) { _impactDir = -transform.forward; }
        else
        {
            Vector3 d = transform.position - e.attacker.transform.position;
            d.y = 0f;
            _impactDir = d.sqrMagnitude > 1e-4f ? d.normalized : -transform.forward;
        }
        _intensity = 1f;
    }

    public void ApplyIK(Animator animator)
    {
        if (animator == null || _intensity <= 0.001f) return;

        _intensity = Mathf.Max(0f, _intensity - decay * Time.deltaTime);

        // Local bend direction: project the world impact onto the body's axes.
        float angle = maxAngle * _intensity;
        // Bend backward (around the right axis) + a little sideways (around forward).
        float pitch = Vector3.Dot(_impactDir, transform.forward) * angle;
        float roll  = Vector3.Dot(_impactDir, transform.right)   * angle;

        if (bendSpine) BendBone(animator, HumanBodyBones.Spine, pitch * 0.5f, roll * 0.5f);
        if (bendChest) BendBone(animator, HumanBodyBones.Chest, pitch * 0.5f, roll * 0.5f);
    }

    private void BendBone(Animator animator, HumanBodyBones bone, float pitchDeg, float rollDeg)
    {
        Transform t = animator.GetBoneTransform(bone);
        if (t == null) return;
        Quaternion extra = Quaternion.AngleAxis(pitchDeg, transform.right) *
                           Quaternion.AngleAxis(rollDeg,  transform.forward);
        t.rotation = extra * t.rotation;
    }
}
