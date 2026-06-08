using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime driver for one enemy part. Reads its <see cref="EnemyPartData"/> from
/// the shared <see cref="SOEnemy"/> asset and orchestrates three layers:
///
///   • Perception   Unaware → Engaged → LostSight (vision cone + alert radius).
///   • Phases        macro behaviour bands, advanced by exit conditions.
///   • Decisions     micro weighted-random state picks inside the active phase.
///
/// Movement / attack logic itself lives in pluggable ScriptableObjects, so this
/// class never hard-codes how a particular enemy behaves.
/// </summary>
[RequireComponent(typeof(HealthSystem))]
[RequireComponent(typeof(PhysicsController))]
public class EnemyController : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private SOEnemy data;
    [Tooltip("Which part of the SOEnemy this GameObject represents.")]
    [SerializeField] private int partIndex = 0;

    [Header("Navigation")]
    [Tooltip("Upward speed applied while climbing a wall (Can Climb).")]
    [SerializeField] private float climbSpeed = 4f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundedGravity = -2f;

    [Header("Reactions")]
    [Tooltip("If the player attacks within this distance, the enemy re-decides immediately " +
             "(so a Dodge state weighted by Player Attacking can fire instantly).")]
    [SerializeField] private float reactDistance = 3.5f;

    [Header("Optional")]
    [Tooltip("Waypoints for Patrol movement. Leave empty for non-patrolling enemies.")]
    [SerializeField] private Transform[] patrolPoints;
    [Tooltip("Local offset of the 'eyes' used for line-of-sight checks.")]
    [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 1f, 0f);

    // ─── Runtime ─────────────────────────────────────────────────────────────
    private HealthSystem      health;
    private PhysicsController physics;
    private EnemyContext      ctx;
    private EnemyPartData     part;
    private bool              _dead;

    // Perception
    private bool  _engaged;
    private float _lostSightTimer;

    // Reactions
    private float _lastPlayerAttackTime = -999f;
    private bool  _reactNow;            // forces an immediate weighted re-decision
    private SOMovementPattern _reactionMove;
    private float _reactionTimer;

    // Phase / decision
    private int   _phaseIndex   = -1;
    private SOEnemyPhase _phase;
    private int   _stateIndex   = -1;
    private float _decisionTimer;
    private float[] _stateLastUsed;
    private int[]   _stateTimesUsed;

    public bool IsAlive => !_dead;
    public string PartName => part != null ? part.partName : name;

    /// <summary>
    /// Label of the currently active decision state, or an empty string when idle
    /// or uninitialized. Consumed by EnemyAnimator for parameter mapping.
    /// </summary>
    public string CurrentStateLabel
    {
        get
        {
            EnemyStateEntry s = CurrentState();
            return s != null ? s.label : string.Empty;
        }
    }

    /// <summary>
    /// Flat (XZ) speed the enemy is actually applying this frame.
    /// EnemyAnimator drives the Speed animator parameter with this value.
    /// </summary>
    public float CurrentSpeed
    {
        get
        {
            Vector3 v = ctx != null ? ctx.velocity : Vector3.zero;
            return new Vector2(v.x, v.z).magnitude;
        }
    }

    /// <summary>
    /// True when the active state has a non-None attack pattern assigned.
    /// Used by EnemyAnimator to drive the IsAttacking bool.
    /// </summary>
    public bool IsActivelyAttacking
    {
        get
        {
            EnemyStateEntry s = CurrentState();
            return s?.attack != null && !(s.attack is SOAttack_None);
        }
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        health  = GetComponent<HealthSystem>();
        physics = GetComponent<PhysicsController>();

        part = ResolvePart();

        ctx = new EnemyContext
        {
            controller    = this,
            self          = transform,
            physics       = physics,
            health        = health,
            spawnPosition = transform.position,
            patrolPoints  = patrolPoints,
        };

        if (part != null)
        {
            ctx.moveSpeed = part.moveSpeed;
            ctx.turnSpeed = part.turnSpeed;
            ctx.canFly    = part.canFly;
            ctx.canClimb  = part.canClimb;
            ctx.wallSteer = part.wallSteer;

            if (part.weapon != null)
                ctx.weapon = Instantiate(part.weapon);
        }
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnDieEvent>(OnDie);
        EventBus.Subscribe<OnAttackInputEvent>(OnPlayerAttack);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnDieEvent>(OnDie);
        EventBus.Unsubscribe<OnAttackInputEvent>(OnPlayerAttack);
    }

    private void Start()
    {
        if (PlayerMovement.Instance != null)
            ctx.player = PlayerMovement.Instance.transform;

        ctx.bossGroup = GetComponentInParent<BossGroup>();
        if (ctx.bossGroup != null && part != null)
            ctx.bossGroup.Register(part.partName, this);

        if (part != null)
            health.Initialize(part.maxHearts);
    }

    // ─── Main loop ───────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (_dead || part == null) return;

        ctx.deltaTime = Time.fixedDeltaTime;
        ctx.ground    = physics.Ground;

        UpdatePerception();

        if (_engaged)
        {
            if (_reactionTimer > 0f) TickReaction();
            else                     TickEngaged();
        }
        else
        {
            TickIdle();
        }

        ApplyGravityAndNavigation();
        physics.Move(ctx.velocity * ctx.deltaTime);
    }

    // ─── Perception ──────────────────────────────────────────────────────────

    private void UpdatePerception()
    {
        bool canSee = CanPerceivePlayer();

        if (!_engaged)
        {
            if (canSee) Engage();
            return;
        }

        if (canSee)
        {
            _lostSightTimer = 0f;
        }
        else
        {
            _lostSightTimer += ctx.deltaTime;
            if (_lostSightTimer >= part.vision.loseSightDelay)
                Disengage();
        }
    }

    private bool CanPerceivePlayer()
    {
        // Lazy-resolve: handles player spawning after enemy Start().
        if (ctx.player == null && PlayerMovement.Instance != null)
            ctx.player = PlayerMovement.Instance.transform;
        if (ctx.player == null) return false;

        Vector3 eye   = transform.position + eyeOffset;
        Vector3 toPl  = ctx.player.position - eye;
        float   dist  = toPl.magnitude;

        // Hearing: sensed within the alert radius regardless of facing / LoS.
        if (dist <= part.vision.alertRadius) return true;

        if (dist > part.vision.range) return false;

        // Field-of-view cone (flat).
        Vector3 flatTo = new Vector3(toPl.x, 0f, toPl.z);
        Vector3 fwd    = new Vector3(transform.forward.x, 0f, transform.forward.z);
        if (flatTo.sqrMagnitude > 0.0001f && fwd.sqrMagnitude > 0.0001f)
        {
            float ang = Vector3.Angle(fwd, flatTo);
            if (ang > part.vision.angle * 0.5f) return false;
        }

        // Line of sight: a blocker between eye and player breaks perception.
        Vector3 target = ctx.player.position + Vector3.up * 0.5f;
        if (Physics.Linecast(eye, target, part.vision.blockMask, QueryTriggerInteraction.Ignore))
            return false;

        return true;
    }

    private void Engage()
    {
        _engaged = true;
        _lostSightTimer = 0f;
        EnterPhase(0);
    }

    private void Disengage()
    {
        _engaged = false;
        _phase = null;
        _phaseIndex = -1;
        _stateIndex = -1;
        _reactionTimer = 0f;
        _reactNow = false;
        _reactionMove = null;
        ctx.StopHorizontal();
        part.idleMovement?.Enter(ctx);
    }

    // ─── Idle (unaware) ──────────────────────────────────────────────────────

    private void TickIdle()
    {
        if (part.idleMovement != null)
        {
            part.idleMovement.Tick(ctx);
        }
        else
        {
            ctx.StopHorizontal();
        }
    }

    // ─── Reflex reaction (interrupts normal behaviour) ───────────────────────

    private void TickReaction()
    {
        _reactionTimer -= ctx.deltaTime;
        _reactionMove?.Tick(ctx);   // no attack during a dodge

        if (_reactionTimer <= 0f)
        {
            // Restore the interrupted state's movement/attack cleanly.
            EnemyStateEntry s = CurrentState();
            s?.movement?.Enter(ctx);
            s?.attack?.Enter(ctx);
        }
    }

    // ─── Engaged: phases + decisions ─────────────────────────────────────────

    private void TickEngaged()
    {
        if (_phase == null) { ctx.StopHorizontal(); return; }

        ctx.phaseTimer += ctx.deltaTime;
        ctx.stateTimer += ctx.deltaTime;
        _decisionTimer -= ctx.deltaTime;
        ctx.timeSincePlayerAttack = Time.time - _lastPlayerAttackTime;

        // Phase transition.
        if (_phase.exitCondition != null && _phase.exitCondition.IsTrue(ctx))
        {
            if (_phaseIndex + 1 < part.phases.Count)
            {
                EnterPhase(_phaseIndex + 1);
                return;
            }
        }

        // Decision: commit for at least duration.x, force a re-roll past duration.y.
        // A nearby player attack forces an immediate re-decision (reactive dodging).
        EnemyStateEntry current = CurrentState();
        bool minElapsed   = current == null || ctx.stateTimer >= current.duration.x;
        bool maxElapsed   = current != null && ctx.stateTimer >= current.duration.y;
        bool timeToDecide = _reactNow || (_decisionTimer <= 0f && minElapsed) || maxElapsed;
        _reactNow = false;

        if (timeToDecide && _phase.states.Count > 0)
        {
            PickState();
            _decisionTimer = Random.Range(_phase.decisionInterval.x, _phase.decisionInterval.y);
        }

        // Run the active state's behaviour.
        EnemyStateEntry state = CurrentState();
        state?.movement?.Tick(ctx);
        state?.attack?.Tick(ctx);
    }

    private void EnterPhase(int index)
    {
        _phaseIndex = index;
        _phase = (index >= 0 && index < part.phases.Count) ? part.phases[index] : null;
        ctx.phaseTimer = 0f;
        _stateIndex = -1;
        _decisionTimer = 0f;

        int count = _phase != null ? _phase.states.Count : 0;
        _stateLastUsed  = new float[count];
        _stateTimesUsed = new int[count];
        for (int i = 0; i < count; i++) _stateLastUsed[i] = -9999f;

        if (count > 0) PickState();
    }

    private void PickState()
    {
        float total = 0f;
        int   n = _phase.states.Count;
        // Reuse a scratch buffer to avoid per-decision allocation.
        EnsureWeightBuffer(n);

        for (int i = 0; i < n; i++)
        {
            ctx.evalTimeSinceUsed = Time.time - _stateLastUsed[i];
            ctx.evalTimesUsed     = _stateTimesUsed[i];
            float w = _phase.states[i].ResolveWeight(ctx, _phase.noise);
            _weights[i] = w;
            total += w;
        }

        int picked = 0;
        if (total > 0f)
        {
            float r = Random.Range(0f, total);
            for (int i = 0; i < n; i++)
            {
                r -= _weights[i];
                if (r <= 0f) { picked = i; break; }
            }
        }

        _stateTimesUsed[picked]++;
        _stateLastUsed[picked] = Time.time;

        if (picked != _stateIndex)
        {
            _stateIndex = picked;
            ctx.stateTimer = 0f;
            EnemyStateEntry s = _phase.states[picked];
            s.movement?.Enter(ctx);
            s.attack?.Enter(ctx);
        }
    }

    private float[] _weights;
    private void EnsureWeightBuffer(int n)
    {
        if (_weights == null || _weights.Length < n) _weights = new float[n];
    }

    private EnemyStateEntry CurrentState()
    {
        if (_phase == null || _stateIndex < 0 || _stateIndex >= _phase.states.Count) return null;
        return _phase.states[_stateIndex];
    }

    // ─── Movement post-processing ────────────────────────────────────────────

    private void ApplyGravityAndNavigation()
    {
        if (!ctx.canFly)
        {
            if (ctx.ground.isGrounded && ctx.velocity.y < 0f)
                ctx.velocity.y = groundedGravity;
            else
                ctx.velocity.y += gravity * ctx.deltaTime;
        }

        Vector3 horiz = new Vector3(ctx.velocity.x, 0f, ctx.velocity.z);
        if (horiz.sqrMagnitude < 0.0001f) return;

        float lookAhead = (physics.Collider != null ? physics.Collider.radius : 0.5f) + 0.15f;
        CollisionInfo wall = physics.CheckDirection(horiz.normalized, lookAhead);
        if (!wall.hit || !wall.IsWall(physics.maxGroundAngle)) return;

        if (ctx.canClimb)
        {
            ctx.velocity.y = Mathf.Max(ctx.velocity.y, climbSpeed);
        }
        else if (ctx.wallSteer)
        {
            Vector3 along = Vector3.ProjectOnPlane(horiz, wall.normal);
            if (along.sqrMagnitude > 0.01f)
            {
                along = along.normalized * horiz.magnitude;
                ctx.velocity.x = along.x;
                ctx.velocity.z = along.z;
            }
        }
    }

    // ─── Death ───────────────────────────────────────────────────────────────

    private void OnDie(OnDieEvent e)
    {
        if (e.murdered != gameObject || _dead) return;
        _dead = true;
        ctx.StopHorizontal();
        // Destroy instead of SetActive(false): avoids stale _dead on re-enable if pooled.
        Destroy(gameObject, 0.05f);
    }

    private void OnPlayerAttack(OnAttackInputEvent e)
    {
        if (!e.pressed || _dead || !_engaged || ctx.player == null) return;
        if (ctx.FlatDistanceToPlayer > reactDistance) return;

        _lastPlayerAttackTime = Time.time;   // feeds WMod_PlayerAttacking for weighted users
        _reactNow = true;                     // re-decide next FixedUpdate, bypassing min commit

        // Dedicated reflex dodge: a reliable interrupt, separate from the weighted system.
        if (part.dodgeReaction != null && _reactionTimer <= 0f && Random.value < part.dodgeChance)
        {
            _reactionMove  = part.dodgeReaction;
            _reactionTimer = Mathf.Max(0.05f, part.dodgeReactionTime);
            _reactionMove.Enter(ctx);
        }
    }

    private EnemyPartData ResolvePart()
    {
        if (data == null || data.parts == null || data.parts.Count == 0) return null;
        int i = Mathf.Clamp(partIndex, 0, data.parts.Count - 1);
        return data.parts[i];
    }

#if UNITY_EDITOR
    // ─── Gizmos ──────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        EnemyPartData p = part ?? ResolvePart();
        if (p == null) return;

        Vector3 eye = transform.position + eyeOffset;

        // Vision range (yellow sphere outline) + cone.
        Gizmos.color = new Color(1f, 0.92f, 0.16f, 0.6f);
        Gizmos.DrawWireSphere(eye, p.vision.range);

        Vector3 fwd = transform.forward;
        Quaternion left  = Quaternion.AngleAxis(-p.vision.angle * 0.5f, Vector3.up);
        Quaternion right = Quaternion.AngleAxis( p.vision.angle * 0.5f, Vector3.up);
        Gizmos.DrawRay(eye, left  * fwd * p.vision.range);
        Gizmos.DrawRay(eye, right * fwd * p.vision.range);

        // Alert radius (blue).
        Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, p.vision.alertRadius);

        // Weapon hitbox (red) when the weapon is a sword.
        if (p.weapon is SOSword sword)
        {
            Transform origin = transform;
            if (!string.IsNullOrEmpty(sword.hitboxOriginName))
            {
                Transform t = transform.Find(sword.hitboxOriginName);
                if (t != null) origin = t;
            }
            Vector3 center = origin.position + origin.forward * sword.hitboxReach;
            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.5f);
            Gizmos.matrix = Matrix4x4.TRS(center, origin.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, sword.hitboxSize);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
#endif
}
