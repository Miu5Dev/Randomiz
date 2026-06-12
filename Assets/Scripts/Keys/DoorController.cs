using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sliding door that opens upward when the player has the required key.
/// Attach to the door mesh/wall GameObject.
///
/// Door open state is managed by <see cref="SaveManager"/> as part of the main
/// save slot — it is not persisted independently. This means door state is always
/// in sync with the active seed: a new game or loading a different slot always
/// starts with all doors closed unless they were opened in that specific run.
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

    // Stable per-door id: name + instance id (instance id is fixed for scene objects).
    private string DoorId => $"{gameObject.name}_{GetInstanceID()}";

    // ─── Static registry (session-scoped) ──────────────────────────────────────
    // Populated by SaveManager before Start() runs (via sceneLoaded callback),
    // so DoorController.Start() can query it for the correct initial state.

    private static readonly HashSet<string> s_openedDoors = new();

    /// <summary>
    /// Called by SaveManager.ApplyRestore before any DoorController.Start() runs.
    /// Populates the registry so doors know which ones were open in the loaded save.
    /// </summary>
    public static void RestoreOpenedDoors(List<string> ids)
    {
        s_openedDoors.Clear();
        if (ids != null)
            foreach (var id in ids)
                s_openedDoors.Add(id);
    }

    /// <summary>Called by SaveManager.SaveGame to collect door state for the save file.</summary>
    public static List<string> GetOpenedDoorIds() => new List<string>(s_openedDoors);

    /// <summary>Called by SaveManager.NewGame to reset all door state for a fresh run.</summary>
    public static void ClearAll() => s_openedDoors.Clear();

    // ─── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _closedPosition = transform.position;
        _openPosition   = _closedPosition + Vector3.up * openDistance;
    }

    private void Start()
    {
        var interactable = GetComponent<Interactable>();
        interactable.OnUse.AddListener(TryOpen);

        // Restore open state from the session registry (populated by SaveManager
        // before this Start() call via the sceneLoaded callback).
        _isOpen = s_openedDoors.Contains(DoorId);
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
            string name = RequiredKeyDisplayName();
            Debug.Log($"[DoorController] Requires: {name}");
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
            elapsed           += Time.deltaTime;
            float t            = Mathf.SmoothStep(0f, 1f, elapsed / openDuration);
            transform.position = Vector3.Lerp(from, _openPosition, t);
            yield return null;
        }

        transform.position = _openPosition;
        _isOpen            = true;
        _isAnimating       = false;

        s_openedDoors.Add(DoorId);
        SaveManager.Instance?.SaveGame(SaveManager.Instance.CurrentSlot);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private string RequiredKeyDisplayName()
    {
        if (KeyInventory.Instance == null) return requiredKeyId;
        var held = KeyInventory.Instance.Keys.Find(k => k.keyId == requiredKeyId);
        return held != null ? held.displayName : requiredKeyId;
    }
}
