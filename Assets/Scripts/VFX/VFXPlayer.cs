using System.Collections;
using UnityEngine;

/// <summary>
/// Singleton MonoBehaviour that bridges EventBus events to the VFX/SFX system.
/// Place once in the scene and assign a <see cref="SOVFXProfile"/> asset.
/// Other systems can also call <see cref="Play"/> directly without going
/// through the EventBus.
/// </summary>
public class VFXPlayer : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static VFXPlayer Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Profile")]
    [SerializeField] private SOVFXProfile profile;

    [Header("Audio")]
    [Tooltip("Shared AudioSource used for one-shot SFX. " +
             "If left null one will be created automatically.")]
    [SerializeField] private AudioSource sfxSource;

    [Header("Footstep Timing")]
    [Tooltip("Minimum horizontal speed (units/s) before footstep sounds trigger.")]
    [SerializeField] private float stepMinSpeed = 1.5f;
    [Tooltip("Interval between footstep sounds at walk speed (seconds).")]
    [SerializeField] private float stepIntervalWalk = 0.5f;
    [Tooltip("Interval between footstep sounds when running.")]
    [SerializeField] private float stepIntervalRun = 0.3f;
    [Tooltip("Horizontal speed above which the run interval is used.")]
    [SerializeField] private float runSpeedThreshold = 5f;

    // ── Private state ─────────────────────────────────────────────────────────
    private Coroutine _stepCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        // Singleton enforcement — destroy duplicate instances.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Ensure an AudioSource is available for one-shot playback.
        if (sfxSource == null)
            sfxSource = gameObject.AddComponent<AudioSource>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnDamagedEvent>(OnDamaged);
        EventBus.Subscribe<OnDieEvent>(OnDie);
        EventBus.Subscribe<OnAttackInputEvent>(OnAttackInput);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnDamagedEvent>(OnDamaged);
        EventBus.Unsubscribe<OnDieEvent>(OnDie);
        EventBus.Unsubscribe<OnAttackInputEvent>(OnAttackInput);

        if (_stepCoroutine != null)
        {
            StopCoroutine(_stepCoroutine);
            _stepCoroutine = null;
        }
    }

    private void Start()
    {
        // Start the footstep polling loop; it self-terminates when the
        // PlayerMovement singleton is not found.
        _stepCoroutine = StartCoroutine(FootstepLoop());
    }

    // ── EventBus handlers ─────────────────────────────────────────────────────

    private void OnDamaged(OnDamagedEvent e)
    {
        // Play the take-damage effect at the victim's world position.
        Vector3 pos = e.victim != null
            ? e.victim.transform.position
            : Vector3.zero;

        Play(SOVFXProfile.TAKE_DAMAGE, pos);
    }

    private void OnDie(OnDieEvent e)
    {
        Vector3 pos = e.murdered != null
            ? e.murdered.transform.position
            : Vector3.zero;

        // Choose between player death and enemy death based on tag.
        bool isPlayer = e.murdered != null && e.murdered.CompareTag("Player");
        Play(isPlayer ? SOVFXProfile.DEATH : SOVFXProfile.ENEMY_DEATH, pos);
    }

    private void OnAttackInput(OnAttackInputEvent e)
    {
        // Only trigger on button press, not on release.
        if (!e.pressed) return;

        Vector3 pos = PlayerMovement.Instance != null
            ? PlayerMovement.Instance.transform.position
            : Vector3.zero;

        Play(SOVFXProfile.ATTACK_SWING, pos);
    }

    // ── Footstep coroutine ────────────────────────────────────────────────────

    /// <summary>
    /// Polls <see cref="PlayerMovement.Instance"/> velocity and fires footstep
    /// sounds at a rate proportional to the player's horizontal speed.
    /// </summary>
    private IEnumerator FootstepLoop()
    {
        while (true)
        {
            PlayerMovement pm = PlayerMovement.Instance;
            if (pm == null)
            {
                // Wait for the player to appear in the scene.
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            // Only play footsteps when grounded and moving fast enough.
            Vector3 flatVel = new Vector3(pm.velocity.x, 0f, pm.velocity.z);
            float   speed   = flatVel.magnitude;

            if (speed >= stepMinSpeed && pm.IsGrounded)
            {
                Play(SOVFXProfile.WALK_STEP, pm.transform.position);

                float interval = speed >= runSpeedThreshold
                    ? stepIntervalRun
                    : stepIntervalWalk;

                yield return new WaitForSeconds(interval);
            }
            else
            {
                yield return null; // Check every frame when idle/airborne.
            }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Looks up <paramref name="actionName"/> in the active profile, spawns the
    /// VFX prefab at <paramref name="position"/> and plays the SFX clip.
    /// Safe to call with a null/missing profile — it will log a warning once and
    /// return silently.
    /// </summary>
    /// <param name="actionName">One of the <see cref="SOVFXProfile"/> action-name constants.</param>
    /// <param name="position">World-space spawn point for the VFX.</param>
    /// <param name="parent">Optional transform to parent the VFX to (overrides attachToSource=false).</param>
    public static void Play(string actionName, Vector3 position, Transform parent = null)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[VFXPlayer] Play called but no VFXPlayer instance exists in the scene.");
            return;
        }

        Instance.PlayInternal(actionName, position, parent);
    }

    private void PlayInternal(string actionName, Vector3 position, Transform parent)
    {
        if (profile == null)
        {
            Debug.LogWarning("[VFXPlayer] No SOVFXProfile assigned.", this);
            return;
        }

        VFXBinding binding = profile.GetBinding(actionName);
        if (binding == null) return; // No binding configured — silent skip.

        // ── Spawn VFX ─────────────────────────────────────────────────────────
        if (binding.vfxPrefab != null)
        {
            Transform resolvedParent = (binding.attachToSource && parent != null) ? parent : null;

            GameObject vfxInstance = resolvedParent != null
                ? Instantiate(binding.vfxPrefab, position, Quaternion.identity, resolvedParent)
                : Instantiate(binding.vfxPrefab, position, Quaternion.identity);

            // Auto-destroy if a positive duration is set.
            if (binding.duration > 0f)
                Destroy(vfxInstance, binding.duration);
        }

        // ── Play SFX ──────────────────────────────────────────────────────────
        if (binding.sfxClip != null && sfxSource != null)
            sfxSource.PlayOneShot(binding.sfxClip, binding.sfxVolume);
    }

    // ── Convenience helpers (for external callers that want typed helpers) ────

    /// <summary>Plays the LAND effect at the given position.</summary>
    public static void PlayLand(Vector3 position)    => Play(SOVFXProfile.LAND, position);

    /// <summary>Plays the DASH effect at the given position.</summary>
    public static void PlayDash(Vector3 position)    => Play(SOVFXProfile.DASH, position);

    /// <summary>Plays the LEDGE_GRAB effect at the given position.</summary>
    public static void PlayLedgeGrab(Vector3 position) => Play(SOVFXProfile.LEDGE_GRAB, position);

    /// <summary>Plays the LEDGE_CLIMB effect at the given position.</summary>
    public static void PlayLedgeClimb(Vector3 position) => Play(SOVFXProfile.LEDGE_CLIMB, position);

    /// <summary>Plays the WALLHUG_ENTER effect at the given position.</summary>
    public static void PlayWallhugEnter(Vector3 position) => Play(SOVFXProfile.WALLHUG_ENTER, position);

    /// <summary>Plays the ATTACK_HIT effect at the given position (call from hit-detection code).</summary>
    public static void PlayAttackHit(Vector3 position) => Play(SOVFXProfile.ATTACK_HIT, position);

    /// <summary>Plays the ITEM_PICKUP effect at the given position.</summary>
    public static void PlayItemPickup(Vector3 position) => Play(SOVFXProfile.ITEM_PICKUP, position);

    /// <summary>Plays the CHECKPOINT_ACTIVATE effect at the given position.</summary>
    public static void PlayCheckpoint(Vector3 position) => Play(SOVFXProfile.CHECKPOINT_ACTIVATE, position);

    /// <summary>Plays the BOSS_SLAM effect at the given position.</summary>
    public static void PlayBossSlam(Vector3 position) => Play(SOVFXProfile.BOSS_SLAM, position);
}
