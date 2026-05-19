using UnityEngine;
using System.Collections.Generic;
using System.IO;

[CreateAssetMenu(fileName = "RandomizerState", menuName = "Randomizer/State")]
public class RandomizerState : ScriptableObject
{
    [System.Serializable]
    public class ChestState
    {
        public string locationId;
        public string itemName;
        public bool opened;
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
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
        Debug.Log($"[RandomizerState] Guardado → {SavePath}");
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
            Debug.Log($"[RandomizerState] Cargado. Seed: {currentSeed} | Cofres: {chests.Count}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RandomizerState] Error al cargar: {e.Message}");
            return false;
        }
    }

    public void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
        Debug.Log("[RandomizerState] Save eliminado.");
    }

    public bool HasSave() => File.Exists(SavePath);

    // ─────────────────────────────────────────────
    // API DE ESTADO
    // ─────────────────────────────────────────────

    public void BuildCache()
    {
        _cache = new();
        foreach (var c in chests)
            _cache[c.locationId] = c;
    }

    public ChestState GetChest(string locationId)
    {
        if (_cache == null) BuildCache();
        _cache.TryGetValue(locationId, out var s);
        return s;
    }

    public void SetItem(string locationId, SOItem item)
    {
        if (_cache == null) BuildCache();
        if (_cache.TryGetValue(locationId, out var s))
        {
            s.itemName = item != null ? item.itemName : null;
            Save();
        }
    }

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
