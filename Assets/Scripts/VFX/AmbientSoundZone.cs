using System.Collections;
using UnityEngine;

/// <summary>
/// Box-trigger volume that fades an ambient AudioClip in when the player enters
/// and fades it out when the player leaves.  Multiple overlapping zones each
/// manage their own private AudioSource, so they blend naturally.
///
/// Requirements:
///   - A BoxCollider set to Is Trigger = true must be present on this GameObject
///     (added automatically in Reset if missing).
///   - The player must have the "Player" tag.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class AmbientSoundZone : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Audio")]
    [Tooltip("Looping ambient clip to play inside this zone.")]
    [SerializeField] private AudioClip ambientClip;

    [Range(0f, 1f)]
    [Tooltip("Target volume reached after fading in.")]
    [SerializeField] private float maxVolume = 0.5f;

    [Tooltip("Volume change per second during fade transitions.")]
    [SerializeField] private float fadeSpeed = 1f;

    [Tooltip("Whether the clip loops. Disable for one-shot entry stingers.")]
    [SerializeField] private bool loop = true;

    // ── Private state ─────────────────────────────────────────────────────────
    private AudioSource _audioSource;
    private Coroutine   _fadeCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        // Create a dedicated AudioSource so the zone is self-contained and
        // does not interfere with other AudioSources on the GameObject.
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.clip        = ambientClip;
        _audioSource.loop        = loop;
        _audioSource.volume      = 0f;
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // Full 2-D ambient sound.
    }

    // Called from the editor when the component is first added — ensures the
    // collider is already a trigger so the designer doesn't forget.
    private void Reset()
    {
        BoxCollider bc = GetComponent<BoxCollider>();
        if (bc != null) bc.isTrigger = true;
    }

    // ── Trigger callbacks ─────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // Start playback before fading so volume ramps from silence.
        if (!_audioSource.isPlaying)
            _audioSource.Play();

        StartFade(maxVolume);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        StartFade(0f, stopOnSilence: true);
    }

    // ── Fade helpers ──────────────────────────────────────────────────────────

    private void StartFade(float targetVolume, bool stopOnSilence = false)
    {
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        _fadeCoroutine = StartCoroutine(FadeRoutine(targetVolume, stopOnSilence));
    }

    private IEnumerator FadeRoutine(float targetVolume, bool stopOnSilence)
    {
        while (!Mathf.Approximately(_audioSource.volume, targetVolume))
        {
            _audioSource.volume = Mathf.MoveTowards(
                _audioSource.volume,
                targetVolume,
                fadeSpeed * Time.deltaTime);

            yield return null;
        }

        _audioSource.volume = targetVolume;

        // Stop the AudioSource once fully silent to free the audio thread resource.
        if (stopOnSilence && _audioSource.volume <= 0f)
            _audioSource.Stop();

        _fadeCoroutine = null;
    }

#if UNITY_EDITOR
    // Draw a wireframe box in the scene view so designers can see the zone bounds.
    private void OnDrawGizmos()
    {
        BoxCollider bc = GetComponent<BoxCollider>();
        if (bc == null) return;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(
            transform.TransformPoint(bc.center),
            transform.rotation,
            transform.lossyScale);
        Gizmos.DrawCube(Vector3.zero, bc.size);

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, bc.size);
        Gizmos.matrix = old;
    }
#endif
}
