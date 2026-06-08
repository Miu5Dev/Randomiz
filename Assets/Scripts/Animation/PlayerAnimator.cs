using UnityEngine;

/// <summary>
/// Drives the player's Animator from the authoritative PlayerStateMachine. It does
/// NOT decide state itself - it reflects PlayerStateMachine.Current into Animator
/// parameters, and GATES actions through PlayerStateMachine.CanAct so things like
/// "attack during a ledge grab" are impossible.
///
/// Also fires procedural flourishes (e.g. a step-up hop) via ProceduralAnimator.
///
/// Lives on the model object (the Animator object), under the player root.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerAnimator : MonoBehaviour
{
    [Header("Tuning")]
    [SerializeField] private float speedDamp = 10f;
    [SerializeField] private float idleThreshold = 0.05f;
    [Tooltip("Run speed used to normalise Speed to 0..1 (set to PlayerMovement.runSpeed).")]
    [SerializeField] private float referenceRunSpeed = 8f;

    [Header("Action layer (masked upper body)")]
    [SerializeField] private int actionLayer = 1;
    [SerializeField] private float actionBlend = 12f;

    [Header("Procedural")]
    [Tooltip("Played as a small hop when an auto step-up happens. Optional.")]
    [SerializeField] private ProceduralClip stepUpHop;
    [Tooltip("Played once on landing from the air. Optional.")]
    [SerializeField] private ProceduralClip landDip;

    private Animator _animator;
    private PlayerMovement _pm;
    private PlayerStateMachine _sm;
    private ProceduralAnimator _proc;

    private float _smoothSpeed, _moveX, _moveY, _rollClipLength;
    private bool  _wasGrounded = true;
    private float _actionWeight;
    private int   _actionStateNoneHash;

    // Cached trigger hashes for item-specific attack animations.
    private static readonly int ShootHash        = Animator.StringToHash("Shoot");
    private static readonly int GrappleThrowHash = Animator.StringToHash("GrappleThrow");
    private static readonly int DeathHash        = Animator.StringToHash("Death");

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _animator.applyRootMotion = false;   // movement is 100% script-driven

        _pm = GetComponentInParent<PlayerMovement>();
        if (_pm == null) _pm = PlayerMovement.Instance;
        _sm = GetComponentInParent<PlayerStateMachine>();
        _proc = GetComponent<ProceduralAnimator>();

        _actionStateNoneHash = Animator.StringToHash("None");
        _rollClipLength = FindClipLength("Roll");

        var ledge = _pm != null ? _pm.GetComponent<PlayerLedgeGrab>() : null;
        if (ledge != null)
        {
            float climbLen = FindClipLength("Climbing");
            if (climbLen > 0.05f) ledge.SetClimbDuration(climbLen);
        }
    }

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
        EventBus.Subscribe<OnDieEvent>(OnDie);
        if (_sm != null) _sm.OnStateChanged += OnStateChanged;
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnAttackInputEvent>(OnAttack);
        EventBus.Unsubscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Unsubscribe<OnItemUnequipEvent>(OnItemUnequip);
        EventBus.Unsubscribe<OnDamagedEvent>(OnDamaged);
        EventBus.Unsubscribe<OnDieEvent>(OnDie);
        if (_sm != null) _sm.OnStateChanged -= OnStateChanged;
    }

    private void Update()
    {
        if (_pm == null || _animator == null) return;
        PlayerAnimState state = _sm != null ? _sm.Current : PlayerAnimState.Locomotion;

        Vector3 v = _pm.velocity;
        float planar = new Vector2(v.x, v.z).magnitude;
        if (planar < idleThreshold) planar = 0f;

        // ── Locomotion blend ────────────────────────────────────────────────
        float target = referenceRunSpeed > 0.01f ? planar / referenceRunSpeed : planar;
        _smoothSpeed = Mathf.Lerp(_smoothSpeed, target, speedDamp * Time.deltaTime);
        _animator.SetFloat(AnimParams.Speed, _smoothSpeed);

        bool grounded = state != PlayerAnimState.Airborne;
        _animator.SetBool(AnimParams.IsGrounded, grounded);
        _animator.SetBool(AnimParams.IsMoving, planar > 0f);
        _animator.SetFloat(AnimParams.VerticalSpeed, v.y);

        // ── State flags reflect PlayerStateMachine (single source of truth) ──
        _animator.SetBool(AnimParams.IsTargeting,     state == PlayerAnimState.Targeting);
        _animator.SetBool(AnimParams.IsDashing,       state == PlayerAnimState.Dash);
        _animator.SetBool(AnimParams.IsWallhugging,   state == PlayerAnimState.Wallhug);
        _animator.SetBool(AnimParams.IsLedgeGrabbing, state == PlayerAnimState.LedgeHang);
        _animator.SetBool(AnimParams.IsClimbingLedge, state == PlayerAnimState.Climb);

        // ── Strafe (only meaningful in Targeting) ───────────────────────────
        // Full analog 360-degree strafe: project world velocity onto local axes and
        // normalise so diagonal directions reach the corners of the blend tree (-1..1
        // on each axis) rather than being clamped to cardinal values.
        if (state == PlayerAnimState.Targeting)
        {
            Transform model = _pm.modelTransform != null ? _pm.modelTransform : transform;
            Vector3 pv   = new Vector3(v.x, 0f, v.z);
            float fwd    = Vector3.Dot(pv, model.forward);
            float side   = Vector3.Dot(pv, model.right);
            float norm   = Mathf.Max(_pm.moveSpeed, 0.01f);

            // Clamp individually so diagonals are expressed as e.g. (0.71, 0.71)
            // rather than being scaled down together — this lets the blend tree
            // show correct diagonal animations at full blend weight.
            float targetY = Mathf.Clamp(fwd  / norm, -1f, 1f);
            float targetX = Mathf.Clamp(side / norm, -1f, 1f);

            _moveY = Mathf.Lerp(_moveY, targetY, speedDamp * Time.deltaTime);
            _moveX = Mathf.Lerp(_moveX, targetX, speedDamp * Time.deltaTime);
        }
        else
        {
            _moveX = Mathf.Lerp(_moveX, 0f, speedDamp * Time.deltaTime);
            _moveY = Mathf.Lerp(_moveY, 0f, speedDamp * Time.deltaTime);
        }
        _animator.SetFloat(AnimParams.MoveX, _moveX);
        _animator.SetFloat(AnimParams.MoveY, _moveY);

        // ── Roll playback speed (whole clip fits in the dash) ───────────────
        float rollSpeed = 1f;
        if (_pm.Dash != null && _rollClipLength > 0.01f)
            rollSpeed = Mathf.Clamp(_rollClipLength / Mathf.Max(_pm.Dash.dashDuration, 0.01f), 0.25f, 6f);
        _animator.SetFloat(AnimParams.RollSpeed, rollSpeed);

        // ── Landing dip (procedural) ────────────────────────────────────────
        if (grounded && !_wasGrounded)
        {
            _animator.SetTrigger(AnimParams.Land);
            if (landDip != null && _proc != null) _proc.Play(landDip);
        }
        _wasGrounded = grounded;

        UpdateActionLayerWeight();
    }

    private void UpdateActionLayerWeight()
    {
        if (actionLayer < 0 || actionLayer >= _animator.layerCount) return;
        var info = _animator.GetCurrentAnimatorStateInfo(actionLayer);
        bool inAction = info.shortNameHash != _actionStateNoneHash || _animator.IsInTransition(actionLayer);
        _actionWeight = Mathf.MoveTowards(_actionWeight, inAction ? 1f : 0f, actionBlend * Time.deltaTime);
        _animator.SetLayerWeight(actionLayer, _actionWeight);
    }

    // ── State change reactions (procedural flourishes) ──────────────────────
    private void OnStateChanged(PlayerAnimState from, PlayerAnimState to)
    {
        if (to == PlayerAnimState.StepUp && stepUpHop != null && _proc != null)
            _proc.Play(stepUpHop);
    }

    // ── Action input - GATED by the state machine ───────────────────────────
    private void OnAttack(OnAttackInputEvent e)
    {
        if (!e.pressed) return;
        // Only act in grounded locomotion / targeting states.
        if (_sm != null && !_sm.CanAct) return;

        var item = EquipHandler.Instance != null ? EquipHandler.Instance.EquipedItem : null;

        if (item is SOGrappleHook)
        {
            // GrappleHook throw uses its own dedicated trigger.
            _animator.SetTrigger(GrappleThrowHash);
        }
        else if (item is SOSlingShot)
        {
            // Ranged shot replaces the melee Attack trigger.
            _animator.SetTrigger(ShootHash);
        }
        else if (item is SOWeapon)
        {
            _animator.SetTrigger(AnimParams.Attack);
        }
        else if (item is SOPotion)
        {
            _animator.SetTrigger(AnimParams.UseItem);
        }
    }

    private void OnItemEquip(OnItemEquipEvent e)     => _animator.SetBool(AnimParams.Armed, e.item is SOWeapon);
    private void OnItemUnequip(OnItemUnequipEvent e) => _animator.SetBool(AnimParams.Armed, false);

    private void OnDamaged(OnDamagedEvent e)
    {
        if (_pm != null && e.victim != _pm.gameObject) return;
        _animator.SetTrigger(AnimParams.Hit);
    }

    private void OnDie(OnDieEvent e)
    {
        if (_pm != null && e.murdered != _pm.gameObject) return;
        _animator.SetTrigger(DeathHash);
    }
}
