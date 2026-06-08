using UnityEngine;

/// <summary>
/// Place on a key world object. When the player walks into the trigger (or
/// interacts via an Interactable component if one is present), the key is
/// added to <see cref="KeyInventory"/> and the GameObject destroys itself.
///
/// Priority order:
///   1. If an <see cref="Interactable"/> component exists on the same GameObject,
///      the pickup is wired into that — the player must press the interact button.
///   2. Otherwise, OnTriggerEnter handles automatic pickup on contact.
/// </summary>
[RequireComponent(typeof(Collider))]
public class KeyPickup : MonoBehaviour
{
    [SerializeField] private string keyId = "key_default";
    [SerializeField] private string displayName = "Key";
    [SerializeField] private Sprite keyIcon;

    // Optional: message to post in the HUD on pickup.
    // Raise whatever notification event the project uses; here we just log.
    // Replace EventBus.Raise(new OnHUDMessageEvent(...)) if that event exists.
    private const string PlayerTag = "Player";

    private void Start()
    {
        // If an Interactable is present, wire the pickup into it so the player
        // has to press the interact button rather than auto-collecting on touch.
        var interactable = GetComponent<Interactable>();
        if (interactable != null)
        {
            interactable.OnUse.AddListener(Collect);
        }
    }

    // ─── Auto-pickup (no Interactable) ────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        // Skip if there is an Interactable — it handles collection.
        if (GetComponent<Interactable>() != null) return;

        if (!other.CompareTag(PlayerTag)) return;
        Collect();
    }

    // ─── Core collection logic ─────────────────────────────────────────────────

    private void Collect()
    {
        if (KeyInventory.Instance == null)
        {
            Debug.LogWarning("[KeyPickup] KeyInventory.Instance is null — cannot collect key.");
            return;
        }

        KeyInventory.Instance.AddKey(keyId, displayName, keyIcon);
        Debug.Log($"[KeyPickup] Collected key '{displayName}' (id: {keyId}).");

        // Notify HUD — raise a generic message event if your project has one.
        // Example (uncomment and adapt if OnHUDMessageEvent exists):
        // EventBus.Raise(new OnHUDMessageEvent { message = $"Picked up: {displayName}" });

        Destroy(gameObject);
    }
}
