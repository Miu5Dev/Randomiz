using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks the player's active spawn point. Checkpoint triggers register themselves
/// (id + world position) on Start, then call <see cref="ActivateCheckpoint"/> when
/// the player reaches them. Activating a checkpoint also triggers an automatic save
/// via <see cref="SaveManager"/>.
///
/// Registration is kept separate from activation so the manager can resolve a saved
/// checkpoint id back to a world position after a reload — provided the trigger has
/// registered itself by the time <see cref="GetSpawnPosition"/> is queried.
/// </summary>
public class CheckpointManager : MonoBehaviour
{
    public static CheckpointManager Instance { get; private set; }

    // Known checkpoints in the current scene (id -> world position).
    private readonly Dictionary<string, Vector3> _checkpoints = new();

    private string _activeId;
    private Vector3 _activePosition;
    private bool _hasActive;

    // Fallback spawn used before any checkpoint is reached (player start or an explicit
    // PlayerSpawnPoint). Keeps respawn anchored to a defined point, never "where you fell".
    private Vector3 _defaultSpawn;
    private bool    _hasDefault;

    public string ActiveCheckpointId => _activeId;

    /// <summary>True when a respawn target exists — an active checkpoint OR a default spawn.</summary>
    public bool HasSpawnPosition => _hasActive || _hasDefault;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Destroy only the duplicate component — this shares the "GameSystems"
            // object with SaveManager (DontDestroyOnLoad) & co. Destroy(gameObject)
            // would take the whole shared object down. See SaveManager.Awake.
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        // Record the player's starting position as the fallback spawn, so dying before
        // reaching any checkpoint still respawns at a defined point. An explicit
        // PlayerSpawnPoint (force) overrides this auto-captured value.
        if (!_hasDefault && PlayerMovement.Instance != null)
            SetDefaultSpawn(PlayerMovement.Instance.transform.position, force: false);
    }

    /// <summary>
    /// Sets the default fallback spawn used when no checkpoint is active. An authored
    /// <see cref="PlayerSpawnPoint"/> passes <paramref name="force"/> = true to override
    /// the auto-captured player start position.
    /// </summary>
    public void SetDefaultSpawn(Vector3 position, bool force = false)
    {
        if (_hasDefault && !force) return;
        _defaultSpawn = position;
        _hasDefault   = true;
    }

    /// <summary>
    /// Registers a checkpoint's world position. Safe to call multiple times; the
    /// latest position for a given id wins. Call from a checkpoint trigger's Start.
    /// If this id is the active (restored) checkpoint, the cached spawn position is
    /// refreshed so a load that happened before registration still resolves.
    /// </summary>
    public void RegisterCheckpoint(string id, Vector3 position)
    {
        if (string.IsNullOrEmpty(id)) return;
        _checkpoints[id] = position;

        if (_hasActive && id == _activeId)
            _activePosition = position;
    }

    /// <summary>
    /// Sets the active checkpoint and saves the game to the current slot.
    /// No-op if the id is already active.
    /// </summary>
    public void ActivateCheckpoint(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (_hasActive && id == _activeId) return;

        _activeId = id;
        _hasActive = true;

        if (_checkpoints.TryGetValue(id, out var pos))
            _activePosition = pos;

        SaveManager.Instance?.SaveGame(SaveManager.Instance.CurrentSlot);
    }

    /// <summary>
    /// Returns the active checkpoint's world position, or <see cref="Vector3.zero"/>
    /// if none is active.
    /// </summary>
    public Vector3 GetSpawnPosition()
    {
        if (_hasActive)
        {
            // Prefer the registered position (kept fresh as the scene loads).
            if (_checkpoints.TryGetValue(_activeId, out var pos))
                return pos;
            return _activePosition;
        }

        // No checkpoint reached yet — fall back to the default spawn point.
        if (_hasDefault) return _defaultSpawn;

        return Vector3.zero;
    }

    /// <summary>
    /// Restores the active checkpoint from save data. The position is used until the
    /// matching trigger registers itself, after which the registered value is used.
    /// </summary>
    public void RestoreActiveCheckpoint(string id, Vector3 position)
    {
        _activeId = id;
        _activePosition = position;
        _hasActive = !string.IsNullOrEmpty(id);

        if (_hasActive)
            _checkpoints[id] = position;
    }

    /// <summary>Clears the active checkpoint (e.g. when starting a new game).</summary>
    public void Clear()
    {
        _activeId = null;
        _hasActive = false;
        _activePosition = Vector3.zero;
        _defaultSpawn = Vector3.zero;
        _hasDefault = false;
    }
}
