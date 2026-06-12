using UnityEngine;

/// <summary>
/// World trigger volume that activates a checkpoint when the player enters it.
/// Put this on a GameObject with a trigger Collider. On activation it notifies
/// child objects via SendMessage("OnCheckpointActivated") so they can play a glow
/// or particle burst, then asks the <see cref="CheckpointManager"/> to set this as
/// the active spawn point (which saves the game).
///
/// Does nothing if this checkpoint is already the active one.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CheckpointTrigger : MonoBehaviour
{
    [SerializeField] private string checkpointId;
    [Tooltip("Tag used to identify the player. Defaults to \"Player\".")]
    [SerializeField] private string playerTag = "Player";

    private void Awake()
    {
        // PhysicsController has no Rigidbody — kinematic Rigidbody on this volume
        // is required for OnTriggerEnter to fire against the player's static collider.
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        // Fall back to the GameObject name so designers can leave the id blank.
        if (string.IsNullOrEmpty(checkpointId))
            checkpointId = gameObject.name;
    }

    private void Start()
    {
        // Register our world position so the manager can resolve the spawn point
        // after a load even before the player walks back into this trigger.
        CheckpointManager.Instance?.RegisterCheckpoint(checkpointId, transform.position);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        var manager = CheckpointManager.Instance;
        if (manager == null) return;

        // Already the active checkpoint — skip activation and feedback.
        if (manager.ActiveCheckpointId == checkpointId) return;

        manager.ActivateCheckpoint(checkpointId);

        // Visual feedback: let child objects (glow, particles) react.
        BroadcastMessage("OnCheckpointActivated", SendMessageOptions.DontRequireReceiver);
    }
}
