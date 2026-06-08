using UnityEngine;

/// <summary>
/// Drives the Animator on the enemy model by reading EnemyController state and
/// subscribing to health events. Attach on the same GameObject as EnemyController
/// (or on a child that contains the Animator).
///
/// Expected Animator parameters (create these in the Animator Controller):
///   Bool    IsMoving      — true while horizontal speed is above the movement threshold
///   Bool    IsAttacking   — true while the active decision state has a non-None attack
///   Bool    IsHurt        — true for a short window after taking damage
///   Bool    IsDead        — latched true on death
///   Float   Speed         — raw horizontal speed value from the physics context
///   Trigger AttackTrigger — fired once when the attack state first becomes active
///   Trigger HurtTrigger   — fired on every damage event
///   Trigger DeathTrigger  — fired once on death
/// </summary>
[RequireComponent(typeof(EnemyController))]
public class EnemyAnimator : MonoBehaviour
{
    // ─── Inspector ───────────────────────────────────────────────────────────

    [Header("Animator Target")]
    [Tooltip("Animator to drive. Auto-resolved from this GameObject or its children if left empty.")]
    [SerializeField] private Animator animator;

    [Header("Thresholds")]
    [Tooltip("Minimum horizontal speed (units/s) required to set IsMoving = true.")]
    [SerializeField] private float movementThreshold = 0.15f;
    [Tooltip("Seconds IsHurt stays true after a hit (visual window).")]
    [SerializeField] private float hurtDuration = 0.35f;

    [Header("Attack Detection")]
    [Tooltip("Minimum time the IsAttacking state must be false before AttackTrigger fires again. " +
             "Prevents re-triggering while the same attack loop continues.")]
    [SerializeField] private float attackRetriggerCooldown = 0.6f;

    // ─── Cached IDs ──────────────────────────────────────────────────────────

    // Bools
    private static readonly int IsMovingID    = Animator.StringToHash("IsMoving");
    private static readonly int IsAttackingID = Animator.StringToHash("IsAttacking");
    private static readonly int IsHurtID      = Animator.StringToHash("IsHurt");
    private static readonly int IsDeadID      = Animator.StringToHash("IsDead");
    // Float
    private static readonly int SpeedID       = Animator.StringToHash("Speed");
    // Triggers
    private static readonly int AttackTriggerID = Animator.StringToHash("AttackTrigger");
    private static readonly int HurtTriggerID   = Animator.StringToHash("HurtTrigger");
    private static readonly int DeathTriggerID  = Animator.StringToHash("DeathTrigger");

    // ─── Runtime ─────────────────────────────────────────────────────────────

    private EnemyController _controller;
    private HitFlash        _hitFlash;

    private bool  _wasAttacking;
    private float _attackRetriggerTimer;
    private float _hurtTimer;
    private bool  _dead;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        _controller = GetComponent<EnemyController>();
        _hitFlash   = GetComponent<HitFlash>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(includeInactive: true);

        if (animator == null)
            Debug.LogWarning($"[EnemyAnimator] No Animator found on {name} or its children.", this);
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
        if (animator == null || _controller == null) return;
        if (_dead) return;     // dead state is latched; nothing more to update

        UpdateMovement();
        UpdateAttack();
        UpdateHurt();
    }

    // ─── Update helpers ──────────────────────────────────────────────────────

    private void UpdateMovement()
    {
        float speed   = _controller.CurrentSpeed;
        bool  moving  = speed > movementThreshold;

        animator.SetFloat(SpeedID,     speed);
        animator.SetBool(IsMovingID,   moving);
    }

    private void UpdateAttack()
    {
        bool attacking = _controller.IsActivelyAttacking;

        animator.SetBool(IsAttackingID, attacking);

        // Tick the retrigger suppression window.
        if (_attackRetriggerTimer > 0f)
            _attackRetriggerTimer -= Time.deltaTime;

        // Fire AttackTrigger on the rising edge (off → on) and after cooldown.
        if (attacking && !_wasAttacking && _attackRetriggerTimer <= 0f)
        {
            animator.SetTrigger(AttackTriggerID);
            _attackRetriggerTimer = attackRetriggerCooldown;
        }

        _wasAttacking = attacking;
    }

    private void UpdateHurt()
    {
        if (_hurtTimer > 0f)
        {
            _hurtTimer -= Time.deltaTime;
            if (_hurtTimer <= 0f)
                animator.SetBool(IsHurtID, false);
        }
    }

    // ─── Event handlers ──────────────────────────────────────────────────────

    private void OnDamaged(OnDamagedEvent e)
    {
        if (e.victim != gameObject) return;
        if (_dead) return;

        // Drive hurt bool + trigger.
        _hurtTimer = hurtDuration;
        animator.SetBool(IsHurtID, true);
        animator.SetTrigger(HurtTriggerID);

        // HitFlash is already self-subscribing to OnDamagedEvent, but if a
        // caller needs to manually trigger it (e.g. from code), expose a path.
        // Nothing to do here — HitFlash handles its own event.
    }

    private void OnDie(OnDieEvent e)
    {
        if (e.murdered != gameObject) return;
        if (_dead) return;

        _dead = true;

        if (animator != null)
        {
            animator.SetBool(IsDeadID,    true);
            animator.SetBool(IsMovingID,  false);
            animator.SetBool(IsHurtID,    false);
            animator.SetBool(IsAttackingID, false);
            animator.SetTrigger(DeathTriggerID);
        }
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Scales the speed of the current Animator state. Useful for attack patterns
    /// that want to synchronise animation pace with their own cooldown values.
    /// </summary>
    public void SetAnimationSpeed(float speed)
    {
        if (animator != null)
            animator.speed = Mathf.Max(0f, speed);
    }

    /// <summary>
    /// Resets animator speed to 1 (convenience wrapper).
    /// </summary>
    public void ResetAnimationSpeed() => SetAnimationSpeed(1f);
}
