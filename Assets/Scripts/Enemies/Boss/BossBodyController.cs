using UnityEngine;

/// <summary>
/// Controls the boss body — the large humanoid torso that anchors both fists.
///
/// Phase 1: body is invulnerable; fists are the only targets.
/// Phase 2: triggered when both fists have been defeated once; body becomes
///          vulnerable, fists respawn with increased speed, and fist slams
///          generate shockwaves.
///
/// Uses the <see cref="HealthSystem"/> component on this GameObject for HP.
/// Raises <see cref="OnBossDefeatedEvent"/> and records the kill in
/// <see cref="BossTracker"/> when the body dies.
/// </summary>
[RequireComponent(typeof(HealthSystem))]
public class BossBodyController : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Boss Identity")]
    [SerializeField] private string bossId = "boss_golem";

    [Header("Fists")]
    [SerializeField] private BossFistController leftFist;
    [SerializeField] private BossFistController rightFist;

    [Header("Movement")]
    [Tooltip("Degrees per second the body rotates to face the player.")]
    [SerializeField] private float rotateSpeed = 30f;

    [Header("Phase 2")]
    [Tooltip("How much faster the fists track in phase 2 (multiplied on respawn).")]
    [SerializeField] private float phase2SpeedMultiplier = 1.6f;

    // ── Runtime ──────────────────────────────────────────────────────────────

    private HealthSystem _health;
    private Transform    _player;
    private bool         _isPhase2;
    private bool         _dead;

    // Count how many fists have been defeated; phase 2 triggers at 2.
    private int _fistsDefeated;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _health = GetComponent<HealthSystem>();
    }

    private void Start()
    {
        if (PlayerMovement.Instance != null)
            _player = PlayerMovement.Instance.transform;

        // Phase 1: body should not accept damage via the global OnDamageDealtEvent.
        // We enforce this by subscribing to OnDamagedEvent and ignoring it until phase 2.
        // The HealthSystem's own OnDamageDealtEvent handler checks the target gameObject,
        // so we block it by making the body temporarily immune via a wrapper.
        SetBodyInvulnerable(true);
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnBossFistDefeatedEvent>(OnFistDefeated);
        EventBus.Subscribe<OnDieEvent>(OnDie);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnBossFistDefeatedEvent>(OnFistDefeated);
        EventBus.Unsubscribe<OnDieEvent>(OnDie);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_dead || _player == null) return;

        // Slowly rotate the body to face the player (Y axis only).
        Vector3 toPlayer = _player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.01f)
        {
            Quaternion target  = Quaternion.LookRotation(toPlayer);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotateSpeed * Time.deltaTime);
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnFistDefeated(OnBossFistDefeatedEvent e)
    {
        // Only count our own fists.
        if (e.fist != leftFist && e.fist != rightFist) return;

        _fistsDefeated++;
        if (_fistsDefeated >= 2 && !_isPhase2)
            EnterPhase2();
    }

    private void OnDie(OnDieEvent e)
    {
        if (e.murdered != gameObject || _dead) return;
        _dead = true;
        TriggerVictory();
    }

    // ── Phase 2 ───────────────────────────────────────────────────────────────

    private void EnterPhase2()
    {
        _isPhase2 = true;

        // Body becomes damageable.
        SetBodyInvulnerable(false);

        // Respawn both fists with phase 2 flags.
        RespawnFist(leftFist);
        RespawnFist(rightFist);

        Debug.Log("[BossBodyController] Phase 2 activated.");
    }

    private void RespawnFist(BossFistController fist)
    {
        if (fist == null) return;
        fist.IsPhase2 = true;
        fist.gameObject.SetActive(true);
    }

    // ── Invulnerability shim ──────────────────────────────────────────────────

    /// <summary>
    /// Toggles body invulnerability by subscribing/unsubscribing a cancellation
    /// handler that fires before the HealthSystem processes the damage event.
    /// Priority 10 ensures it runs before HealthSystem's subscription (priority 0).
    /// </summary>
    private bool _invulnHandlerRegistered;

    private void SetBodyInvulnerable(bool invulnerable)
    {
        if (invulnerable && !_invulnHandlerRegistered)
        {
            EventBus.Subscribe<OnDamageDealtEvent>(BlockDamageIfNotPhase2, priority: 10);
            _invulnHandlerRegistered = true;
        }
        else if (!invulnerable && _invulnHandlerRegistered)
        {
            EventBus.Unsubscribe<OnDamageDealtEvent>(BlockDamageIfNotPhase2);
            _invulnHandlerRegistered = false;
        }
    }

    private void BlockDamageIfNotPhase2(OnDamageDealtEvent e)
    {
        // Cancel only damage aimed at this body while in phase 1.
        if (e.Target == gameObject && !_isPhase2)
            EventBus.Cancel<OnDamageDealtEvent>();
    }

    // ── Victory ───────────────────────────────────────────────────────────────

    private void TriggerVictory()
    {
        // Persist the defeat.
        if (BossTracker.Instance != null)
            BossTracker.Instance.MarkBossDefeated(bossId);

        // Notify the rest of the game (cutscene triggers, door locks, etc.).
        EventBus.Raise(new OnBossDefeatedEvent { bossId = bossId });

        gameObject.SetActive(false);
    }
}
