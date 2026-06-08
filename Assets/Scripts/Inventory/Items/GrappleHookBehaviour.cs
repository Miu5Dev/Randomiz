using UnityEngine;

/// <summary>
/// MonoBehaviour that drives the full grappling-hook interaction.
/// Attach to the GrappleHookItem GameObject in the player's equip hierarchy.
///
/// Flow:
///   1. Player equips the grappling hook (OnItemEquipEvent).
///   2. Press attack  → enter aim mode (camera zooms, look is re-enabled if it
///      had been locked by another system).
///   3. Press attack a second time while aiming → fire raycast.
///      • HIT  : spawn hook at impact point, draw rope, apply pull/swing each
///               FixedUpdate.
///      • MISS : exit aim mode.
///   4. Press attack while hooked → release hook, restore state.
///   5. Player lands on ground while hooked → auto-release.
///   6. Unequip → release and clean up everything.
///
/// Rope is visualised with a LineRenderer.
/// Pull/swing are applied through PlayerMovement.velocity so they respect the
/// existing movement system rather than fighting a CharacterController.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class GrappleHookBehaviour : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Origin of the rope on the player model (e.g. right hand bone).")]
    [SerializeField] private Transform handPoint;

    [Tooltip("Camera used for aim direction. Leave null to use Camera.main.")]
    [SerializeField] private Camera gameplayCamera;

    [Header("Aim")]
    [Tooltip("Field of view while aiming (zoom).")]
    [SerializeField] private float aimFOV = 50f;

    [Tooltip("Optional crosshair UI for the grapple aim mode.")]
    [SerializeField] private GameObject crosshairUI;

    [Header("Release Conditions")]
    [Tooltip("Distance threshold from the anchor below which the hook is auto-released (arrived).")]
    [SerializeField] private float arrivalDistance = 1.2f;

    // ── Private state ──────────────────────────────────────────────────────────

    private SOGrappleHook _data;
    private bool          _isEquipped;
    private bool          _isAiming;
    private bool          _isHooked;
    private Vector3       _hookPoint;
    private GameObject    _hookInstance;
    private float         _defaultFOV;
    private LineRenderer  _lineRenderer;

    // Cache PlayerMovement to apply velocity directly each FixedUpdate.
    private PlayerMovement _playerMovement;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.positionCount = 2;
        _lineRenderer.enabled       = false;

        if (gameplayCamera == null)
            gameplayCamera = Camera.main;

        if (gameplayCamera != null)
            _defaultFOV = gameplayCamera.fieldOfView;

        _playerMovement = GetComponentInParent<PlayerMovement>();
        if (_playerMovement == null)
            _playerMovement = FindFirstObjectByType<PlayerMovement>();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Subscribe<OnItemUnequipEvent>(OnItemUnequip);
        EventBus.Subscribe<OnAttackInputEvent>(OnAttackInput);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Unsubscribe<OnItemUnequipEvent>(OnItemUnequip);
        EventBus.Unsubscribe<OnAttackInputEvent>(OnAttackInput);

        ReleaseHook();
        ExitAimMode();
    }

    private void FixedUpdate()
    {
        if (!_isHooked) return;

        ApplyGrapplePhysics();

        // Auto-release if the player has reached the anchor.
        if (_playerMovement != null)
        {
            float dist = Vector3.Distance(_playerMovement.transform.position, _hookPoint);
            if (dist <= arrivalDistance)
                ReleaseHook();
        }
    }

    private void LateUpdate()
    {
        if (!_isHooked) return;

        // Keep the rope visual updated every frame.
        UpdateRopeVisual();
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnItemEquip(OnItemEquipEvent e)
    {
        if (e.item is SOGrappleHook gh)
        {
            _data       = gh;
            _isEquipped = true;
        }
        else if (_isEquipped)
        {
            _isEquipped = false;
            ReleaseHook();
            ExitAimMode();
        }
    }

    private void OnItemUnequip(OnItemUnequipEvent e)
    {
        if (e.item is SOGrappleHook)
        {
            _isEquipped = false;
            _data       = null;
            ReleaseHook();
            ExitAimMode();
        }
    }

    private void OnAttackInput(OnAttackInputEvent e)
    {
        if (!_isEquipped || !e.pressed || _data == null) return;

        // Gate: must be able to act (except when already hooked — we want to
        // allow releasing the hook even mid-air).
        if (!_isHooked && PlayerStateMachine.Instance != null
            && !PlayerStateMachine.Instance.CanAct) return;

        if (_isHooked)
        {
            // Second press while hooked → release.
            ReleaseHook();
            return;
        }

        if (_isAiming)
        {
            // Second press while aiming → fire.
            FireHook();
        }
        else
        {
            // First press → enter aim mode.
            EnterAimMode();
        }
    }

    // ── Aim mode ───────────────────────────────────────────────────────────────

    private void EnterAimMode()
    {
        if (_isAiming) return;
        _isAiming = true;

        if (gameplayCamera != null)
            gameplayCamera.fieldOfView = aimFOV;

        if (crosshairUI != null)
            crosshairUI.SetActive(true);
    }

    private void ExitAimMode()
    {
        if (!_isAiming) return;
        _isAiming = false;

        if (gameplayCamera != null)
            gameplayCamera.fieldOfView = _defaultFOV;

        if (crosshairUI != null)
            crosshairUI.SetActive(false);
    }

    // ── Hook fire ──────────────────────────────────────────────────────────────

    private void FireHook()
    {
        ExitAimMode();

        if (_data == null) return;

        Vector3 origin    = GetRayOrigin();
        Vector3 direction = GetAimDirection();

        if (Physics.Raycast(origin, direction, out RaycastHit hit,
                            _data.ropeLength, _data.hookableLayers,
                            QueryTriggerInteraction.Ignore))
        {
            _hookPoint = hit.point;
            AttachHook(hit);
        }
        // On miss just exit aim — no hook is spawned.
    }

    private void AttachHook(RaycastHit hit)
    {
        _isHooked = true;

        // Spawn hook head visual at the impact point.
        if (_data.hookPrefab != null)
            _hookInstance = Instantiate(_data.hookPrefab, hit.point,
                                        Quaternion.LookRotation(hit.normal));

        // Enable rope LineRenderer.
        _lineRenderer.enabled = true;
        UpdateRopeVisual();
    }

    // ── Physics ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Each FixedUpdate: pull the player toward the hook point and apply a
    /// lateral swing force so the player can pendulum around the anchor.
    /// Forces are injected into PlayerMovement.velocity so they blend with
    /// existing movement (walk, jump, air control).
    /// </summary>
    private void ApplyGrapplePhysics()
    {
        if (_playerMovement == null || _data == null) return;

        Vector3 playerPos  = _playerMovement.transform.position;
        Vector3 toAnchor   = (_hookPoint - playerPos);
        float   dist       = toAnchor.magnitude;

        if (dist < 0.01f) return;

        Vector3 pullDir    = toAnchor.normalized;

        // Pull force — stronger when farther away for a snappy feel.
        float pullMag      = _data.pullSpeed * Time.fixedDeltaTime;
        _playerMovement.velocity += pullDir * pullMag;

        // Swing force — perpendicular to pull, in the horizontal plane.
        // This gives the classic pendulum swing when the player moves laterally.
        Vector3 swingAxis  = Vector3.up;
        Vector3 lateralDir = Vector3.ProjectOnPlane(_playerMovement.velocity, pullDir).normalized;
        if (lateralDir.sqrMagnitude > 0.001f)
            _playerMovement.velocity += lateralDir * (_data.swingForce * Time.fixedDeltaTime);

        // Clamp velocity along the rope direction to not overshoot the anchor.
        float ropeComponent = Vector3.Dot(_playerMovement.velocity, pullDir);
        float maxPull       = _data.pullSpeed;
        if (ropeComponent > maxPull)
            _playerMovement.velocity -= pullDir * (ropeComponent - maxPull);

        _ = swingAxis; // suppress unused-variable warning
    }

    // ── Release ────────────────────────────────────────────────────────────────

    private void ReleaseHook()
    {
        if (!_isHooked) return;
        _isHooked = false;

        if (_hookInstance != null)
        {
            Destroy(_hookInstance);
            _hookInstance = null;
        }

        _lineRenderer.enabled = false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Vector3 GetRayOrigin()
    {
        if (gameplayCamera != null) return gameplayCamera.transform.position;
        if (handPoint      != null) return handPoint.position;
        return transform.position;
    }

    private Vector3 GetAimDirection()
    {
        if (gameplayCamera != null) return gameplayCamera.transform.forward;
        if (handPoint      != null) return handPoint.forward;
        return transform.forward;
    }

    private void UpdateRopeVisual()
    {
        if (_lineRenderer == null) return;
        Vector3 ropeOrigin = handPoint != null ? handPoint.position : transform.position;
        _lineRenderer.SetPosition(0, ropeOrigin);
        _lineRenderer.SetPosition(1, _hookPoint);
    }
}
