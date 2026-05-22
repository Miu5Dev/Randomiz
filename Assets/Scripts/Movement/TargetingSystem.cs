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

    [Tooltip("If true (default), the target button works as Press & Hold: targeting stays active only while the button is held. If false, behaves as a toggle.")]
    [SerializeField] private bool holdToTarget = true;

    [Header("Look-cycle")]
    [Tooltip("Magnitude on the X axis of the Look input required to trigger a target cycle.")]
    [SerializeField] private float lookCycleThreshold = 0.5f;

    [Tooltip("Minimum time (seconds) between two consecutive cycles triggered by the Look input.")]
    [SerializeField] private float cycleCooldown = 0.4f;

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
    private float _lastCycleTime = -999f;

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
        // High priority so we run before CameraTargetController and can cancel the event while targeting.
        EventBus.Subscribe<OnLookInputEvent>(HandleLookInput, priority: 100);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnTargetInputEvent>(HandleTargetInput);
        EventBus.Unsubscribe<OnLookInputEvent>(HandleLookInput);

        if (IsTargeting)
            SetTargeting(false);
    }

    // ────────────────────────────────────────────────────────────────────────
    // INPUT
    // ────────────────────────────────────────────────────────────────────────

    private void HandleTargetInput(OnTargetInputEvent e)
    {
        if (holdToTarget)
        {
            // Press & Hold: targeting follows the button state directly.
            if (e.pressed)
            {
                if (!IsTargeting)
                {
                    SetTargeting(true);
                    AcquireTarget();
                }
                else if (cycleOnRepress && CurrentTarget != null)
                {
                    // Re-press while already holding cycles to the next target.
                    if (!_consumedThisPress)
                    {
                        CycleTarget();
                        _consumedThisPress = true;
                    }
                }
            }
            else
            {
                // Released → end targeting.
                _consumedThisPress = false;
                if (IsTargeting)
                    SetTargeting(false);
            }
            return;
        }

        // Toggle behaviour (legacy): act on the "pressed" edge only.
        if (!e.pressed)
        {
            _consumedThisPress = false;
            return;
        }

        if (_consumedThisPress) return;
        _consumedThisPress = true;

        if (!IsTargeting)
        {
            SetTargeting(true);
            AcquireTarget();
        }
        else
        {
            if (cycleOnRepress && CurrentTarget != null)
                CycleTarget();
            else
                SetTargeting(false);
        }
    }

    private void HandleLookInput(OnLookInputEvent e)
    {
        if (!IsTargeting) return;

        // Swallow the look input while locked on so the camera doesn't fight the lock-on yaw.
        EventBus.Cancel<OnLookInputEvent>();

        if (e == null || !e.pressed) return;
        if (Time.time - _lastCycleTime < cycleCooldown) return;

        float x = e.Delta.x;
        if (Mathf.Abs(x) < lookCycleThreshold) return;

        CycleTargetDirectional(x > 0f ? 1 : -1);
        _lastCycleTime = Time.time;
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

    /// <summary>
    /// Cycle to the next enemy on the left (direction == -1) or right (direction == +1) of the current target,
    /// ordered by their angular position around the player on the XZ plane. Wraps around if it runs out.
    /// </summary>
    public void CycleTargetDirectional(int direction)
    {
        if (direction == 0) return;

        Vector3 origin = transform.position;
        int count = Physics.OverlapSphereNonAlloc(
            origin, searchRadius, _overlapBuffer, searchMask, QueryTriggerInteraction.Collide);

        var candidates = new List<(Transform t, float angle)>();
        for (int i = 0; i < count; i++)
        {
            Collider c = _overlapBuffer[i];
            if (c == null) continue;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            if (!c.CompareTag(enemyTag)) continue;

            if (requireLineOfSight &&
                Physics.Linecast(origin + Vector3.up, c.transform.position + Vector3.up, out _, searchMask, QueryTriggerInteraction.Ignore))
                continue;

            Vector3 d = c.transform.position - origin;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) continue;

            // Signed angle around world up. Positive = clockwise from player forward = right.
            float angle = Vector3.SignedAngle(modelTransform.forward, d.normalized, Vector3.up);
            candidates.Add((c.transform, angle));
        }

        if (candidates.Count <= 1) return;

        candidates.Sort((a, b) => a.angle.CompareTo(b.angle));

        int currentIndex = -1;
        if (CurrentTarget != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].t == CurrentTarget) { currentIndex = i; break; }
            }
        }

        int nextIndex;
        if (currentIndex < 0)
            nextIndex = direction > 0 ? 0 : candidates.Count - 1;
        else
            nextIndex = (currentIndex + direction + candidates.Count) % candidates.Count;

        SetTarget(candidates[nextIndex].t);
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
