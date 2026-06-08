using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Randomiz.UI;

/// <summary>
/// Screen-space overlay boss health bar. Built entirely in code — no prefab required.
///
/// On Awake it creates:
///   • A Screen Space - Overlay Canvas (if none already exists for the boss)
///   • A dark background panel centered at the bottom of the screen
///   • A red fill bar that shrinks left as HP drops
///   • A TMP_Text label showing the boss name
///
/// Subscribes to <see cref="OnHealthChangedEvent"/> filtered to the
/// <see cref="HealthSystem"/> component on the same (or assigned) GameObject.
/// </summary>
public class BossHealthBar : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Name shown above the HP bar.")]
    [SerializeField] private string bossName = "Golem";
    [Tooltip("Leave null to auto-resolve from this GameObject.")]
    [SerializeField] private HealthSystem targetHealth;

    [Header("Layout")]
    [SerializeField] private Vector2 barSize       = new Vector2(600f, 28f);
    [SerializeField] private float   bottomPadding = 60f;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.85f);
    [SerializeField] private Color fillColor        = new Color(0.85f, 0.12f, 0.12f, 1f);
    [SerializeField] private Color textColor        = Color.white;

    // ── Runtime ──────────────────────────────────────────────────────────────

    private Image   _fillImage;
    private Canvas  _canvas;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (targetHealth == null)
            targetHealth = GetComponent<HealthSystem>();

        BuildUI();
    }

    private void OnEnable()  => EventBus.Subscribe<OnHealthChangedEvent>(OnHealthChanged);
    private void OnDisable() => EventBus.Unsubscribe<OnHealthChangedEvent>(OnHealthChanged);

    private void OnDestroy()
    {
        if (_canvas != null)
            Destroy(_canvas.gameObject);
    }

    // ── Event handler ─────────────────────────────────────────────────────────

    private void OnHealthChanged(OnHealthChangedEvent e)
    {
        if (targetHealth == null || e.target != targetHealth.gameObject) return;

        float normalizedHp = e.maxHearts > 0 ? e.currentHealth / (e.maxHearts * 4f) : 0f;
        if (_fillImage != null)
            _fillImage.fillAmount = Mathf.Clamp01(normalizedHp);

        // Hide the bar when the boss is dead.
        if (_canvas != null)
            _canvas.gameObject.SetActive(normalizedHp > 0f);
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUI()
    {
        // Root canvas — Screen Space Overlay so it always renders on top.
        GameObject canvasGO = new GameObject("BossHealthBarCanvas");
        DontDestroyOnLoad(canvasGO);  // survives scene transitions (optional; remove if not needed)
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;   // above normal HUD

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Container panel — anchored at the bottom center.
        GameObject panel = new GameObject("BossBarPanel");
        panel.transform.SetParent(canvasGO.transform, false);

        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(0.5f, 0f);
        panelRect.anchorMax        = new Vector2(0.5f, 0f);
        panelRect.pivot            = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, bottomPadding);
        // Add a little vertical room for the label above the bar.
        panelRect.sizeDelta        = new Vector2(barSize.x, barSize.y + 28f);

        // Boss name label.
        GameObject labelGO = new GameObject("BossNameLabel");
        labelGO.transform.SetParent(panel.transform, false);

        RectTransform labelRect    = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin        = new Vector2(0f, 1f);
        labelRect.anchorMax        = new Vector2(1f, 1f);
        labelRect.pivot            = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = new Vector2(0f, 4f);
        labelRect.sizeDelta        = new Vector2(0f, 26f);

        TMP_Text label   = labelGO.AddComponent<TextMeshProUGUI>();
        label.text        = bossName;
        label.alignment   = TextAlignmentOptions.Center;
        label.color        = textColor;
        label.fontSize    = 20f;

        // Background bar.
        GameObject bg          = new GameObject("BossBarBG");
        bg.transform.SetParent(panel.transform, false);
        RectTransform bgRect   = bg.AddComponent<RectTransform>();
        bgRect.anchorMin       = new Vector2(0f, 0f);
        bgRect.anchorMax       = new Vector2(1f, 0f);
        bgRect.pivot           = new Vector2(0.5f, 0f);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta        = new Vector2(0f, barSize.y);

        // Optional themed skin for the bar track + fill.
        var theme = UITheme.Current;

        Image bgImage   = bg.AddComponent<Image>();
        bgImage.color    = backgroundColor;
        if (theme != null && theme.bossBarBackgroundSprite != null)
        {
            bgImage.sprite = theme.bossBarBackgroundSprite;
            bgImage.type   = Image.Type.Sliced;
        }

        // Fill bar (child of background so it stretches within it).
        GameObject fill          = new GameObject("BossBarFill");
        fill.transform.SetParent(bg.transform, false);
        RectTransform fillRect   = fill.AddComponent<RectTransform>();
        fillRect.anchorMin       = Vector2.zero;
        fillRect.anchorMax       = Vector2.one;
        fillRect.offsetMin       = new Vector2(2f,  2f);
        fillRect.offsetMax       = new Vector2(-2f, -2f);

        _fillImage            = fill.AddComponent<Image>();
        _fillImage.color       = fillColor;
        if (theme != null && theme.bossBarFillSprite != null)
            _fillImage.sprite = theme.bossBarFillSprite;
        _fillImage.type        = Image.Type.Filled;
        _fillImage.fillMethod  = Image.FillMethod.Horizontal;
        _fillImage.fillOrigin  = (int)Image.OriginHorizontal.Left;
        _fillImage.fillAmount  = 1f;
    }
}
