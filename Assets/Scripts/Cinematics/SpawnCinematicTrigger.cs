using UnityEngine;
using System.Collections;

/// <summary>
/// MonoBehaviour for playing a cinematic sequence when the scene first loads.
/// Place this in the spawn room to play an intro cutscene on first load.
/// Can be configured to play only on first load (not on checkpoint respawns).
///
/// This script checks a save flag to determine if this is a fresh scene load.
/// </summary>
public class SpawnCinematicTrigger : MonoBehaviour
{
    [SerializeField] private CinematicSequence spawnCinematic;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private bool playOnce = true;

    // Key used to track if the spawn cinematic has already been played in this save.
    private const string SpawnCinematicPlayedKey = "SpawnCinematicPlayed";

    private void Start()
    {
        if (spawnCinematic == null || !spawnCinematic.HasShots)
        {
            Debug.LogWarning("[SpawnCinematicTrigger] No cinematic sequence assigned or sequence is empty");
            return;
        }

        // If playOnce is enabled and the cinematic was already played, skip it.
        if (playOnce && PlayerPrefs.GetInt(SpawnCinematicPlayedKey, 0) == 1)
        {
            return;
        }

        // Play the cinematic.
        if (CinematicPlayer.Instance != null)
        {
            StartCoroutine(PlayCinematicAndSpawn());
        }
        else
        {
            Debug.LogError("[SpawnCinematicTrigger] CinematicPlayer singleton not found");
        }
    }

    private IEnumerator PlayCinematicAndSpawn()
    {
        // Play the cinematic.
        CinematicPlayer.Instance.PlayCinematic(spawnCinematic);

        // Wait for it to finish.
        yield return new WaitUntil(() => !CinematicPlayer.Instance.IsPlaying);

        // Mark that the spawn cinematic has been played.
        if (playOnce)
        {
            PlayerPrefs.SetInt(SpawnCinematicPlayedKey, 1);
            PlayerPrefs.Save();
        }

        // Position the player at the spawn point if specified.
        if (spawnPoint != null && PlayerMovement.Instance != null)
        {
            PlayerMovement.Instance.transform.position = spawnPoint.position;
        }
    }
}
