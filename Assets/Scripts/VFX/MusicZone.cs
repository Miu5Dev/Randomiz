using UnityEngine;

/// <summary>
/// Box-trigger volume that requests a BGM track from <see cref="MusicManager"/>
/// when the player enters and releases it when the player exits.
///
/// The manager always plays the highest-priority active zone's clip, so
/// overlapping zones (dungeon inside overworld) resolve automatically.
/// Set boss-room priority higher than dungeon, dungeon higher than overworld.
///
/// Requirements:
///   - A BoxCollider with Is Trigger = true (added automatically via Reset).
///   - The player must have the "Player" tag.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class MusicZone : MonoBehaviour
{
    [Header("Music")]
    [Tooltip("BGM track to play while inside this zone.")]
    [SerializeField] private AudioClip clip;

    [Tooltip("Crossfade duration when this zone activates or deactivates (seconds).")]
    [SerializeField] private float fadeTime = 2f;

    [Tooltip("Higher value wins over overlapping zones. " +
             "Suggested: overworld = 0, dungeon = 10, boss room = 20.")]
    [SerializeField] private int priority = 0;

    // ── Accessors (read by MusicManager) ──────────────────────────────────────
    public AudioClip Clip     => clip;
    public float     FadeTime => fadeTime;
    public int       Priority => priority;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        // PhysicsController moves the player via transform (no Rigidbody).
        // Unity only fires OnTriggerEnter when at least one object has a Rigidbody,
        // so we ensure this volume has a kinematic one.
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
    }

    private void Reset()
    {
        BoxCollider bc = GetComponent<BoxCollider>();
        if (bc != null) bc.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.transform.root.CompareTag("Player")) return;
        MusicManager.Instance?.RegisterZone(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.transform.root.CompareTag("Player")) return;
        MusicManager.Instance?.UnregisterZone(this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        BoxCollider bc = GetComponent<BoxCollider>();
        if (bc == null) return;

        // Purple to distinguish from AmbientSoundZone (cyan).
        Gizmos.color = new Color(0.85f, 0.35f, 0.95f, 0.2f);
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(
            transform.TransformPoint(bc.center),
            transform.rotation,
            transform.lossyScale);
        Gizmos.DrawCube(Vector3.zero, bc.size);

        Gizmos.color = new Color(0.85f, 0.35f, 0.95f, 0.85f);
        Gizmos.DrawWireCube(Vector3.zero, bc.size);
        Gizmos.matrix = old;
    }
#endif
}
