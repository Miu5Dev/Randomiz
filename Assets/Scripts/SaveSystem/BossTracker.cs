using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks which bosses have been defeated in the current run. The list is the
/// in-memory source of truth; SaveManager reads it on save and repopulates it on
/// load. No file I/O happens here — persistence is owned by SaveManager.
/// </summary>
public class BossTracker : MonoBehaviour
{
    public static BossTracker Instance { get; private set; }

    private readonly HashSet<string> _defeated = new();

    /// <summary>Snapshot of defeated boss ids (new list — safe to mutate by callers).</summary>
    public List<string> DefeatedBosses => new List<string>(_defeated);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Marks a boss as defeated. Idempotent.</summary>
    public void MarkBossDefeated(string bossId)
    {
        if (string.IsNullOrEmpty(bossId)) return;
        _defeated.Add(bossId);
    }

    public bool IsBossDefeated(string bossId) =>
        !string.IsNullOrEmpty(bossId) && _defeated.Contains(bossId);

    /// <summary>Replaces the tracked set wholesale. Used by SaveManager on load.</summary>
    public void RestoreFrom(IEnumerable<string> bossIds)
    {
        _defeated.Clear();
        if (bossIds == null) return;
        foreach (var id in bossIds)
            if (!string.IsNullOrEmpty(id))
                _defeated.Add(id);
    }

    /// <summary>Clears all tracked bosses (e.g. when starting a new game).</summary>
    public void Clear() => _defeated.Clear();
}
