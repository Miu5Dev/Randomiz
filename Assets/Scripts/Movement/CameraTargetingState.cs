using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Camera-side reaction to the targeting system.
/// While the player is locked on, this component:
///   • Smoothly zooms the camera (FieldOfView or distance) using SmoothDamp.
///   • Shows cinematic letterbox bars created entirely by code (no scene UI needed).
/// Restores everything smoothly when targeting ends.
/// </summary>
[DisallowMultipleComponent]
public class CameraTargetingState : MonoBehaviour
{
    public enum ZoomMode
    {
        FieldOfView,
        Distance
    }

    [Header("References")]
    [Tooltip("Targeting system that drives the on/off state. Required.")]
    [SerializeField] private TargetingSystem targetingSystem;

    [Tooltip("The actual Camera component that will get zoomed.")]
    [SerializeField] private Camera cameraToZoom;

    [Tooltip("Optional: only used in Distance mode. The Transform that follows the CameraTarget (usually the camera's parent rig or the camera itself when offset on local Z).")]
    [SerializeField] private Transform cameraRig;

    [Header("Zoom")]
    [SerializeField] private ZoomMode zoomMode = ZoomMode.FieldOfView;

    [Tooltip("Target FOV while locked-on (FieldOfView mode).")]
    [SerializeField] private float targetingFOV = 45f;

    [Tooltip("Distance multiplier applied to the camera's local Z while locked-on (Distance mode). 0.7 = 30% closer.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float targetingDistanceMultiplier = 0.7f;

    [Tooltip("SmoothDamp time (seconds) for the zoom transition. ~0.3 = smooth cinematic feel.")]
    [SerializeField] private float zoomSmoothTime = 0.3f;

    [Header("Letterbox")]
    [Tooltip("Bar height in pixels (top and bottom).")]
    [SerializeField] private float barHeightPixels = 90f;

    [Tooltip("Total time for the bars to enter or exit, in seconds.")]
    [SerializeField] private float letterboxDuration = 0.4f;

    [Tooltip("Easing curve applied to the bar entry/exit animation.")]
    [SerializeField] private AnimationCurve letterboxCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Sorting order of the procedural Canvas. Keep it high so the bars sit above gameplay UI.")]
    [SerializeField] private int letterboxSortingOrder = 999;

    [Header("Ranged Aim (slingshot / grapple)")]
    [Tooltip("When a ranged weapon is equipped AND the player is targeting, the camera moves " +
             "to this local Z (≈ first person) so the shot lines up with the crosshair.")]
    [SerializeField] private float firstPersonLocalZ = 0.2f;

    [Tooltip("Show a procedural crosshair while aiming a ranged weapon.")]
    [SerializeField] private bool showCrosshair = true;

    [Tooltip("Crosshair size in pixels.")]
    [SerializeField] private float crosshairSize = 28f;

    [Tooltip("Crosshair color.")]
    [SerializeField] private Color crosshairColor = new(1f, 1f, 1f, 0.85f);

    // ────────────────────────────────────────────────────────────────────────
    // INTERNAL STATE — Zoom (SmoothDamp)
    // ────────────────────────────────────────────────────────────────────────
    private float _baseFOV;
    private float _baseLocalZ;
    private float _currentFOV;
    private float _fovVelocity;
    private float _currentLocalZ;
    private float _localZVelocity;
    private bool _active;

    // Cinemachine path: drive CameraDistance instead of localPosition.z.
    private CinemachineThirdPersonFollow _thirdPersonFollow;
    private float _baseCameraDistance;
    private float _currentCameraDistance;
    private float _cameraDistanceVelocity;

    // ────────────────────────────────────────────────────────────────────────
    // INTERNAL STATE — Letterbox
    // ────────────────────────────────────────────────────────────────────────
    private Canvas _letterboxCanvas;
    private RectTransform _topBar;
    private RectTransform _bottomBar;
    private Coroutine _letterboxRoutine;

    // ────────────────────────────────────────────────────────────────────────
    // INTERNAL STATE — Ranged aim
    // ────────────────────────────────────────────────────────────────────────
    private bool _rangedEquipped;            // slingshot or grapple in hand
    private bool _wasAiming;                 // previous-frame aiming state for edge detection
    private GameObject _crosshair;

    // True if cameraRig was explicitly set in the inspector; false = no-op to avoid
    // fighting with CinemachineBrain that also drives the camera every LateUpdate.
    private bool _cameraRigExplicit;

    private void Awake()
    {
        if (cameraToZoom == null) cameraToZoom = Camera.main;

        if (cameraToZoom != null)
        {
            _baseFOV = cameraToZoom.fieldOfView;
            _currentFOV = _baseFOV;
        }

        // Only take control of local Z if the user explicitly assigned a rig.
        // Auto-assigning Camera.main here would fight CinemachineBrain every frame.
        _cameraRigExplicit = cameraRig != null;
        if (cameraRig != null)
        {
            _baseLocalZ = cameraRig.localPosition.z;
            _currentLocalZ = _baseLocalZ;
        }

        // Cinemachine 3.x path: preferred over localPosition.z manipulation.
        // CameraTargetingState lives on [CAM] (the VCam) which also has ThirdPersonFollow.
        _thirdPersonFollow = GetComponent<CinemachineThirdPersonFollow>();
        if (_thirdPersonFollow == null)
            _thirdPersonFollow = GetComponentInParent<CinemachineThirdPersonFollow>();
        if (_thirdPersonFollow != null)
        {
            _baseCameraDistance    = _thirdPersonFollow.CameraDistance;
            _currentCameraDistance = _baseCameraDistance;
        }

        BuildLetterbox();
    }

    private void OnEnable()
    {
        if (targetingSystem != null)
            targetingSystem.OnTargetingChanged += HandleTargetingChanged;
        EventBus.Subscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Subscribe<OnItemUnequipEvent>(OnItemUnequip);
    }

    private void OnDisable()
    {
        if (targetingSystem != null)
            targetingSystem.OnTargetingChanged -= HandleTargetingChanged;
        EventBus.Unsubscribe<OnItemEquipEvent>(OnItemEquip);
        EventBus.Unsubscribe<OnItemUnequipEvent>(OnItemUnequip);
    }

    private void OnItemEquip(OnItemEquipEvent e) =>
        _rangedEquipped = e.item is SOSlingShot || e.item is SOGrappleHook;

    private void OnItemUnequip(OnItemUnequipEvent e) => _rangedEquipped = false;

    // ────────────────────────────────────────────────────────────────────────
    // PROCEDURAL LETTERBOX BUILD
    // ────────────────────────────────────────────────────────────────────────
    private void BuildLetterbox()
    {
        var canvasGO = new GameObject("TargetingLetterboxCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        _letterboxCanvas = canvasGO.GetComponent<Canvas>();
        _letterboxCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _letterboxCanvas.sortingOrder = letterboxSortingOrder;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var raycaster = canvasGO.GetComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        _topBar = CreateBar("LetterboxTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        _bottomBar = CreateBar("LetterboxBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));

        // Start off-screen (height = barHeightPixels but pivoted so they sit just beyond the edge).
        PositionBar(_topBar, 0f);
        PositionBar(_bottomBar, 0f);

        BuildCrosshair();
    }

    // Simple centered "+" crosshair (two bars) on the same canvas. Hidden by default.
    private void BuildCrosshair()
    {
        _crosshair = new GameObject("Crosshair", typeof(RectTransform));
        var rt = (RectTransform)_crosshair.transform;
        rt.SetParent(_letterboxCanvas.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(crosshairSize, crosshairSize);

        float thickness = Mathf.Max(2f, crosshairSize * 0.08f);
        MakeCrossBar(rt, new Vector2(crosshairSize, thickness));   // horizontal
        MakeCrossBar(rt, new Vector2(thickness, crosshairSize));   // vertical

        _crosshair.SetActive(false);
    }

    private void MakeCrossBar(RectTransform parent, Vector2 size)
    {
        var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = crosshairColor;
        img.raycastTarget = false;
    }

    private RectTransform CreateBar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(_letterboxCanvas.transform, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(0f, barHeightPixels);
        rt.anchoredPosition = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;

        return rt;
    }

    /// <summary>
    /// Drives bar visibility by a 0..1 progress value.
    /// 0 = fully off-screen, 1 = fully on-screen.
    /// </summary>
    private void PositionBar(RectTransform bar, float progress)
    {
        if (bar == null) return;
        // Size stays constant; we slide the bar in via anchoredPosition so the motion is purely vertical.
        // Top bar: anchored at top with pivot (0.5, 1). anchoredPosition.y = 0 → fully visible. Positive y pushes it up (off-screen).
        // Bottom bar: anchored at bottom with pivot (0.5, 0). anchoredPosition.y = 0 → fully visible. Negative y pushes it down (off-screen).
        float hiddenOffset = barHeightPixels;
        float visibleY = 0f;
        float hiddenY = bar == _topBar ? hiddenOffset : -hiddenOffset;
        float y = Mathf.Lerp(hiddenY, visibleY, progress);
        bar.anchoredPosition = new Vector2(0f, y);
    }

    // ────────────────────────────────────────────────────────────────────────
    // TARGETING REACTION
    // ────────────────────────────────────────────────────────────────────────
    private void HandleTargetingChanged(bool active)
    {
        _active = active;

        if (_letterboxRoutine != null)
            StopCoroutine(_letterboxRoutine);
        _letterboxRoutine = StartCoroutine(AnimateLetterbox(active));
    }

    private IEnumerator AnimateLetterbox(bool show)
    {
        // Determine starting progress from current bar position so a mid-animation reverse is smooth.
        float startProgress = InferCurrentProgress();
        float endProgress = show ? 1f : 0f;

        if (Mathf.Approximately(startProgress, endProgress))
            yield break;

        float distance = Mathf.Abs(endProgress - startProgress);
        float duration = Mathf.Max(0.0001f, letterboxDuration * distance);

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            float eased = letterboxCurve.Evaluate(k);
            float progress = Mathf.Lerp(startProgress, endProgress, eased);

            PositionBar(_topBar, progress);
            PositionBar(_bottomBar, progress);
            yield return null;
        }

        PositionBar(_topBar, endProgress);
        PositionBar(_bottomBar, endProgress);
        _letterboxRoutine = null;
    }

    private float InferCurrentProgress()
    {
        if (_topBar == null) return 0f;
        float hiddenOffset = barHeightPixels;
        float y = _topBar.anchoredPosition.y;
        // y goes from hiddenOffset (hidden) → 0 (shown). Map to progress 0..1.
        float p = 1f - Mathf.Clamp01(y / Mathf.Max(0.0001f, hiddenOffset));
        return p;
    }

    // ────────────────────────────────────────────────────────────────────────
    // UPDATE (zoom only — letterbox runs in coroutine)
    // ────────────────────────────────────────────────────────────────────────
    private void LateUpdate()
    {
        if (cameraToZoom == null) return;

        // Aiming = targeting while a ranged weapon is in hand → go first person.
        bool aiming = _active && _rangedEquipped;

        // Block movement while aiming, restore when done.
        if (aiming != _wasAiming)
        {
            EventBus.Raise(new OnSetMovementEnabledEvent { enabled = !aiming });
            _wasAiming = aiming;
        }

        if (_crosshair != null)
            _crosshair.SetActive(showCrosshair && aiming);

        // ── Camera distance: Cinemachine ThirdPersonFollow path (preferred).
        if (_thirdPersonFollow != null)
        {
            float desiredDist;
            if (aiming)                                           desiredDist = firstPersonLocalZ;
            else if (zoomMode == ZoomMode.Distance && _active)    desiredDist = _baseCameraDistance * targetingDistanceMultiplier;
            else                                                  desiredDist = _baseCameraDistance;

            _currentCameraDistance = Mathf.SmoothDamp(_currentCameraDistance, desiredDist, ref _cameraDistanceVelocity, zoomSmoothTime);
            _thirdPersonFollow.CameraDistance = _currentCameraDistance;
        }
        // ── Legacy path: direct Transform rig control (non-Cinemachine setups).
        // Only run when cameraRig was explicitly assigned — auto-assigning Camera.main
        // would fight CinemachineBrain that also drives the camera in LateUpdate.
        else if (_cameraRigExplicit && cameraRig != null)
        {
            float desiredZ;
            if (aiming)                                        desiredZ = firstPersonLocalZ;
            else if (zoomMode == ZoomMode.Distance && _active) desiredZ = _baseLocalZ * targetingDistanceMultiplier;
            else                                               desiredZ = _baseLocalZ;

            _currentLocalZ = Mathf.SmoothDamp(_currentLocalZ, desiredZ, ref _localZVelocity, zoomSmoothTime);
            Vector3 lp = cameraRig.localPosition;
            lp.z = _currentLocalZ;
            cameraRig.localPosition = lp;
        }

        // ── FOV zoom (FieldOfView mode only). Weapon behaviours do their own aim-FOV
        //    zoom, so we leave FOV at base while free-aiming. ──
        if (zoomMode == ZoomMode.FieldOfView)
        {
            float desiredFOV = (_active && !aiming) ? targetingFOV : _baseFOV;
            _currentFOV = Mathf.SmoothDamp(_currentFOV, desiredFOV, ref _fovVelocity, zoomSmoothTime);
            cameraToZoom.fieldOfView = _currentFOV;
        }
    }
}
