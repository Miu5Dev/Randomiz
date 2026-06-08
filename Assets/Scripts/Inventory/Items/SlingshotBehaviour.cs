using UnityEngine;

/// <summary>
/// MonoBehaviour attached to the SlingshotItem GameObject (child of the player's
/// equip slot). Listens for equip/unequip events and manages the full aim →
/// charge → fire loop using attack input.
///
/// Aim state  : hold attack button → zoom FOV, show crosshair.
/// Fire        : release attack button while aiming → launch stone with arc.
/// Unequip     : immediately exits aim mode and restores camera FOV.
///
/// Ammo: unlimited. Stones are pooled via simple Instantiate/Destroy for now.
/// </summary>
public class SlingshotBehaviour : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Transform at the tip of the slingshot from which stones are launched.")]
    [SerializeField] private Transform firePoint;

    [Tooltip("Camera whose FOV is adjusted during aim. Assign the main gameplay camera.")]
    [SerializeField] private Camera gameplayCamera;

    [Tooltip("Optional crosshair UI GameObject shown while aiming.")]
    [SerializeField] private GameObject crosshairUI;

    // ── Private state ──────────────────────────────────────────────────────────

    private SOSlingShot _data;            // SO asset for this slingshot
    private bool        _isEquipped;
    private bool        _isAiming;
    private bool        _attackHeld;
    private float       _defaultFOV;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;

        if (gameplayCamera != null)
            _defaultFOV = gameplayCamera.fieldOfView;
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

        // Safety: restore state if this component is disabled while aiming.
        ExitAimMode();
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnItemEquip(OnItemEquipEvent e)
    {
        if (e.item is SOSlingShot ss)
        {
            _data       = ss;
            _isEquipped = true;
        }
        else if (_isEquipped)
        {
            // Another item was equipped — treat as unequip for us.
            _isEquipped = false;
            ExitAimMode();
        }
    }

    private void OnItemUnequip(OnItemUnequipEvent e)
    {
        if (e.item is SOSlingShot)
        {
            _isEquipped = false;
            _data       = null;
            ExitAimMode();
        }
    }

    private void OnAttackInput(OnAttackInputEvent e)
    {
        if (!_isEquipped || _data == null) return;

        // Gate: player must be able to act (no wallhug, ledge grab, etc.)
        if (PlayerStateMachine.Instance != null && !PlayerStateMachine.Instance.CanAct) return;

        if (e.pressed)
        {
            // Hold begins: enter aim mode.
            _attackHeld = true;
            EnterAimMode();
        }
        else
        {
            // Release: fire only if we were actually aiming.
            if (_isAiming && _attackHeld)
                FireStone();

            _attackHeld = false;
        }
    }

    // ── Aim mode ───────────────────────────────────────────────────────────────

    private void EnterAimMode()
    {
        if (_isAiming) return;
        _isAiming = true;

        if (gameplayCamera != null && _data != null)
            gameplayCamera.fieldOfView = _data.aimFOV;

        if (crosshairUI != null)
            crosshairUI.SetActive(true);
    }

    private void ExitAimMode()
    {
        if (!_isAiming) return;
        _isAiming   = false;
        _attackHeld = false;

        if (gameplayCamera != null)
            gameplayCamera.fieldOfView = _defaultFOV;

        if (crosshairUI != null)
            crosshairUI.SetActive(false);
    }

    // ── Projectile launch ──────────────────────────────────────────────────────

    private void FireStone()
    {
        ExitAimMode();

        if (_data == null || _data.stonePrefab == null)
        {
            Debug.LogWarning("[SlingshotBehaviour] stonePrefab is not assigned on the SOSlingShot asset.");
            return;
        }

        Transform origin = firePoint != null ? firePoint : transform;
        Vector3 fireDir  = GetAimDirection(origin);

        GameObject stone = Instantiate(_data.stonePrefab, origin.position, Quaternion.LookRotation(fireDir));

        SlingshotStone stoneComp = stone.GetComponent<SlingshotStone>();
        if (stoneComp != null)
        {
            stoneComp.Launch(gameObject, fireDir, _data.projectileSpeed, _data.damage);
        }
        else
        {
            // Fallback: just add a velocity via Rigidbody if no SlingshotStone component.
            Rigidbody rb = stone.GetComponent<Rigidbody>();
            if (rb != null)
                rb.linearVelocity = fireDir * _data.projectileSpeed;
            else
                Debug.LogWarning("[SlingshotBehaviour] stonePrefab has neither SlingshotStone nor Rigidbody.");
        }
    }

    /// <summary>
    /// Returns the world-space aim direction. Prioritises the camera's forward
    /// direction (so the shot goes where the player is looking) but falls back to
    /// the fire point's forward if no camera is available.
    /// </summary>
    private Vector3 GetAimDirection(Transform origin)
    {
        if (gameplayCamera != null)
            return gameplayCamera.transform.forward;

        return origin.forward;
    }
}
