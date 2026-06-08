using UnityEngine;

/// <summary>
/// Data bag that pairs an action name with a VFX prefab and an SFX clip.
/// Serialized inside <see cref="SOVFXProfile"/> and configured through the
/// Randomiz/VFX &amp; SFX Binder editor window.
/// </summary>
[System.Serializable]
public class VFXBinding
{
    // ── Identity ──────────────────────────────────────────────────────────────
    [Tooltip("Must match one of the SOVFXProfile action-name constants.")]
    public string actionName = string.Empty;

    // ── Visual effect ─────────────────────────────────────────────────────────
    [Tooltip("Prefab instantiated when the action fires. Leave null to skip VFX.")]
    public GameObject vfxPrefab;

    [Tooltip("When true the spawned VFX is parented to the source transform " +
             "instead of being placed in world space.")]
    public bool attachToSource = false;

    [Tooltip("Seconds before the spawned VFX instance is automatically destroyed. " +
             "Set to 0 to disable auto-destroy (useful when the prefab self-manages).")]
    public float duration = 2f;

    // ── Audio effect ──────────────────────────────────────────────────────────
    [Tooltip("One-shot audio clip played when the action fires. Leave null to skip SFX.")]
    public AudioClip sfxClip;

    [Range(0f, 1f)]
    [Tooltip("Volume scalar applied to the one-shot playback.")]
    public float sfxVolume = 1f;
}
