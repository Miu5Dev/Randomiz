using UnityEngine;

/// <summary>
/// Tracks elapsed real time and player death count for the current run.
/// Timer starts when the scene loads (Awake) and freezes when the boss is defeated.
/// Attach to any persistent GameObject in the gameplay scene (wizard adds it to GameSystems).
/// </summary>
public class RunTracker : MonoBehaviour
{
    public static RunTracker Instance { get; private set; }

    private float _startTime;
    private float _finalTime;
    private bool  _running;

    public int   DeathCount     { get; private set; }
    public float ElapsedSeconds => _running ? Time.realtimeSinceStartup - _startTime : _finalTime;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance   = this;
        _startTime = Time.realtimeSinceStartup;
        _running   = true;
        DeathCount = 0;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnDieEvent>(OnPlayerDied);
        EventBus.Subscribe<OnBossEncounterEndedEvent>(OnBossEncounterEnded);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnDieEvent>(OnPlayerDied);
        EventBus.Unsubscribe<OnBossEncounterEndedEvent>(OnBossEncounterEnded);
    }

    private void OnPlayerDied(OnDieEvent e)
    {
        if (!IsPlayer(e?.murdered)) return;
        DeathCount++;
    }

    private void OnBossEncounterEnded(OnBossEncounterEndedEvent e)
    {
        if (!e.defeated || !_running) return;
        _finalTime = Time.realtimeSinceStartup - _startTime;
        _running   = false;
    }

    private static bool IsPlayer(GameObject go)
    {
        if (go == null) return false;
        if (PlayerMovement.Instance != null && go == PlayerMovement.Instance.gameObject) return true;
        return go.CompareTag("Player");
    }
}
