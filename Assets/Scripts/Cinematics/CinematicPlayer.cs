using System.Collections;
using UnityEngine;

/// <summary>
/// Singleton that plays CinematicSequences using plain Unity Cameras.
/// No Cinemachine required.
///
/// How it works:
///   • Each CinematicShot has a Camera reference.
///   • CinematicPlayer disables the gameplay camera and enables the shot camera.
///   • After the shot duration it moves to the next shot.
///   • On finish it re-enables the gameplay camera and player control.
/// </summary>
public class CinematicPlayer : MonoBehaviour
{
    public static CinematicPlayer Instance { get; private set; }

    [Header("Gameplay camera to disable during cinematics")]
    [Tooltip("Assign the main gameplay Camera here. It will be disabled while a cinematic plays.")]
    [SerializeField] private Camera gameplayCamera;

    // ── State ─────────────────────────────────────────────────────────────────

    public bool IsPlaying { get; private set; }

    private Coroutine        _playbackCoroutine;
    private CinematicSequence _currentSequence;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Starts playing a cinematic sequence. No-op if already playing.</summary>
    public void PlayCinematic(CinematicSequence sequence)
    {
        if (sequence == null || !sequence.HasShots) return;
        if (IsPlaying) StopCinematic();

        _currentSequence  = sequence;
        _playbackCoroutine = StartCoroutine(PlaySequenceCoroutine(sequence));
    }

    /// <summary>Interrupts the current cinematic and immediately restores player control.</summary>
    public void StopCinematic()
    {
        if (_playbackCoroutine != null) StopCoroutine(_playbackCoroutine);
        _playbackCoroutine = null;
        RestoreControl();
    }

    // ── Playback coroutine ────────────────────────────────────────────────────

    private IEnumerator PlaySequenceCoroutine(CinematicSequence seq)
    {
        IsPlaying = true;

        // Disable player input + gameplay camera.
        if (seq.DisablePlayerDuringCinematic)
            EventBus.Raise(new OnSetMovementEnabledEvent { enabled = false });
        if (seq.LockCameraDuringCinematic && gameplayCamera != null)
            gameplayCamera.enabled = false;

        seq.onCinematicStart?.Invoke();

        // Play each shot.
        foreach (var shot in seq.Shots)
        {
            if (shot.cam == null)
            {
                yield return new WaitForSeconds(shot.duration);
                continue;
            }

            shot.cam.enabled = true;
            yield return new WaitForSeconds(shot.duration);
            shot.cam.enabled = false;
        }

        seq.onCinematicEnd?.Invoke();
        RestoreControl();
    }

    private void RestoreControl()
    {
        IsPlaying = false;

        // Re-enable gameplay camera.
        if (gameplayCamera != null) gameplayCamera.enabled = true;

        // Re-enable player.
        EventBus.Raise(new OnSetMovementEnabledEvent { enabled = true });
        EventBus.Raise(new OnSetCameraEnabledEvent   { enabled = true });

        _currentSequence   = null;
        _playbackCoroutine = null;
    }
}
