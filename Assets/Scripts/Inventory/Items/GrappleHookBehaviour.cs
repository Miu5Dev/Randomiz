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

        // Physics is owned by PlayerMovement.TickGrapple (so it isn't stomped by the
        // normal locomotion that overwrites velocity each frame). We only watch for
        // arrival here and release.
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

        // Fall back to "everything" when the asset's hookable layers are left unset,
        // so the hook still attaches instead of silently doing nothing.
        LayerMask mask = _data.hookableLayers.value == 0 ? ~0 : _data.hookableLayers;

        // SphereCast (radius 0.08) instead of Raycast: prevents the hook from
        // clipping through thin walls when the ray origin is near geometry.
        const float castRadius = 0.08f;
        if (Physics.SphereCast(origin, castRadius, direction, out RaycastHit hit,
                               _data.ropeLength, mask,
                               QueryTriggerInteraction.Ignore))
        {
            // Never hook onto the player itself.
            if (_playerMovement != null &&
                hit.collider.transform.root == _playerMovement.transform.root)
                return;

            // Validate line-of-sight from the player's head to the hit point.
            // The camera origin can sit inside or behind thin walls in tight spaces,
            // causing the hook to land on an unreachable surface.
            Vector3 playerHead = _playerMovement != null
                ? _playerMovement.transform.position + Vector3.up * 1.5f
                : origin;

            if (Physics.Linecast(playerHead, hit.point, out RaycastHit blocker,
                                 mask, QueryTriggerInteraction.Ignore)
                && blocker.collider != hit.collider
                && (_playerMovement == null ||
                    blocker.collider.transform.root != _playerMovement.transform.root))
            {
                // Something blocks the path from the player — attach to the near
                // surface instead so the hook always lands somewhere reachable.
                AttachHook(blocker);
            }
            else
            {
                AttachHook(hit);
            }
        }
        // On miss just exit aim — no hook is spawned.
    }

    private void AttachHook(RaycastHit hit)
    {
        _isHooked  = true;
        _hookPoint = hit.point;

        if (_data.hookPrefab != null)
            _hookInstance = Instantiate(_data.hookPrefab, hit.point,
                                        Quaternion.LookRotation(hit.normal));

        _lineRenderer.enabled = true;
        UpdateRopeVisual();

        if (_playerMovement != null)
            _playerMovement.BeginGrapple(_hookPoint, _data.pullSpeed, _data.swingForce);
    }

// ── Release ────────────────────────────────────────────────────────────────

    private void ReleaseHook()
    {
        if (!_isHooked) return;
        _isHooked = false;

        if (_playerMovement != null)
            _playerMovement.EndGrapple();

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
