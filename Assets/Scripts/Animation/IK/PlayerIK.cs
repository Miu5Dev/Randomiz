using System.Linq;
using UnityEngine;

/// <summary>
/// Central IK coordinator. Unity delivers OnAnimatorIK to ONE component, so this
/// receives it and dispatches to each enabled IK module (FootIK, HandIK, LookAtIK,
/// HitReactIK, WallhugIK on the same GameObject - auto-collected).
///
/// Two jobs that fix module conflicts:
///   1. Resets all four limb IK goals to weight 0 each frame BEFORE dispatching,
///      so a module that goes inactive doesn't leave a stale goal, and modules
///      never need to zero each other out.
///   2. Runs modules in Priority order (low -> high), so the highest-priority
///      active module writes a shared goal LAST and wins (e.g. WallhugIK over FootIK).
///
/// Shares the lazily-resolved PlayerMovement / PlayerLedgeGrab refs (no Awake race).
/// The Animator layer's "IK Pass" must be ON or these calls do nothing.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerIK : MonoBehaviour
{
    public Animator Animator { get; private set; }

    private IIKModule[] _modules;
    private PlayerMovement  _player;
    private PlayerLedgeGrab _ledge;
    private bool _initialised;

    public PlayerMovement Player
    {
        get
        {
            if (_player == null)
                _player = GetComponentInParent<PlayerMovement>() ?? PlayerMovement.Instance;
            return _player;
        }
    }

    public PlayerLedgeGrab Ledge
    {
        get
        {
            if (_ledge == null && Player != null)
                _ledge = Player.GetComponent<PlayerLedgeGrab>();
            return _ledge;
        }
    }

    private void Awake()
    {
        Animator = GetComponent<Animator>();
        // Sort by priority so high-priority modules write shared goals last.
        _modules = GetComponents<IIKModule>().OrderBy(m => m.Priority).ToArray();
    }

    private void Start()
    {
        foreach (var m in _modules) m.Init(this);
        _initialised = true;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (_modules == null || !_initialised || layerIndex != 0) return;

        // Clean slate each frame so inactive modules leave no residue.
        Animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
        Animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
        Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
        Animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
        Animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
        Animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
        Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
        Animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);

        foreach (var m in _modules)
            if (((MonoBehaviour)m).enabled) m.ApplyIK(Animator);
    }
}

/// <summary>Contract for a pluggable IK behaviour driven by PlayerIK.</summary>
public interface IIKModule
{
    /// <summary>Lower runs first; higher writes shared goals last (wins). Default 0.</summary>
    int Priority { get; }
    void Init(PlayerIK owner);
    void ApplyIK(Animator animator);
}
