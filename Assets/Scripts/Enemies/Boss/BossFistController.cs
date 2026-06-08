using UnityEngine;

/// <summary>
/// Controls one fist sphere of the boss.
///
/// State machine (enum FistState):
///   Idle            — hovering near the body's hand socket, waiting for a decision.
///   TrackingPlayer  — slowly drifts toward the player's XZ position.
///   WindUp          — rises upward as a telegraph (1.5 s); plays particles + audio.
///   Slamming        — drops rapidly downward toward the ground.
///   Stunned         — landed on the ground; vulnerable to player melee for 3 s.
///   Dead            — fist has been destroyed (no further updates).
///
/// The fist is a kinematic Rigidbody moved with MovePosition so it integrates
/// cleanly with the physics engine without fighting gravity on its own.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class BossFistController : MonoBehaviour
{
    // ── Configuration ────────────────────────────────────────────────────────

    [Header("Tracking")]
    [Tooltip("XZ drift speed while tracking the player (m/s).")]
    [SerializeField] private float trackSpeed = 3f;
    [Tooltip("Height above the ground the fist hovers at in Idle/Tracking.")]
    [SerializeField] private float hoverHeight = 4f;

    [Header("Wind-up / Slam")]
    [Tooltip("How high above hover the fist rises during wind-up (m).")]
    [SerializeField] private float windUpRaise = 3f;
    [Tooltip("Seconds spent in the wind-up pose before slamming.")]
    [SerializeField] private float windUpDuration = 1.5f;
    [Tooltip("Downward speed during the slam (m/s).")]
    [SerializeField] private float slamSpeed = 22f;

    [Header("Stun")]
    [Tooltip("Seconds the fist is vulnerable after hitting the ground.")]
    [SerializeField] private float stunDuration = 3f;
    [Tooltip("Layer mask that counts as 'ground' for the slam collision.")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Phase 2")]
    [Tooltip("Set by BossBodyController when phase 2 begins.")]
    public bool IsPhase2;
    [Tooltip("Prefab spawned at impact position when in phase 2.")]
    [SerializeField] private BossShockwave shockwavePrefab;

    [Header("Health")]
    [Tooltip("Total fist HP (each unit = 1 damage).")]
    [SerializeField] private int maxHealth = 100;

    [Header("FX")]
    [SerializeField] private ParticleSystem chargeParticles;
    [SerializeField] private AudioSource    windupSound;

    [Header("Visual feedback")]
    [SerializeField] private Renderer fistRenderer;
    [SerializeField] private Color    normalColor  = Color.gray;
    [SerializeField] private Color    stunnedColor = Color.yellow;

    // ── Public state ─────────────────────────────────────────────────────────

    /// <summary>True while the fist is in the Stunned state and can be damaged.</summary>
    public bool DamagableByPlayer => _state == FistState.Stunned;

    // ── Runtime ──────────────────────────────────────────────────────────────

    private enum FistState { Idle, TrackingPlayer, WindUp, Slamming, Stunned, Dead }

    private FistState  _state       = FistState.Idle;
    private float      _stateTimer;
    private int        _currentHealth;
    private Rigidbody  _rb;
    private Collider   _collider;
    private Transform  _player;

    // Position the fist will target during the slam (locked at wind-up end).
    private Vector3 _slamTargetXZ;
    // Position the fist rose to at wind-up peak.
    private Vector3 _windUpPeakPos;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _rb              = GetComponent<Rigidbody>();
        _collider        = GetComponent<Collider>();
        _rb.isKinematic  = true;
        _rb.useGravity   = false;
        _currentHealth   = maxHealth;
    }

    private void Start()
    {
        if (PlayerMovement.Instance != null)
            _player = PlayerMovement.Instance.transform;

        ApplyColor(normalColor);
    }

    private void FixedUpdate()
    {
        if (_state == FistState.Dead) return;

        _stateTimer += Time.fixedDeltaTime;

        switch (_state)
        {
            case FistState.Idle:           TickIdle();      break;
            case FistState.TrackingPlayer: TickTracking();  break;
            case FistState.WindUp:         TickWindUp();    break;
            case FistState.Slamming:       TickSlamming();  break;
            case FistState.Stunned:        TickStunned();   break;
        }
    }

    // ── State ticks ──────────────────────────────────────────────────────────

    private void TickIdle()
    {
        // After a short idle pause, start tracking.
        if (_stateTimer > 1.5f)
            EnterState(FistState.TrackingPlayer);
    }

    private void TickTracking()
    {
        if (_player == null) return;

        // Drift toward player XZ, maintaining hover height.
        Vector3 target = new Vector3(_player.position.x, _player.position.y + hoverHeight, _player.position.z);
        Vector3 next   = Vector3.MoveTowards(transform.position, target, trackSpeed * Time.fixedDeltaTime);

        // Increase tracking speed in phase 2.
        float speed = IsPhase2 ? trackSpeed * 1.6f : trackSpeed;
        next = Vector3.MoveTowards(transform.position, target, speed * Time.fixedDeltaTime);

        _rb.MovePosition(next);

        // Once close enough to the player's XZ, wind up for a slam.
        float xzDist = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(_player.position.x,   _player.position.z));

        if (xzDist < 2.5f || _stateTimer > 4f)
            EnterState(FistState.WindUp);
    }

    private void TickWindUp()
    {
        // Smoothly rise to the peak position.
        Vector3 next = Vector3.MoveTowards(transform.position, _windUpPeakPos, 6f * Time.fixedDeltaTime);
        _rb.MovePosition(next);

        if (_stateTimer >= windUpDuration)
        {
            // Lock slam XZ on the player's current position.
            if (_player != null)
                _slamTargetXZ = new Vector3(_player.position.x, 0f, _player.position.z);
            EnterState(FistState.Slamming);
        }
    }

    private void TickSlamming()
    {
        // Plunge straight down (no more XZ correction — committed to the locked position).
        Vector3 next = transform.position + Vector3.down * slamSpeed * Time.fixedDeltaTime;
        _rb.MovePosition(next);
        // Ground detection is handled by OnCollisionEnter.
    }

    private void TickStunned()
    {
        if (_stateTimer >= stunDuration)
        {
            ExitStun();
            EnterState(FistState.Idle);
        }
    }

    // ── State transitions ────────────────────────────────────────────────────

    private void EnterState(FistState next)
    {
        ExitCurrentState();
        _state      = next;
        _stateTimer = 0f;

        switch (next)
        {
            case FistState.WindUp:
                _windUpPeakPos = new Vector3(
                    transform.position.x,
                    transform.position.y + windUpRaise,
                    transform.position.z);
                chargeParticles?.Play();
                windupSound?.Play();
                break;

            case FistState.Stunned:
                ApplyColor(stunnedColor);
                // Make the collider solid so sword hits register.
                _collider.isTrigger = false;
                EventBus.Raise(new OnBossFistStunnedEvent { isStunned = true, fist = this });
                break;

            case FistState.Idle:
                // Return to hover height above spawn/socket position.
                break;
        }
    }

    private void ExitCurrentState()
    {
        if (_state == FistState.WindUp)
        {
            chargeParticles?.Stop();
        }
    }

    private void ExitStun()
    {
        ApplyColor(normalColor);
        EventBus.Raise(new OnBossFistStunnedEvent { isStunned = false, fist = this });
    }

    // ── Collision ────────────────────────────────────────────────────────────

    private void OnCollisionEnter(Collision collision)
    {
        if (_state != FistState.Slamming) return;

        // Check if we hit the ground layer.
        if ((groundLayer.value & (1 << collision.gameObject.layer)) == 0) return;

        // Spawn shockwave in phase 2.
        if (IsPhase2 && shockwavePrefab != null)
        {
            Vector3 impactPos = collision.contacts[0].point;
            Instantiate(shockwavePrefab, impactPos, Quaternion.identity);
        }

        EnterState(FistState.Stunned);
    }

    // ── Damage (only while Stunned) ──────────────────────────────────────────

    /// <summary>
    /// Called externally (e.g. player melee hitbox) to damage this fist.
    /// Damage is only accepted in the Stunned state.
    /// </summary>
    public void TakeDamage(float amount, GameObject attacker)
    {
        if (_state != FistState.Stunned) return;
        if (amount <= 0f) return;

        _currentHealth -= Mathf.CeilToInt(amount);
        if (_currentHealth <= 0)
        {
            _currentHealth = 0;
            Die();
        }
    }

    private void Die()
    {
        _state = FistState.Dead;
        chargeParticles?.Stop();
        _collider.enabled = false;
        EventBus.Raise(new OnBossFistStunnedEvent { isStunned = false, fist = this });
        EventBus.Raise(new OnBossFistDefeatedEvent { fist = this });
        gameObject.SetActive(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ApplyColor(Color c)
    {
        if (fistRenderer != null)
            fistRenderer.material.color = c;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 1.5f);
    }
#endif
}
