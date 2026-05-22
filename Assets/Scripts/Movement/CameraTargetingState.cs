using System.Collections;
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

    // ────────────────────────────────────────────────────────────────────────
    // INTERNAL STATE — Letterbox
    // ────────────────────────────────────────────────────────────────────────
    private Canvas _letterboxCanvas;
    private RectTransform _topBar;
    private RectTransform _bottomBar;
    private Coroutine _letterboxRoutine;

    private void Awake()
    {
        if (cameraToZoom == null) cameraToZoom = Camera.main;

        if (cameraToZoom != null)
        {
            _baseFOV = cameraToZoom.fieldOfView;
            _currentFOV = _baseFOV;
        }

        if (cameraRig == null && cameraToZoom != null)
            cameraRig = cameraToZoom.transform;

        if (cameraRig != null)
        {
            _baseLocalZ = cameraRig.localPosition.z;
            _currentLocalZ = _baseLocalZ;
        }

        BuildLetterbox();
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

        if (zoomMode == ZoomMode.FieldOfView)
        {
            float desiredFOV = _active ? targetingFOV : _baseFOV;
            _currentFOV = Mathf.SmoothDamp(_currentFOV, desiredFOV, ref _fovVelocity, zoomSmoothTime);
            cameraToZoom.fieldOfView = _currentFOV;
        }
        else if (cameraRig != null)
        {
            float desiredZ = _active ? _baseLocalZ * targetingDistanceMultiplier : _baseLocalZ;
            _currentLocalZ = Mathf.SmoothDamp(_currentLocalZ, desiredZ, ref _localZVelocity, zoomSmoothTime);
            Vector3 lp = cameraRig.localPosition;
            lp.z = _currentLocalZ;
            cameraRig.localPosition = lp;
        }
    }
}
