using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Sliding door that opens upward when the player has the required key.
/// Attach to the door mesh/wall GameObject.
///
/// Interaction is handled by requiring an <see cref="Interactable"/> on the
/// same GameObject — the player presses the interact button when nearby.
/// If no key is held, a log warning is emitted (replace with your HUD message
/// event if available). If the key is held, the door slides up smoothly and the
/// open state is persisted to a dedicated JSON save file.
/// </summary>
[RequireComponent(typeof(Interactable))]
public class DoorController : MonoBehaviour
{
    [Header("Key settings")]
    [SerializeField] private string requiredKeyId = "key_default";
    [SerializeField] private bool consumeKey = false;

    [Header("Animation")]
    [Tooltip("How far (world units) the door slides upward when opened.")]
    [SerializeField] private float openDistance = 3f;
    [Tooltip("Duration of the sliding animation in seconds.")]
    [SerializeField] private float openDuration = 1.5f;

    // ─── State ─────────────────────────────────────────────────────────────────

    private bool _isOpen;
    private bool _isAnimating;
    private Vector3 _closedPosition;
    private Vector3 _openPosition;

    // ─── Persistence ───────────────────────────────────────────────────────────

    [System.Serializable]
    private class SaveData { public bool isOpen; }

    // Use the GameObject's instance ID so each door has its own save file.
    private string SavePath =>
        Path.Combine(Application.persistentDataPath, $"door_{gameObject.name}_{GetInstanceID()}.json");

    // ─── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _closedPosition = transform.position;
        _openPosition   = _closedPosition + Vector3.up * openDistance;
    }

    private void Start()
    {
        // Wire the Interactable so player interaction calls TryOpen.
        var interactable = GetComponent<Interactable>();
        interactable.OnUse.AddListener(TryOpen);

        // Restore saved state (snap instantly, no animation on load).
        if (File.Exists(SavePath)) LoadState();
        if (_isOpen) transform.position = _openPosition;
    }

    // ─── Interaction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player activates the door's Interactable.
    /// Opens the door if the player holds the required key; logs a warning otherwise.
    /// </summary>
    public void TryOpen()
    {
        if (_isOpen || _isAnimating) return;

        if (KeyInventory.Instance == null)
        {
            Debug.LogWarning("[DoorController] KeyInventory.Instance is null.");
            return;
        }

        if (!KeyInventory.Instance.HasKey(requiredKeyId))
        {
            // Resolve display name for the message.
            string name = RequiredKeyDisplayName();
            Debug.Log($"[DoorController] Requires: {name}");

            // Replace with your HUD notification event if available, e.g.:
            // EventBus.Raise(new OnHUDMessageEvent { message = $"Requires: {name}" });
            return;
        }

        if (consumeKey) KeyInventory.Instance.ConsumeKey(requiredKeyId);

        StartCoroutine(SlideOpen());
    }

    // ─── Animation ─────────────────────────────────────────────────────────────

    private IEnumerator SlideOpen()
    {
        _isAnimating = true;

        float elapsed = 0f;
        Vector3 from  = transform.position;

        while (elapsed < openDuration)
        {
            elapsed             += Time.deltaTime;
            float t              = Mathf.SmoothStep(0f, 1f, elapsed / openDuration);
            transform.position   = Vector3.Lerp(from, _openPosition, t);
            yield return null;
        }

        transform.position = _openPosition;
        _isOpen            = true;
        _isAnimating       = false;

        SaveState();
    }

    // ─── Persistence ───────────────────────────────────────────────────────────

    private void SaveState()
    {
        var data = new SaveData { isOpen = _isOpen };
#if UNITY_EDITOR
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
#else
        File.WriteAllText(SavePath, JsonUtility.ToJson(data));
#endif
    }

    private void LoadState()
    {
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            if (data != null) _isOpen = data.isOpen;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[DoorController] Load failed: {ex.Message}");
        }
    }

    public static void DeleteSave(DoorController door)
    {
        if (door != null && File.Exists(door.SavePath)) File.Delete(door.SavePath);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private string RequiredKeyDisplayName()
    {
        if (KeyInventory.Instance == null) return requiredKeyId;

        // Look for a held key with this ID to get its display name, or fall
        // back to the raw keyId.  We search all held keys (the player might
        // not hold it, but a scene peer KeyPickup could provide the name).
        var held = KeyInventory.Instance.Keys.Find(k => k.keyId == requiredKeyId);
        return held != null ? held.displayName : requiredKeyId;
    }
}
