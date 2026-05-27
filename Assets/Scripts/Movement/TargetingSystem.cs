using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TargetingSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform modelTransform;
    [SerializeField] private Transform cameraTarget;

    [Header("Detection")]
    [SerializeField] private float searchRadius = 15f;
    [SerializeField] private string enemyTag = "Enemy";
    [SerializeField] private LayerMask searchMask = ~0;
    [SerializeField] private bool requireLineOfSight = false;

    [Header("Rotation")]
    [SerializeField] private float playerRotationSpeed = 12f;
    [SerializeField] private float cameraAlignSpeed = 6f;

    [Header("Camera Pitch (Vertical)")]
    [Tooltip("Camera pitch speed (degrees/second) when using the right stick vertically.")]
    [SerializeField] private float cameraPitchSpeed = 45f;
    [Tooltip("Minimum downward pitch (negative).")]
    [SerializeField] private float minPitch = -30f;
    [Tooltip("Maximum upward pitch.")]
    [SerializeField] private float maxPitch = 60f;

    [Header("Cycle")]
    [SerializeField] private bool cycleOnRepress = true;
    [SerializeField] private bool holdToTarget = true;

    [Header("Look-cycle")]
    [SerializeField] private float lookCycleThreshold = 0.5f;
    [SerializeField] private float cycleCooldown = 0.4f;

    public bool IsTargeting { get; private set; }
    public Transform CurrentTarget { get; private set; }
    /// <summary>
    /// External lock. While true, targeting input is ignored and any active target is cleared.
    /// Used by movement states like ledge grab to disable targeting.
    /// </summary>
    public bool Locked { get; set; }
    public event Action<bool> OnTargetingChanged;
    public event Action<Transform> OnTargetChanged;

    private readonly Collider[] _overlapBuffer = new Collider[32];
    private bool _consumedThisPress;
    private float _lastCycleTime = -999f;
    private float _snapRotationRemaining = 0f;
    private float _currentCameraYawVelocity = 0f;
    private float _currentPitch = 0f;          // Ángulo vertical acumulado (grados)

    private Quaternion _desiredPlayerRotation;
    private bool _hasDesiredRotation;

    private void Awake()
    {
        if (modelTransform == null) modelTransform = transform;
        if (cameraTarget == null) Debug.LogWarning("[TargetingSystem] CameraTarget not assigned.");

        // Initialize pitch from the cameraTarget's current X rotation.
        if (cameraTarget != null)
            _currentPitch = cameraTarget.eulerAngles.x;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnTargetInputEvent>(HandleTargetInput);
        EventBus.Subscribe<OnLookInputEvent>(HandleLookInput, priority: 100);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnTargetInputEvent>(HandleTargetInput);
        EventBus.Unsubscribe<OnLookInputEvent>(HandleLookInput);
        if (IsTargeting) SetTargeting(false);
    }

    private void HandleTargetInput(OnTargetInputEvent e)
    {
        if (Locked)
        {
            if (IsTargeting) SetTargeting(false);
            _consumedThisPress = false;
            return;
        }
        if (holdToTarget)
        {
            if (e.pressed)
            {
                if (!IsTargeting)
                {
                    SetTargeting(true);
                    AcquireTarget();
                }
                else if (cycleOnRepress && CurrentTarget != null && !_consumedThisPress)
                {
                    CycleTarget();
                    _consumedThisPress = true;
                }
            }
            else
            {
                _consumedThisPress = false;
                if (IsTargeting) SetTargeting(false);
            }
            return;
        }

        if (!e.pressed) { _consumedThisPress = false; return; }
        if (_consumedThisPress) return;
        _consumedThisPress = true;

        if (!IsTargeting)
        {
            SetTargeting(true);
            AcquireTarget();
        }
        else
        {
            if (cycleOnRepress && CurrentTarget != null) CycleTarget();
            else SetTargeting(false);
        }
    }

    private void HandleLookInput(OnLookInputEvent e)
    {
        if (!IsTargeting) return;

        // Cancel the event so no other handler moves the camera horizontally.
        EventBus.Cancel<OnLookInputEvent>();

        if (e == null) return;

        // Ajustar el pitch (vertical) con la entrada en Y
        float verticalDelta = e.Delta.y;
        if (Mathf.Abs(verticalDelta) > 0.01f)
        {
            _currentPitch -= verticalDelta * cameraPitchSpeed * Time.deltaTime;
            _currentPitch = Mathf.Clamp(_currentPitch, minPitch, maxPitch);
        }

        // Horizontal cycle logic with the right stick (uses e.Delta.x).
        if (e.pressed && Time.time - _lastCycleTime >= cycleCooldown)
        {
            float x = e.Delta.x;
            if (Mathf.Abs(x) >= lookCycleThreshold)
            {
                CycleTargetDirectional(x > 0f ? 1 : -1);
                _lastCycleTime = Time.time;
            }
        }
    }

    private void SetTargeting(bool active)
    {
        if (IsTargeting == active) return;
        IsTargeting = active;

        if (!active)
            SetTarget(null);
        else
        {
            if (cameraTarget != null && modelTransform != null)
            {
                float cameraYaw = cameraTarget.eulerAngles.y;
                modelTransform.rotation = Quaternion.Euler(0f, cameraYaw, 0f);
                // Keep the current pitch (don't reset it).
            }
        }
        OnTargetingChanged?.Invoke(active);
    }

    private void SetTarget(Transform t)
    {
        if (CurrentTarget == t) return;
        CurrentTarget = t;
        OnTargetChanged?.Invoke(t);

        if (t != null && modelTransform != null)
        {
            Vector3 dir = t.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion lookRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                modelTransform.rotation = lookRot;
                _snapRotationRemaining = 0.15f;
            }
        }
    }

    public void AcquireTarget() => SetTarget(FindBestCandidate(null));
    public void CycleTarget() => SetTarget(FindBestCandidate(CurrentTarget));

    public void CycleTargetDirectional(int direction)
    {
        if (direction == 0) return;
        Vector3 origin = transform.position;
        int count = Physics.OverlapSphereNonAlloc(origin, searchRadius, _overlapBuffer, searchMask, QueryTriggerInteraction.Collide);
        var candidates = new List<(Transform t, float angle)>();
        for (int i = 0; i < count; i++)
        {
            Collider c = _overlapBuffer[i];
            if (c == null || c.transform == transform || c.transform.IsChildOf(transform)) continue;
            if (!c.CompareTag(enemyTag)) continue;
            if (requireLineOfSight && Physics.Linecast(origin + Vector3.up, c.transform.position + Vector3.up, out _, searchMask, QueryTriggerInteraction.Ignore)) continue;
            Vector3 d = c.transform.position - origin;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) continue;
            float angle = Vector3.SignedAngle(modelTransform.forward, d.normalized, Vector3.up);
            candidates.Add((c.transform, angle));
        }
        if (candidates.Count <= 1) return;
        candidates.Sort((a, b) => a.angle.CompareTo(b.angle));
        int currentIndex = -1;
        if (CurrentTarget != null)
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i].t == CurrentTarget) { currentIndex = i; break; }
        int nextIndex = currentIndex < 0 ? (direction > 0 ? 0 : candidates.Count - 1) : (currentIndex + direction + candidates.Count) % candidates.Count;
        SetTarget(candidates[nextIndex].t);
    }

    private Transform FindBestCandidate(Transform excluding)
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, searchRadius, _overlapBuffer, searchMask, QueryTriggerInteraction.Collide);
        Transform best = null;
        float bestScore = float.MaxValue;
        Vector3 origin = transform.position;
        for (int i = 0; i < count; i++)
        {
            Collider c = _overlapBuffer[i];
            if (c == null || c.transform == transform || c.transform.IsChildOf(transform)) continue;
            if (!c.CompareTag(enemyTag)) continue;
            Transform candidate = c.transform;
            if (excluding != null && candidate == excluding) continue;
            if (requireLineOfSight && Physics.Linecast(origin + Vector3.up * 1f, candidate.position + Vector3.up * 1f, out _, searchMask, QueryTriggerInteraction.Ignore)) continue;
            // sqrMagnitude avoids the sqrt of Vector3.Distance; ordering is preserved.
            float dist = (candidate.position - origin).sqrMagnitude;
            if (dist < bestScore)
            {
                bestScore = dist;
                best = candidate;
            }
        }
        if (best == null && excluding != null) best = FindBestCandidate(null);
        return best;
    }

    private void Update()
    {
        if (!IsTargeting) return;

        // Validate target — compare against squared radius to avoid sqrt every frame.
        if (CurrentTarget != null)
        {
            float maxRangeSqr = searchRadius * 1.25f;
            maxRangeSqr *= maxRangeSqr;
            if (!CurrentTarget.gameObject.activeInHierarchy ||
                (CurrentTarget.position - transform.position).sqrMagnitude > maxRangeSqr)
                SetTarget(null);
        }

        if (CurrentTarget == null) return;

        Vector3 toTarget = CurrentTarget.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            Quaternion desired = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            _desiredPlayerRotation = desired;
            _hasDesiredRotation = true;

            // Player model rotation.
            if (_snapRotationRemaining > 0f)
            {
                modelTransform.rotation = desired;
                _snapRotationRemaining -= Time.deltaTime;
            }
            else
            {
                modelTransform.rotation = Quaternion.Slerp(modelTransform.rotation, desired, playerRotationSpeed * Time.deltaTime);
            }
        }
        else
        {
            _hasDesiredRotation = false;
        }
    }

    private void LateUpdate()
    {
        if (!IsTargeting || CurrentTarget == null || !_hasDesiredRotation)
            return;

        if (cameraTarget != null)
        {
            // Yaw: smooth auto-orientation toward the target.
            float currentYaw = cameraTarget.eulerAngles.y;
            float desiredYaw = _desiredPlayerRotation.eulerAngles.y;
            float newYaw = Mathf.SmoothDampAngle(currentYaw, desiredYaw, ref _currentCameraYawVelocity, 1f / cameraAlignSpeed);

            // Pitch: player-controlled (clamped).
            // Ensure pitch is applied in the [-180, 180] range (normalized).
            float newPitch = _currentPitch;

            // Apply the final rotation: (pitch, yaw, 0).
            cameraTarget.eulerAngles = new Vector3(newPitch, newYaw, 0f);
        }
    }

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