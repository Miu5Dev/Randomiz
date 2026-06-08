using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One wave the crystal spawns: a set of enemy types with a count each.
/// </summary>
[System.Serializable]
public class BossWave
{
    [Tooltip("Enemy types to spawn this wave (each must have a prefab on its SOEnemy).")]
    public SOEnemy[] enemyTypes;
    [Tooltip("How many of each type. Index-matched to Enemy Types; defaults to 1 if shorter.")]
    public int[] counts;
}

/// <summary>
/// Crystal boss. Floats out of reach and spawns waves of enemies; while it has live
/// adds it is invulnerable. Clear the wave and the crystal <b>falls</b> to a weakened,
/// vulnerable state for <see cref="weakenedDuration"/> seconds — hit it directly here.
/// If it survives the window it rises, becomes invulnerable again and spawns the next
/// (optionally larger) wave. Repeat until its HP reaches zero.
///
/// HP lives on the required <see cref="HealthSystem"/>. Damage is gated by the same
/// priority-10 cancellation shim used by <see cref="BossBodyController"/>: while not
/// weakened, any <see cref="OnDamageDealtEvent"/> aimed at this object is cancelled
/// before the HealthSystem (priority 0) can process it.
///
/// IMPORTANT: for the player's melee to land, the crystal's root must carry a solid
/// Collider on a layer included in the sword's target mask (see <c>SOSword.targetLayers</c>).
/// </summary>
[RequireComponent(typeof(HealthSystem))]
[RequireComponent(typeof(Collider))]
public class CrystalBossController : MonoBehaviour, IBossEncounter
{
    // ── Identity ──────────────────────────────────────────────────────────────
    [Header("Identity")]
    [SerializeField] private string bossId   = "boss_crystal";
    [SerializeField] private string bossName = "The Crystal";

    // ── Waves ─────────────────────────────────────────────────────────────────
    [Header("Waves")]
    [Tooltip("Waves spawned in order. Once the last is reached it repeats (with scaling).")]
    [SerializeField] private List<BossWave> waves = new();
    [Tooltip("Extra enemies added per type each time a wave cycle is survived.")]
    [SerializeField] private int countScalingPerCycle = 1;
    [Tooltip("Seconds between individual enemy spawns within a wave.")]
    [SerializeField] private float spawnDelay = 0.4f;

    [Header("Spawn placement")]
    [Tooltip("Where adds appear. If empty, they spawn on a ring around the crystal.")]
    [SerializeField] private Transform[] spawnPoints;
    [Tooltip("Ring radius used when no spawn points are assigned.")]
    [SerializeField] private float spawnRadius = 6f;

    // ── Weakened window ─────────────────────────────────────────────────────────
    [Header("Weakened window")]
    [Tooltip("Seconds the crystal stays fallen & vulnerable after a wave is cleared.")]
    [SerializeField] private float weakenedDuration = 6f;

    // ── Motion ──────────────────────────────────────────────────────────────────
    [Header("Motion (height above placed position)")]
    [Tooltip("Hover height while spawning / invulnerable.")]
    [SerializeField] private float floatHeight = 3.5f;
    [Tooltip("Height while weakened — low enough for the player to melee it.")]
    [SerializeField] private float weakenedHeight = 0.8f;
    [SerializeField] private float riseSpeed    = 4f;
    [SerializeField] private float descendSpeed = 6f;

    // ── Feedback ────────────────────────────────────────────────────────────────
    [Header("Visual feedback")]
    [SerializeField] private Renderer crystalRenderer;
    [SerializeField] private Color normalColor   = new Color(0.4f, 0.7f, 1f);
    [SerializeField] private Color weakenedColor  = new Color(1f, 0.55f, 0.2f);
    [SerializeField] private ParticleSystem weakenedParticles;
    [SerializeField] private AudioSource    weakenedSound;

    // ── IBossEncounter ──────────────────────────────────────────────────────────
    public string BossId   => bossId;
    public string BossName => bossName;

    // ── Runtime ──────────────────────────────────────────────────────────────────
    private enum State { Dormant, Spawning, WaitingForClear, Weakened, Dead }

    private readonly List<EnemyController> _liveEnemies = new();
    private State     _state = State.Dormant;
    private bool      _started;
    private bool      _vulnerable;
    private int       _cycle;
    private float     _weakenedTimer;
    private float     _baseY, _floatY, _weakY;
    private Coroutine _spawnRoutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        ApplyColor(normalColor);
    }

    private void OnEnable()
    {
        // Priority 10 runs before HealthSystem (priority 0) so it can veto the hit.
        EventBus.Subscribe<OnDamageDealtEvent>(BlockDamageWhileInvulnerable, priority: 10);
        EventBus.Subscribe<OnDieEvent>(OnDie);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnDamageDealtEvent>(BlockDamageWhileInvulnerable);
        EventBus.Unsubscribe<OnDieEvent>(OnDie);
    }

    // ── IBossEncounter ──────────────────────────────────────────────────────────

    /// <summary>Starts the fight. Called by <see cref="BossArea"/>. Idempotent.</summary>
    public void BeginEncounter()
    {
        if (_started) return;
        _started = true;

        _baseY  = transform.position.y;
        _floatY = _baseY + floatHeight;
        _weakY  = _baseY + weakenedHeight;
        _cycle  = 0;

        EnterSpawning();
    }

    // ── Update ────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_state == State.Dormant || _state == State.Dead) return;

        UpdateHeight();

        if (_state == State.WaitingForClear)
        {
            _liveEnemies.RemoveAll(e => e == null || !e.IsAlive);
            if (_liveEnemies.Count == 0)
                EnterWeakened();
        }
        else if (_state == State.Weakened)
        {
            _weakenedTimer += Time.deltaTime;
            if (_weakenedTimer >= weakenedDuration)
                SurviveWeakened();
        }
    }

    // Float toward the height appropriate for the current state.
    private void UpdateHeight()
    {
        float targetY = _state == State.Weakened ? _weakY : _floatY;
        float speed   = _state == State.Weakened ? descendSpeed : riseSpeed;

        Vector3 p = transform.position;
        p.y = Mathf.MoveTowards(p.y, targetY, speed * Time.deltaTime);
        transform.position = p;
    }

    // ── States ────────────────────────────────────────────────────────────────────

    private void EnterSpawning()
    {
        _state = State.Spawning;
        ApplyColor(normalColor);

        BossWave wave = (waves != null && waves.Count > 0)
            ? waves[Mathf.Min(_cycle, waves.Count - 1)]
            : null;
        int extraPerType = Mathf.Max(0, countScalingPerCycle) * _cycle;

        if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
        _spawnRoutine = StartCoroutine(SpawnWaveRoutine(wave, extraPerType));
    }

    private IEnumerator SpawnWaveRoutine(BossWave wave, int extraPerType)
    {
        _liveEnemies.Clear();

        // Build the spawn queue from the wave's types × counts (+ scaling).
        var queue = new List<SOEnemy>();
        if (wave?.enemyTypes != null)
        {
            for (int i = 0; i < wave.enemyTypes.Length; i++)
            {
                SOEnemy type = wave.enemyTypes[i];
                if (type == null) continue;
                int baseCount = (wave.counts != null && i < wave.counts.Length) ? wave.counts[i] : 1;
                int count = Mathf.Max(0, baseCount + extraPerType);
                for (int j = 0; j < count; j++) queue.Add(type);
            }
        }

        foreach (SOEnemy type in queue)
        {
            SpawnEnemy(type);
            yield return new WaitForSeconds(spawnDelay);
        }

        _spawnRoutine = null;

        // Wave fully spawned — now wait for the player to clear it.
        if (_state == State.Spawning)
            _state = State.WaitingForClear;
    }

    private void SpawnEnemy(SOEnemy type)
    {
        GameObject prefab = type.prefab;
        if (prefab == null)
        {
            Debug.LogWarning($"[CrystalBoss] Enemy type '{type.name}' has no prefab assigned.", this);
            return;
        }

        GameObject instance = Instantiate(prefab, PickSpawnPos(), Quaternion.identity);
        var controller = instance.GetComponent<EnemyController>();
        if (controller != null) _liveEnemies.Add(controller);
        else Debug.LogWarning($"[CrystalBoss] Spawned '{instance.name}' has no EnemyController.", instance);
    }

    private Vector3 PickSpawnPos()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform p = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (p != null) return p.position;
        }

        // Ring around the crystal at the placed (ground) height.
        Vector2 dir = Random.insideUnitCircle.normalized;
        float dist  = Random.Range(spawnRadius * 0.5f, spawnRadius);
        return new Vector3(transform.position.x + dir.x * dist, _baseY, transform.position.z + dir.y * dist);
    }

    private void EnterWeakened()
    {
        _state         = State.Weakened;
        _weakenedTimer = 0f;
        _vulnerable    = true;

        ApplyColor(weakenedColor);
        weakenedParticles?.Play();
        weakenedSound?.Play();
        EventBus.Raise(new OnBossWeakenedEvent { weakened = true });
    }

    // Window elapsed without dying → rise and send the next wave.
    private void SurviveWeakened()
    {
        _vulnerable = false;
        ApplyColor(normalColor);
        weakenedParticles?.Stop();
        EventBus.Raise(new OnBossWeakenedEvent { weakened = false });

        _cycle++;
        EnterSpawning();
    }

    // ── Damage / death ──────────────────────────────────────────────────────────

    // Cancels any hit aimed at this crystal while it is NOT weakened.
    private void BlockDamageWhileInvulnerable(OnDamageDealtEvent e)
    {
        if (e.Target == gameObject && !_vulnerable)
            EventBus.Cancel<OnDamageDealtEvent>();
    }

    private void OnDie(OnDieEvent e)
    {
        if (e.murdered != gameObject || _state == State.Dead) return;
        Die();
    }

    private void Die()
    {
        _state      = State.Dead;
        _vulnerable = false;

        if (_spawnRoutine != null) { StopCoroutine(_spawnRoutine); _spawnRoutine = null; }
        weakenedParticles?.Stop();
        EventBus.Raise(new OnBossWeakenedEvent { weakened = false });

        if (BossTracker.Instance != null)
            BossTracker.Instance.MarkBossDefeated(bossId);

        EventBus.Raise(new OnBossDefeatedEvent { bossId = bossId });
        EventBus.Raise(new OnBossEncounterEndedEvent { bossId = bossId, defeated = true });

        gameObject.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private void ApplyColor(Color c)
    {
        if (crystalRenderer != null)
            crystalRenderer.material.color = c;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Spawn ring.
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);

        // Assigned spawn points.
        if (spawnPoints != null)
        {
            Gizmos.color = new Color(0f, 0.6f, 1f, 0.9f);
            foreach (Transform p in spawnPoints)
                if (p != null) Gizmos.DrawWireSphere(p.position, 0.4f);
        }
    }
#endif
}
