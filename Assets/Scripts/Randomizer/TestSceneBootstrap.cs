using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Attach this to a GameObject in your test scene.
/// Calls GenerateSeedFromScene() on Start so you can test
/// without writing any extra code.
/// </summary>
public class TestSceneBootstrap : MonoBehaviour
{
    [SerializeField] private RandomizerSystem randomizerSystem;

    [Header("Test Controls")]
    [Tooltip("Si true, borra el save y genera seed nueva aunque haya save previo")]
    [SerializeField] private bool forceNewSeed = false;

    private void Start()
    {
        if (forceNewSeed)
        {
            randomizerSystem.NewGame(GetIds(), GetRequirements());
        }
        else
        {
            // Intenta cargar save; si no hay, genera desde la escena
            var pool = randomizerSystem.GetComponent<RandomizerSystem>();
            // GenerateSeedFromScene detecta todos los ChestBehaviour en la escena
            randomizerSystem.GenerateSeedFromScene();
        }
    }

    // Helpers para construir las listas desde los cofres de la escena
    private List<string> GetIds()
    {
        var chests = FindObjectsByType<ChestBehaviour>(FindObjectsSortMode.None);
        var ids = new List<string>();
        foreach (var c in chests) ids.Add(c.locationId);
        return ids;
    }

    private List<List<SOItem>> GetRequirements()
    {
        var chests = FindObjectsByType<ChestBehaviour>(FindObjectsSortMode.None);
        var reqs = new List<List<SOItem>>();
        foreach (var c in chests) reqs.Add(c.requiredItems);
        return reqs;
    }
}
