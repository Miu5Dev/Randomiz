using UnityEngine;

/// <summary>
/// MonoBehaviour + BoxCollider trigger that plays a cinematic when the player enters.
/// Optionally disables itself after the first trigger (oneShot mode).
/// Must be used with a BoxCollider set to isTrigger = true.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class CinematicTriggerVolume : MonoBehaviour
{
    [SerializeField] private CinematicSequence cinematic;
    [SerializeField] private bool oneShot = true;

    private bool hasTriggered;

    private void Awake()
    {
        // PhysicsController has no Rigidbody — kinematic Rigidbody on this volume
        // is required for OnTriggerEnter to fire against the player's static collider.
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only trigger on the player.
        if (!other.CompareTag("Player"))
            return;

        // If oneShot is enabled and we've already triggered, skip.
        if (oneShot && hasTriggered)
            return;

        if (cinematic == null || !cinematic.HasShots)
        {
            Debug.LogWarning("[CinematicTriggerVolume] No cinematic sequence assigned or sequence is empty");
            return;
        }

        // Play the cinematic.
        if (CinematicPlayer.Instance != null)
        {
            CinematicPlayer.Instance.PlayCinematic(cinematic);
            hasTriggered = true;

            // Disable this component if oneShot is enabled.
            if (oneShot)
            {
                enabled = false;
            }
        }
        else
        {
            Debug.LogError("[CinematicTriggerVolume] CinematicPlayer singleton not found");
        }
    }

    /// <summary>
    /// Manually resets the oneShot flag, allowing the trigger to fire again.
    /// </summary>
    public void ResetTrigger()
    {
        hasTriggered = false;
        enabled = true;
    }
}
