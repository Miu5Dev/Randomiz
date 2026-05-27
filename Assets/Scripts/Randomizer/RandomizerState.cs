using UnityEngine;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// ScriptableObject holding the per-run randomizer state (seed + per-chest item
/// assignment + opened flag). Persists to disk as JSON in
/// <see cref="Application.persistentDataPath"/>.
///
/// Performance: <see cref="SetItem"/> is the hot path during seed generation
/// (called once per chest). It does NOT auto-save — callers must explicitly call
/// <see cref="Save"/> once at the end of the batch (RandomizerSystem already does
/// this). <see cref="SetOpened"/> still auto-saves because chest pickups are
/// infrequent user actions that should be persisted immediately.
/// </summary>
[CreateAssetMenu(fileName = "RandomizerState", menuName = "Randomizer/State")]
public class RandomizerState : ScriptableObject
{
    [System.Serializable]
    public class ChestState
    {
        public string locationId;
        public string itemName;
        public bool   opened;
    }

    [System.Serializable]
    private class SaveData
    {
        public int seed;
        public List<ChestState> chests = new();
    }

    public List<ChestState> chests = new();
    public int currentSeed { get; private set; }

    private Dictionary<string, ChestState> _cache;

    private static string SavePath =>
        Path.Combine(Application.persistentDataPath, "randomizer_state.json");

    // ─────────────────────────────────────────────
    // SAVE / LOAD
    // ─────────────────────────────────────────────

    public void Save()
    {
        var data = new SaveData { seed = currentSeed, chests = chests };
        // prettyPrint only in editor to keep runtime writes smaller/faster.
#if UNITY_EDITOR
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
#else
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: false));
#endif
    }

    public bool Load()
    {
        if (!File.Exists(SavePath)) return false;
        try
        {
            var data    = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            currentSeed = data.seed;
            chests      = data.chests;
            BuildCache();
            Debug.Log($"[RandomizerState] Loaded. Seed: {currentSeed} | Chests: {chests.Count}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RandomizerState] Load error: {e.Message}");
            return false;
        }
    }

    public void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }

    public bool HasSave() => File.Exists(SavePath);

    // ─────────────────────────────────────────────
    // STATE API
    // ─────────────────────────────────────────────

    public void BuildCache()
    {
        _cache = new Dictionary<string, ChestState>(chests.Count);
        foreach (var c in chests)
            _cache[c.locationId] = c;
    }

    public ChestState GetChest(string locationId)
    {
        if (string.IsNullOrEmpty(locationId)) return null;
        if (_cache == null) BuildCache();
        _cache.TryGetValue(locationId, out var s);
        return s;
    }

    /// <summary>
    /// Assigns an item to a chest. Does NOT auto-save — caller is expected to
    /// batch and call <see cref="Save"/> once after a generation pass.
    /// </summary>
    public void SetItem(string locationId, SOItem item)
    {
        if (_cache == null) BuildCache();
        if (_cache.TryGetValue(locationId, out var s))
            s.itemName = item != null ? item.itemName : null;
    }

    /// <summary>Marks a chest as opened and saves immediately (user-facing event).</summary>
    public void SetOpened(string locationId)
    {
        if (_cache == null) BuildCache();
        if (_cache.TryGetValue(locationId, out var s))
        {
            s.opened = true;
            Save();
        }
    }

    public ChestState Register(string locationId)
    {
        if (_cache == null) BuildCache();
        if (!_cache.ContainsKey(locationId))
        {
            var entry = new ChestState { locationId = locationId };
            chests.Add(entry);
            _cache[locationId] = entry;
        }
        return _cache[locationId];
    }

    public void Clear()
    {
        chests.Clear();
        _cache = null;
        DeleteSave();
    }

    public void SetSeed(int seed) => currentSeed = seed;

    public bool IsComplete =>
        chests.Count > 0 && chests.TrueForAll(c => !string.IsNullOrEmpty(c.itemName));
}
