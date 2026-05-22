using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Z-Targeting / lock-on system inspired by Zelda Twilight Princess.
/// Toggles on/off via OnTargetInputEvent and searches for the nearest GameObject
/// tagged "Enemy" within a configurable radius. When a target is locked, it rotates
/// the player towards it (Y axis only) and aligns the CameraTarget so the camera
/// orbits around the player keeping the target in frame.
/// The system is safe to use even when there are no enemies in the scene yet:
/// it will simply toggle IsTargeting without binding a CurrentTarget.
/// </summary>
[DisallowMultipleComponent]
public class TargetingSystem : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform of the player mesh. Will be rotated (Y axis only) towards the target.")]
    [SerializeField] private Transform modelTransform;

    [Tooltip("The CameraTarget transform (the one the camera follows). Its yaw will be aligned so the target stays in frame.")]
    [SerializeField] private Transform cameraTarget;

    [Header("Detection")]
    [Tooltip("Search radius (meters) around the player to find Enemy-tagged objects.")]
    [SerializeField] private float searchRadius = 15f;

    [Tooltip("Tag used to identify potential targets.")]
    [SerializeField] private string enemyTag = "Enemy";

    [Tooltip("Optional: layer mask used for the OverlapSphere. Default = all layers.")]
    [SerializeField] private LayerMask searchMask = ~0;

    [Tooltip("If true, the target line of sight is checked before locking on (uses Physics.Linecast).")]
    [SerializeField] private bool requireLineOfSight = false;

    [Header("Rotation")]
    [Tooltip("How fast the player model rotates towards the target (Slerp factor).")]
    [SerializeField] private float playerRotationSpeed = 12f;

    [Tooltip("How fast the CameraTarget yaw aligns with the target direction.")]
    [SerializeField] private float cameraAlignSpeed = 8f;

    [Header("Cycle")]
    [Tooltip("If true, pressing target again while already locked cycles to the next valid enemy. Otherwise it untargets.")]
    [SerializeField] private bool cycleOnRepress = true;

    // ────────────────────────────────────────────────────────────────────────
    // PUBLIC STATE
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>True while the player has the lock-on stance active.</summary>
    public bool IsTargeting { get; private set; }

    /// <summary>The currently locked target. May be null even when IsTargeting is true (no enemies in range).</summary>
    public Transform CurrentTarget { get; private set; }

    /// <summary>Raised when targeting is enabled (true) or disabled (false).</summary>
    public event Action<bool> OnTargetingChanged;

    /// <summary>Raised whenever the active target changes (including to null).</summary>
    public event Action<Transform> OnTargetChanged;

    // Internal
    private readonly Collider[] _overlapBuffer = new Collider[32];
    private bool _consumedThisPress;

    // ────────────────────────────────────────────────────────────────────────
    // LIFECYCLE
    // ────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (modelTransform == null)
            modelTransform = transform;

        if (cameraTarget == null)
            Debug.LogWarning("[TargetingSystem] CameraTarget is not assigned. Camera will not orbit around enemies.");
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnTargetInputEvent>(HandleTargetInput);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnTargetInputEvent>(HandleTargetInput);

        if (IsTargeting)
            SetTargeting(false);
    }

    // ────────────────────────────────────────────────────────────────────────
    // INPUT
    // ────────────────────────────────────────────────────────────────────────

    private void HandleTargetInput(OnTargetInputEvent e)
    {
        // We treat the event as a toggle on the "pressed" edge only.
        if (!e.pressed)
        {
            _consumedThisPress = false;
            return;
        }

        if (_consumedThisPress) return;
        _consumedThisPress = true;

        if (!IsTargeting)
        {
            // Begin lock-on.
            SetTargeting(true);
            AcquireTarget();
        }
        else
        {
            // Already locked: cycle if requested AND there is at least one candidate, otherwise untarget.
            if (cycleOnRepress && CurrentTarget != null)
                CycleTarget();
            else
                SetTargeting(false);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // TARGETING LOGIC
    // ────────────────────────────────────────────────────────────────────────

    private void SetTargeting(bool active)
    {
        if (IsTargeting == active) return;
        IsTargeting = active;

        if (!active)
            SetTarget(null);

        OnTargetingChanged?.Invoke(active);
    }

    private void SetTarget(Transform t)
    {
        if (CurrentTarget == t) return;
        CurrentTarget = t;
        OnTargetChanged?.Invoke(t);
    }

    /// <summary>Find the closest enemy in range and lock on. Sets target to null if none found.</summary>
    public void AcquireTarget()
    {
        SetTarget(FindBestCandidate(null));
    }

    /// <summary>Pick the next valid enemy. Falls back to closest if none after the current.</summary>
    public void CycleTarget()
    {
        SetTarget(FindBestCandidate(CurrentTarget));
    }

    private Transform FindBestCandidate(Transform excluding)
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, searchRadius, _overlapBuffer, searchMask, QueryTriggerInteraction.Collide);

        Transform best = null;
        float bestScore = float.MaxValue;

        Vector3 origin = transform.position;

        for (int i = 0; i < count; i++)
        {
            Collider c = _overlapBuffer[i];
            if (c == null) continue;

            // Skip self / children of self
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;

            if (!c.CompareTag(enemyTag)) continue;

            Transform candidate = c.transform;
            if (excluding != null && candidate == excluding) continue;

            if (requireLineOfSight)
            {
                if (Physics.Linecast(origin + Vector3.up * 1f, candidate.position + Vector3.up * 1f, out _, searchMask, QueryTriggerInteraction.Ignore))
                    continue;
            }

            float dist = Vector3.Distance(origin, candidate.position);
            // Lower score is better. Distance is the primary factor.
            float score = dist;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        // If we were cycling and ran out of candidates, fall back to the closest (including the previous one).
        if (best == null && excluding != null)
            best = FindBestCandidate(null);

        return best;
    }

    // ────────────────────────────────────────────────────────────────────────
    // UPDATE
    // ────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsTargeting) return;

        // Drop the target if it has been destroyed or moved out of range.
        if (CurrentTarget != null)
        {
            if (!CurrentTarget.gameObject.activeInHierarchy ||
                Vector3.Distance(transform.position, CurrentTarget.position) > searchRadius * 1.25f)
            {
                SetTarget(null);
            }
        }

        if (CurrentTarget == null) return;

        // Rotate the player model on Y towards the target.
        Vector3 toTarget = CurrentTarget.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            Quaternion desired = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            modelTransform.rotation = Quaternion.Slerp(
                modelTransform.rotation, desired, playerRotationSpeed * Time.deltaTime);

            // Align the CameraTarget yaw so the camera orbits with the target in frame.
            if (cameraTarget != null)
            {
                Vector3 currentEuler = cameraTarget.eulerAngles;
                float desiredYaw = desired.eulerAngles.y;
                float newYaw = Mathf.LerpAngle(currentEuler.y, desiredYaw, cameraAlignSpeed * Time.deltaTime);
                cameraTarget.eulerAngles = new Vector3(currentEuler.x, newYaw, 0f);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>World-space direction from the player to the current target (XZ plane). Zero if no target.</summary>
    public Vector3 DirectionToTarget
    {
        get
        {
            if (CurrentTarget == null) return Vector3.zero;
            Vector3 d = CurrentTarget.position - transform.position;
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, searchRadius);

        if (CurrentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position + Vector3.up, CurrentTarget.position + Vector3.up);
        }
    }
#endif
}
