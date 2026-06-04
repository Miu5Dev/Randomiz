using UnityEngine;

/// <summary>
/// Drives the player's Animator from PlayerMovement state and input events.
/// Lives on the model object (the one with the Animator), a child of the player.
///
/// Base layer  : 1D locomotion blend tree (Speed) + Jump / Roll / Hit states.
/// Action layer: masked upper-body actions (attack / sheathe) so the player can
///               act while moving - the "merge" of two animations.
///
/// Parameter names are centralised in AnimParams so the generator
/// (AnimatorControllerBuilder) and this driver never drift apart.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerAnimator : MonoBehaviour
{
    [Header("Tuning")]
    [Tooltip("How fast the Speed parameter follows the real planar speed (damping).")]
    [SerializeField] private float speedDamp = 10f;
    [Tooltip("Planar speed below this is treated as idle (avoids foot sliding).")]
    [SerializeField] private float idleThreshold = 0.05f;
    [Tooltip("Reference run speed used to normalise Speed to 0..1 (set to PlayerMovement.runSpeed).")]
    [SerializeField] private float referenceRunSpeed = 8f;

    [Header("Action layer")]
    [Tooltip("Index of the masked upper-body action layer (1 with the generated controller).")]
    [SerializeField] private int actionLayer = 1;
    [Tooltip("How fast the action layer weight blends in/out.")]
    [SerializeField] private float actionBlend = 12f;

    [Header("Ledge shimmy")]
    [Tooltip("Fraction of moveSpeed at which the shimmy blend reaches full L/R.")]
    [SerializeField] private float _ledgeSpeedMul = 0.5f;
    [Tooltip("Lateral speed (m/s) the shimmy clip is authored for. Used to sync playback.")]
    [SerializeField] private float shimmyRefSpeed = 1.2f;

    private Animator _animator;
    private PlayerMovement _pm;
    private PlayerLedgeGrab _ledge;

    private float _smoothSpeed;
    private bool  _wasGrounded = true;
    private float _actionWeight;
    private int   _actionStateNoneHash;
    private float _shimmyDir;
    private float _rollClipLength;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        // PlayerMovement is the singleton on the parent player rig.
        _pm = GetComponentInParent<PlayerMovement>();
        if (_pm == null) _pm = PlayerMovement.Instance;
        if (_pm != null) _ledge = _pm.GetComponent<PlayerLedgeGrab>();

        _actionStateNoneHash = Animator.StringToHash("None");
        _rollClipLength = FindClipLength("Roll");

        // Match the script-driven ledge climb to the climb clip length so the body
        // and the animation finish together (no teleport-then-animate snap).
        if (_ledge != null)
        {
            float climbLen = FindClipLength("Climbing");
            if (climbLen > 0.05f) _ledge.SetClimbDuration(climbLen);
        }
    }

    /// <summary>Length (s) of a clip in the controller whose name contains 'partial'.</summary>
    private float FindClipLength(string partial)
    {
        if (_animator.runtimeAnimatorController == null) return 0f;
        foreach (var c in _animator.runtimeAnimatorController.animationClips)
            if (c != null && c.name.IndexOf(partial, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return c.length;
        return 0f;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnAttackInputEvent>(OnAttack);
        EventBus.Subscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Subscribe<OnItemUnequipEvent>(OnItemUnequip);
        EventBus.Subscribe<OnDamagedEvent>(OnDamaged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnAttackInputEvent>(OnAttack);
        EventBus.Unsubscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Unsubscribe<OnItemUnequipEvent>(OnItemUnequip);
        EventBus.Unsubscribe<OnDamagedEvent>(OnDamaged);
    }

    private void Update()
    {
        if (_pm == null || _animator == null) return;

        // ── Locomotion ─────────────────────────────────────────────────────
        Vector3 v = _pm.velocity;
        float planar = new Vector2(v.x, v.z).magnitude;
        if (planar < idleThreshold) planar = 0f;

        // Normalise to 0..1 (idle..run) so the blend tree thresholds are stable
        // regardless of the actual move/run speeds.
        float target = referenceRunSpeed > 0.01f ? planar / referenceRunSpeed : planar;
        _smoothSpeed = Mathf.Lerp(_smoothSpeed, target, speedDamp * Time.deltaTime);
        _animator.SetFloat(AnimParams.Speed, _smoothSpeed);

        bool grounded = !_pm.isJumping && Mathf.Abs(v.y) < 0.5f;
        _animator.SetBool(AnimParams.IsGrounded, grounded);
        _animator.SetBool(AnimParams.IsMoving, planar > 0f);

        // Vertical velocity for jump/fall blends.
        _animator.SetFloat(AnimParams.VerticalSpeed, v.y);

        // ── Action / state flags ───────────────────────────────────────────
        _animator.SetBool(AnimParams.IsDashing,      _pm.IsDashing);
        _animator.SetBool(AnimParams.IsWallhugging,  _pm.IsWallhugging);
        _animator.SetBool(AnimParams.IsLedgeGrabbing, _pm.IsLedgeGrabbing);
        _animator.SetBool(AnimParams.IsClimbingLedge, _pm.IsClimbingLedge);

        // Feed the climb animation's real progress to the movement script so the
        // body follows the clip's motion curve (fixes "body leads, anim lags").
        if (_pm.IsClimbingLedge && _ledge != null)
        {
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsName("ClimbUp"))
                _ledge.SetClimbProgress(Mathf.Clamp01(st.normalizedTime));
        }

        // Roll playback speed: play the WHOLE roll clip within the dash duration,
        // so the animation never gets cut short when the dash is brief. Speed =
        // clipLength / dashDuration (clamped). Always set it (default 1) so the
        // roll never freezes if the clip length couldn't be cached.
        float rollSpeed = 1f;
        if (_pm.Dash != null && _rollClipLength > 0.01f)
        {
            float dur = Mathf.Max(_pm.Dash.dashDuration, 0.01f);
            rollSpeed = Mathf.Clamp(_rollClipLength / dur, 0.25f, 6f);
        }
        _animator.SetFloat(AnimParams.RollSpeed, rollSpeed);

        // Shimmy while hanging. Project the ledge velocity onto the LEDGE tangent
        // (not the model's right - the model faces the wall, so its right axis is
        // wrong here). Sign: +1 right, -1 left. Also sync playback speed to the real
        // drag speed so the feet/hands don't slide out of sync with the movement.
        if (_pm.IsLedgeGrabbing && _ledge != null)
        {
            Vector3 along = Vector3.Cross(Vector3.up, _ledge.LedgeWallNormal).normalized;
            float lateralSpeed = Vector3.Dot(v, along);                 // m/s along the edge
            float maxLateral   = Mathf.Max(_pm.moveSpeed * _ledgeSpeedMul, 0.01f);
            float dir = Mathf.Clamp(lateralSpeed / maxLateral, -1f, 1f);
            _shimmyDir = Mathf.Lerp(_shimmyDir, dir, 12f * Time.deltaTime);

            // Drive the shimmy clip speed by how fast we actually move. The shimmy
            // clip is authored for ~shimmyRefSpeed m/s; scale to match real speed.
            float playback = Mathf.Abs(lateralSpeed) / Mathf.Max(shimmyRefSpeed, 0.01f);
            _animator.SetFloat(AnimParams.ShimmySpeed, Mathf.Clamp(playback, 0.1f, 2.5f));
        }
        else
        {
            _shimmyDir = Mathf.Lerp(_shimmyDir, 0f, 12f * Time.deltaTime);
            _animator.SetFloat(AnimParams.ShimmySpeed, 1f);
        }
        _animator.SetFloat(AnimParams.ShimmyDir, _shimmyDir);

        // Landing trigger (was airborne, now grounded).
        if (grounded && !_wasGrounded)
            _animator.SetTrigger(AnimParams.Land);
        _wasGrounded = grounded;

        // ── Action layer weight ────────────────────────────────────────────
        // Raise the masked upper-body layer to 1 only while it's playing an action
        // (not on the empty "None" state and not mid-transition into None). This
        // stops the arms snapping to the bind pose during plain locomotion.
        UpdateActionLayerWeight();
    }

    private void UpdateActionLayerWeight()
    {
        if (actionLayer < 0 || actionLayer >= _animator.layerCount) return;

        var info = _animator.GetCurrentAnimatorStateInfo(actionLayer);
        bool inAction = info.shortNameHash != _actionStateNoneHash;
        // While transitioning, keep the layer up so the blend reads naturally.
        if (_animator.IsInTransition(actionLayer)) inAction = true;

        float target = inAction ? 1f : 0f;
        _actionWeight = Mathf.MoveTowards(_actionWeight, target, actionBlend * Time.deltaTime);
        _animator.SetLayerWeight(actionLayer, _actionWeight);
    }

    // ── Event handlers → triggers ──────────────────────────────────────────

    private void OnAttack(OnAttackInputEvent e)
    {
        if (!e.pressed) return;
        // Only the upper-body action layer reacts; locomotion keeps playing.
        if (EquipHandler.Instance != null && EquipHandler.Instance.EquipedItem is SOWeapon)
            _animator.SetTrigger(AnimParams.Attack);
        else if (EquipHandler.Instance != null && EquipHandler.Instance.EquipedItem is SOPotion)
            _animator.SetTrigger(AnimParams.UseItem);
    }

    private void OnItemEquip(OnItemEquipEvent e)
    {
        // Switch the action layer's "armed" pose if a weapon is equipped.
        bool armed = e.item is SOWeapon;
        _animator.SetBool(AnimParams.Armed, armed);
    }

    private void OnItemUnequip(OnItemUnequipEvent e)
    {
        _animator.SetBool(AnimParams.Armed, false);
    }

    private void OnDamaged(OnDamagedEvent e)
    {
        if (e.victim != (_pm != null ? _pm.gameObject : null)) return;
        _animator.SetTrigger(AnimParams.Hit);
    }
}
