using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class RandomizerSystem : MonoBehaviour
{
    [SerializeField] private SOItemPool pool;
    [Tooltip("-1 = seed aleatoria cada run")]
    [SerializeField] private int seed = -1;

    private RandomizerState State => pool.state;

    // Cache de requirements durante la generación
    private Dictionary<string, List<SOItem>> _locationRequirements = new();

    // ─────────────────────────────────────────────
    // ENTRADA PÚBLICA
    // ─────────────────────────────────────────────

    /// <summary>
    /// Llama al inicio del juego. Si hay save previo lo restaura;
    /// si no, genera una seed nueva.
    /// </summary>
    public void LoadOrGenerate(List<string> locationIds, List<List<SOItem>> requirements)
    {
        CacheRequirements(locationIds, requirements);

        if (State.HasSave() && State.Load())
        {
            Debug.Log("[Randomizer] Run anterior restaurada ✓");
            return;
        }

        GenerateSeed(locationIds, requirements);
    }

    /// <summary>
    /// Borra el save actual y genera una run completamente nueva.
    /// </summary>
    public void NewGame(List<string> locationIds, List<List<SOItem>> requirements)
    {
        State.Clear();
        GenerateSeed(locationIds, requirements);
    }

    /// <summary>
    /// Versión rápida para escenas de prueba: auto-descubre todos los
    /// ChestBehaviour cargados en ese momento.
    /// </summary>
    public void GenerateSeedFromScene()
    {
        var chests = FindObjectsByType<ChestBehaviour>(FindObjectsSortMode.None).ToList();
        GenerateSeed(
            chests.Select(c => c.locationId).ToList(),
            chests.Select(c => c.requiredItems).ToList()
        );
    }

    // ─────────────────────────────────────────────
    // GENERACIÓN
    // ─────────────────────────────────────────────

    public void GenerateSeed(List<string> locationIds, List<List<SOItem>> requirements)
    {
        if (locationIds == null || locationIds.Count == 0)
        {
            Debug.LogError("[Randomizer] Lista de locationIds vacía.");
            return;
        }

        int usedSeed = seed == -1 ? Random.Range(0, int.MaxValue) : seed;
        Random.InitState(usedSeed);
        State.SetSeed(usedSeed);
        Debug.Log($"[Randomizer] Generando seed {usedSeed} con {locationIds.Count} cofres...");

        // Registra todos los cofres en el state
        State.Clear();
        foreach (var id in locationIds)
            State.Register(id);

        CacheRequirements(locationIds, requirements);

        var progression = pool.items
            .Where(e => e.isProgression)
            .OrderBy(e => GetTier(e.item))
            .ThenByDescending(e => e.priority)
            .Select(e => e.item)
            .ToList();

        var fill = pool.items
            .Where(e => !e.isProgression)
            .Select(e => e.item)
            .ToList();

        AssumedFill(progression);
        FillRemaining(fill);
        State.Save();

        if (ValidateSeed())
            Debug.Log("[Randomizer] ✓ Seed válida y guardada.");
        else
            Debug.LogError("[Randomizer] ✗ Seed inválida — revisa requiredItems y la pool.");
    }

    // ─────────────────────────────────────────────
    // ASSUMED FILL
    // ─────────────────────────────────────────────

    private void AssumedFill(List<SOItem> itemsToPlace)
    {
        var remaining = itemsToPlace
            .OrderBy(i => GetTier(i))
            .ThenBy(_ => Random.value)
            .ToList();

        foreach (var item in remaining)
        {
            var assumed = itemsToPlace
                .Where(i => i != item)
                .Select(i => i.itemName)
                .ToHashSet();

            int currentMaxTier = GetReachableTierSoFar(assumed);
            int itemTier       = GetTier(item);

            var reachable = State.chests
                .Where(c =>
                    string.IsNullOrEmpty(c.itemName) &&
                    IsAccessible(c.locationId, assumed) &&
                    IsTierProgressionValid(itemTier, currentMaxTier))
                .ToList();

            if (reachable.Count == 0)
            {
                Debug.LogError(
                    $"[Randomizer] Sin ubicación para '{item.itemName}' " +
                    $"(tier {itemTier}, maxTierActual={currentMaxTier}). " +
                    $"¿Suficientes cofres accesibles para ese tier?");
                return;
            }

            State.SetItem(reachable[Random.Range(0, reachable.Count)].locationId, item);
        }
    }

    private void FillRemaining(List<SOItem> fillItems)
    {
        var empty = State.chests.Where(c => string.IsNullOrEmpty(c.itemName)).ToList();
        Shuffle(fillItems);
        for (int i = 0; i < empty.Count && i < fillItems.Count; i++)
            State.SetItem(empty[i].locationId, fillItems[i]);
    }

    // ─────────────────────────────────────────────
    // VALIDACIÓN POST-GENERACIÓN
    // ─────────────────────────────────────────────

    public bool ValidateSeed()
    {
        var collected = new HashSet<string>();
        bool progress = true;

        while (progress)
        {
            progress = false;
            foreach (var chest in State.chests)
                if (!string.IsNullOrEmpty(chest.itemName) &&
                    IsAccessible(chest.locationId, collected) &&
                    collected.Add(chest.itemName))
                    progress = true;
        }

        return pool.items
            .Where(e => e.isRequired)
            .All(e => collected.Contains(e.item.itemName));
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    private void CacheRequirements(List<string> ids, List<List<SOItem>> reqs)
    {
        _locationRequirements = new();
        for (int i = 0; i < ids.Count; i++)
            _locationRequirements[ids[i]] = reqs[i];
    }

    private int GetReachableTierSoFar(HashSet<string> assumed) =>
        State.chests
            .Where(c => !string.IsNullOrEmpty(c.itemName) && IsAccessible(c.locationId, assumed))
            .Select(c => GetTier(pool.FindItem(c.itemName)))
            .DefaultIfEmpty(0)
            .Max();

    private bool IsAccessible(string locationId, HashSet<string> available)
    {
        if (!_locationRequirements.TryGetValue(locationId, out var reqs)) return true;
        return reqs == null || reqs.All(r => r != null && available.Contains(r.itemName));
    }

    private bool IsTierProgressionValid(int itemTier, int currentMaxTier)
    {
        if (itemTier == 0 || itemTier == 1) return true;
        return itemTier <= currentMaxTier + 1;
    }

    private int GetTier(SOItem item) => item is SOWeapon w ? w.tier : 0;

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
