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

        // Validar duplicados
        var duplicates = locationIds
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            Debug.LogError(
                $"[Randomizer] ✗ Duplicados detectados: {string.Join(", ", duplicates)}\n" +
                $"⚠️ Solución: Cambia el nombre de los GameObjects para que sean únicos.");
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
    // Agrupa items por tier para colocarlos en orden correcto
    var itemsByTier = new Dictionary<int, List<SOItem>>();
    foreach (var item in itemsToPlace)
    {
        int tier = GetTier(item);
        if (!itemsByTier.ContainsKey(tier))
            itemsByTier[tier] = new List<SOItem>();
        itemsByTier[tier].Add(item);
    }

    // Ordena tiers ascendente (0 → 1 → 2 → ...)
    var sortedTiers = itemsByTier.Keys.OrderBy(t => t).ToList();
    var placed = new HashSet<string>();  // items ya colocados

    foreach (var tier in sortedTiers)
    {
        var tierItems = itemsByTier[tier];
        
        // Baraja items del mismo tier para aleatoriedad
        Shuffle(tierItems);

        foreach (var item in tierItems)
        {
            var assumed = placed.ToHashSet();
            int currentMaxTier = GetReachableTierSoFar(assumed);
            int itemTier = GetTier(item);
            

            // Buscar cofres accesibles y válidos
            var reachable = State.chests
                .Where(c =>
                    string.IsNullOrEmpty(c.itemName) &&
                    IsAccessible(c.locationId, assumed) &&
                    IsTierProgressionValid(itemTier, currentMaxTier))
                .ToList();

            if (reachable.Count == 0)
            {
                // Intento de recuperación: fuerza accesibilidad si es Tier 0 o 1
                if (itemTier <= 1)
                {
                    var anyEmpty = State.chests
                        .Where(c => string.IsNullOrEmpty(c.itemName))
                        .FirstOrDefault();
                    
                    if (anyEmpty != null)
                    {
                        Debug.LogWarning($"[Randomizer] ⚠ No hay cofres accesibles para '{item.itemName}' " +
                                        $"(T{itemTier}), forzando colocación en {anyEmpty.locationId}");
                        State.SetItem(anyEmpty.locationId, item);
                        placed.Add(item.itemName);
                        continue;
                    }
                }

                Debug.LogError(
                    $"[Randomizer] ✗ Sin ubicación para '{item.itemName}' " +
                    $"(tier {itemTier}, maxTierActual={currentMaxTier}). " +
                    $"¿Suficientes cofres accesibles para ese tier? " +
                    $"Items colocados: {placed.Count}, Todavía por colocar: {itemsToPlace.Count - placed.Count}");
                
                // NO retornar temprano — continuar para intentar colocar los demás items
                continue;
            }

            State.SetItem(reachable[Random.Range(0, reachable.Count)].locationId, item);
            placed.Add(item.itemName);
        }
        
        
    }

        // Verificación final: ¿quedaron items sin colocar?
            var unplaced = itemsToPlace.Where(i => !placed.Contains(i.itemName)).ToList();
            if (unplaced.Count > 0)
            {
                Debug.LogWarning($"[Randomizer] ⚠ {unplaced.Count} items no se pudieron colocar: " +
                                 $"{string.Join(", ", unplaced.Select(i => i.itemName))}");
            }

        // ← AGREGA ESTO: Rellenar cofres vacíos con filler infinito
            var emptyChests = State.chests
                .Where(c => string.IsNullOrEmpty(c.itemName))
                .ToList();

            if (emptyChests.Count > 0)
            {
                if (pool.fillerItems.Count > 0)
                {
                    Debug.Log($"[Randomizer] ℹ️ {emptyChests.Count} cofres vacíos — rellenando con filler infinito");
                
                    foreach (var chest in emptyChests)
                    {
                        var filler = pool.fillerItems[Random.Range(0, pool.fillerItems.Count)];
                        State.SetItem(chest.locationId, filler);
                        Debug.Log($"[Randomizer] ✓ {chest.locationId} → {filler.itemName} (filler, tier {GetTier(filler)})");
                    }
                }
                else
                {
                    Debug.LogError(
                        $"[Randomizer] ✗ CRÍTICO: {emptyChests.Count} cofres vacíos y **fillerItems está vacío** en SOItemPool.\n" +
                        $"⚠️ Solución: Abre el SOItemPool y arrastra al menos 1 item a 'Filler items'");
                }
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
        // Tier 0 (pociones, llaves) y Tier 1 (espada inicial) siempre validan
        if (itemTier <= 1) return true;
    
        // Tier 2+ requiere maxTier actual >= tier - 1
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
