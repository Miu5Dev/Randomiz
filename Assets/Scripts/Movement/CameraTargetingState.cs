using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Camera-side reaction to the targeting system.
/// While the player is locked on, this component:
///   • Zooms the camera in (FieldOfView or distance to the CameraTarget).
///   • Shows cinematic letterbox bars (top/bottom UI panels).
/// Restores everything smoothly when targeting ends.
/// Compatible with the existing third-person Camera that follows a CameraTarget transform.
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

    [Tooltip("How fast the zoom transitions in/out (higher = snappier).")]
    [SerializeField] private float zoomLerpSpeed = 6f;

    [Header("Letterbox")]
    [Tooltip("Top black bar. RectTransform with a black Image, anchored to the top, height = 0 by default.")]
    [SerializeField] private RectTransform topBar;

    [Tooltip("Bottom black bar. RectTransform with a black Image, anchored to the bottom, height = 0 by default.")]
    [SerializeField] private RectTransform bottomBar;

    [Tooltip("Final bar height as a fraction of screen height (0..0.5). 0.10 ≈ 10% of the screen on top and bottom.")]
    [Range(0f, 0.3f)]
    [SerializeField] private float barHeightPercent = 0.10f;

    [Tooltip("If true, uses an absolute pixel height instead of barHeightPercent.")]
    [SerializeField] private bool useAbsolutePixels = false;

    [Tooltip("Absolute bar height in pixels when useAbsolutePixels = true.")]
    [SerializeField] private float barHeightPixels = 90f;

    [Tooltip("How fast the letterbox bars open / close (higher = snappier).")]
    [SerializeField] private float barLerpSpeed = 8f;

    // ────────────────────────────────────────────────────────────────────────
    // INTERNAL STATE
    // ────────────────────────────────────────────────────────────────────────
    private float _baseFOV;
    private float _baseLocalZ;
    private float _currentBarHeight;
    private float _targetBarHeight;
    private bool _active;

    private void Awake()
    {
        if (cameraToZoom == null) cameraToZoom = Camera.main;

        if (cameraToZoom != null)
            _baseFOV = cameraToZoom.fieldOfView;

        if (cameraRig == null && cameraToZoom != null)
            cameraRig = cameraToZoom.transform;

        if (cameraRig != null)
            _baseLocalZ = cameraRig.localPosition.z;

        InitBar(topBar);
        InitBar(bottomBar);
    }

    private void OnEnable()
    {
        if (targetingSystem != null)
            targetingSystem.OnTargetingChanged += HandleTargetingChanged;
    }

    private void OnDisable()
    {
        if (targetingSystem != null)
            targetingSystem.OnTargetingChanged -= HandleTargetingChanged;
    }

    private void InitBar(RectTransform bar)
    {
        if (bar == null) return;
        Vector2 size = bar.sizeDelta;
        size.y = 0f;
        bar.sizeDelta = size;
    }

    private void HandleTargetingChanged(bool active)
    {
        _active = active;
        _targetBarHeight = active ? ResolveTargetBarHeight() : 0f;
    }

    private float ResolveTargetBarHeight()
    {
        if (useAbsolutePixels) return barHeightPixels;
        return Mathf.Max(0f, Screen.height * barHeightPercent);
    }

    private void LateUpdate()
    {
        // ── Zoom ────────────────────────────────────────────────────────────
        if (cameraToZoom != null)
        {
            if (zoomMode == ZoomMode.FieldOfView)
            {
                float desiredFOV = _active ? targetingFOV : _baseFOV;
                cameraToZoom.fieldOfView = Mathf.Lerp(
                    cameraToZoom.fieldOfView, desiredFOV, zoomLerpSpeed * Time.deltaTime);
            }
            else if (cameraRig != null)
            {
                float desiredZ = _active ? _baseLocalZ * targetingDistanceMultiplier : _baseLocalZ;
                Vector3 lp = cameraRig.localPosition;
                lp.z = Mathf.Lerp(lp.z, desiredZ, zoomLerpSpeed * Time.deltaTime);
                cameraRig.localPosition = lp;
            }
        }

        // ── Letterbox bars ──────────────────────────────────────────────────
        if (_active) _targetBarHeight = ResolveTargetBarHeight();
        _currentBarHeight = Mathf.Lerp(_currentBarHeight, _targetBarHeight, barLerpSpeed * Time.deltaTime);

        if (Mathf.Abs(_currentBarHeight - _targetBarHeight) < 0.5f)
            _currentBarHeight = _targetBarHeight;

        SetBarHeight(topBar, _currentBarHeight);
        SetBarHeight(bottomBar, _currentBarHeight);
    }

    private static void SetBarHeight(RectTransform bar, float height)
    {
        if (bar == null) return;
        Vector2 size = bar.sizeDelta;
        if (Mathf.Approximately(size.y, height)) return;
        size.y = height;
        bar.sizeDelta = size;
    }
}
