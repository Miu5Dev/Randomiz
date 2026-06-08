using UnityEngine;

/// <summary>
/// Handles cross-cutting animation concerns that sit above individual clip playback:
///   • Reacts to OnDamagedEvent / OnDieEvent for hit and death triggers.
///   • Provides momentum-decay helpers for locomotion → idle blending so the
///     animator speed parameter eases out rather than snapping.
///   • Exposes ApplyRootMotionForClip so callers can switch root-motion on/off
///     for specific clips (e.g. attack clips that need it).
///
/// Attach to the same model object as the Animator.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimationTransitionHelper : MonoBehaviour
{
    [Header("Momentum decay")]
    [Tooltip("How fast the Speed parameter decays when the player stops moving.")]
    [SerializeField] private float momentumDecayRate = 6f;

    [Tooltip("Speed threshold below which the parameter is snapped to zero to avoid micro-drift.")]
    [SerializeField] private float snapToZeroThreshold = 0.02f;

    // Internal decay state — set by external callers via BeginMomentumDecay().
    private bool  _decaying;
    private float _decayedSpeed;

    private Animator   _animator;
    private PlayerMovement _pm;

    // Hash for the Death trigger (same naming convention as AnimParams).
    private static readonly int DeathHash = Animator.StringToHash("Death");

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _pm = GetComponentInParent<PlayerMovement>();
        if (_pm == null) _pm = PlayerMovement.Instance;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnDamagedEvent>(OnDamaged);
        EventBus.Subscribe<OnDieEvent>(OnDie);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnDamagedEvent>(OnDamaged);
        EventBus.Unsubscribe<OnDieEvent>(OnDie);
    }

    private void Update()
    {
        if (!_decaying) return;

        _decayedSpeed = Mathf.Lerp(_decayedSpeed, 0f, momentumDecayRate * Time.deltaTime);
        if (_decayedSpeed < snapToZeroThreshold)
        {
            _decayedSpeed = 0f;
            _decaying     = false;
        }
        _animator.SetFloat(AnimParams.Speed, _decayedSpeed);
    }

    // ── Event handlers ──────────────────────────────────────────────────────

    private void OnDamaged(OnDamagedEvent e)
    {
        // Only react when this model's owner is the victim.
        if (_pm != null && e.victim != _pm.gameObject) return;
        // Hit trigger is handled by PlayerAnimator; this component handles death only.
    }

    private void OnDie(OnDieEvent e)
    {
        if (_pm != null && e.murdered != _pm.gameObject) return;

        _animator.SetTrigger(DeathHash);

        // Disable movement so the player can't walk away during the death animation.
        EventBus.Raise(new OnSetMovementEnabledEvent { enabled = false });
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Start a momentum-decay pass on the Speed parameter. Call this when
    /// transitioning from run/walk to idle so the parameter eases out rather than
    /// snapping. PlayerAnimator continues overwriting Speed each frame, so only call
    /// this when you want to temporarily take over (e.g. from a state-change callback).
    /// </summary>
    public void BeginMomentumDecay(float fromSpeed)
    {
        _decayedSpeed = fromSpeed;
        _decaying     = true;
    }

    /// <summary>
    /// Stops an ongoing momentum-decay pass immediately.
    /// </summary>
    public void CancelMomentumDecay()
    {
        _decaying = false;
    }

    /// <summary>
    /// Enables or disables root motion for a specific clip by matching its name
    /// against the controller's clip list. Applies the setting to the Animator
    /// immediately (last call wins if multiple clips match).
    /// </summary>
    /// <param name="clipName">Partial, case-insensitive clip name to match.</param>
    /// <param name="enable">True to enable root motion while that clip plays.</param>
    public void ApplyRootMotionForClip(string clipName, bool enable)
    {
        if (_animator.runtimeAnimatorController == null) return;

        foreach (var clip in _animator.runtimeAnimatorController.animationClips)
        {
            if (clip != null &&
                clip.name.IndexOf(clipName, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _animator.applyRootMotion = enable;
                return;
            }
        }

        // Clip not found — log once so designers know the name is wrong.
        Debug.LogWarning(
            $"[AnimationTransitionHelper] ApplyRootMotionForClip: no clip matching '{clipName}' found.",
            this);
    }
}
