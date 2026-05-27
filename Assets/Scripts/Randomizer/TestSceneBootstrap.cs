using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Bootstraps the randomizer for a test scene. Discovers all ChestBehaviour
/// instances in the scene and either loads a saved seed or generates a new one.
///
/// Runs in Awake (DefaultExecutionOrder -100) so that ChestBehaviour.Start can
/// safely read the populated state — Unity orders all Awakes before any Start.
/// </summary>
[DefaultExecutionOrder(-100)]
public class TestSceneBootstrap : MonoBehaviour
{
    [SerializeField] private RandomizerSystem randomizerSystem;

    [Header("Test Controls")]
    [Tooltip("If true, delete the save and generate a fresh seed (even if a save exists).")]
    [SerializeField] private bool forceNewSeed = false;

    private void Awake()
    {
        if (randomizerSystem == null)
        {
            Debug.LogError("[TestSceneBootstrap] RandomizerSystem reference not set.");
            return;
        }

        // Single scene scan — reused for ids + requirements.
        var chests = FindObjectsByType<ChestBehaviour>(FindObjectsSortMode.None);
        int n = chests.Length;

        var ids  = new List<string>(n);
        var reqs = new List<List<SOItem>>(n);
        for (int i = 0; i < n; i++)
        {
            ids.Add(chests[i].locationId);
            reqs.Add(chests[i].requiredItems);
        }

        if (forceNewSeed)
            randomizerSystem.NewGame(ids, reqs);
        else
            randomizerSystem.LoadOrGenerate(ids, reqs); // Honors existing save if present.
    }
}
