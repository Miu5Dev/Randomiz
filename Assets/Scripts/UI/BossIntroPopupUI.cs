using System.Collections;
using Randomiz.UI;
using TMPro;
using UnityEngine;

/// <summary>
/// Center-screen boss intro banner. Subscribes to <see cref="OnBossEncounterStartedEvent"/>
/// (raised by <see cref="BossArea"/>) and flashes the boss name in the middle of the
/// screen — scale + fade in, hold, fade out. Built entirely in code via
/// <see cref="UIFactory"/>; never blocks gameplay raycasts. The Setup Wizard adds one to
/// the scene automatically.
/// </summary>
public class BossIntroPopupUI : MonoBehaviour
{
    [Header("Text")]
    [Tooltip("Small label shown above the boss name.")]
    [SerializeField] private string subtitle = "BOSS";

    [Header("Timing (seconds, unscaled)")]
    [SerializeField] private float introTime = 0.5f;
    [SerializeField] private float holdTime  = 2.2f;
    [SerializeField] private float outroTime = 0.7f;

    private Canvas        _canvas;
    private CanvasGroup   _group;
    private RectTransform _root;
    private TMP_Text      _nameLabel;
    private Coroutine     _running;

    private void Awake()
    {
        BuildUI();
        _group.alpha = 0f;
        _canvas.gameObject.SetActive(false);
    }

    private void OnEnable()  => EventBus.Subscribe<OnBossEncounterStartedEvent>(OnEncounter);
    private void OnDisable() => EventBus.Unsubscribe<OnBossEncounterStartedEvent>(OnEncounter);

    private void OnEncounter(OnBossEncounterStartedEvent e)
    {
        if (e == null || string.IsNullOrEmpty(e.bossName)) return;
        _nameLabel.text = e.bossName;
        if (_running != null) StopCoroutine(_running);
        _running = StartCoroutine(Show());
    }

    // ─── Animation ───────────────────────────────────────────────────────────

    private IEnumerator Show()
    {
        _canvas.gameObject.SetActive(true);

        // Intro — scale pop + fade.
        float t = 0f;
        while (t < introTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / introTime);
            _group.alpha     = k;
            _root.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, EaseOutBack(k));
            yield return null;
        }
        _group.alpha = 1f;
        _root.localScale = Vector3.one;

        yield return new WaitForSecondsRealtime(holdTime);

        // Outro — fade.
        t = 0f;
        while (t < outroTime)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = 1f - Mathf.Clamp01(t / outroTime);
            yield return null;
        }

        _group.alpha = 0f;
        _canvas.gameObject.SetActive(false);
        _running = null;
    }

    // Overshoot easing for a lively "pop".
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float xm = x - 1f;
        return 1f + c3 * xm * xm * xm + c1 * xm * xm;
    }

    // ─── Construction ────────────────────────────────────────────────────────

    private void BuildUI()
    {
        _canvas = UIFactory.CreateCanvas("BossIntroCanvas");
        _canvas.sortingOrder = 120; // above HUD/pickup(95), below dialogue(200)/death(300)
        _canvas.transform.SetParent(transform, false);

        var rootGo = new GameObject("BossIntro", typeof(RectTransform), typeof(CanvasGroup));
        rootGo.transform.SetParent(_canvas.transform, false);
        _root = rootGo.GetComponent<RectTransform>();
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.55f);
        _root.pivot     = new Vector2(0.5f, 0.5f);
        _root.sizeDelta = new Vector2(1200f, 220f);

        _group = rootGo.GetComponent<CanvasGroup>();
        _group.blocksRaycasts = false; // never eat gameplay/menu clicks
        _group.interactable   = false;

        Color accent = UITheme.Current != null ? UITheme.Current.accent : new Color(1f, 0.92f, 0.6f);

        // Subtitle ("BOSS") at the top.
        var sub = UIFactory.CreateLabel(_root, subtitle, 28, accent);
        var sRt = sub.rectTransform;
        sRt.anchorMin = sRt.anchorMax = new Vector2(0.5f, 1f);
        sRt.pivot            = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = Vector2.zero;
        sRt.sizeDelta        = new Vector2(800f, 40f);
        sub.fontStyle        = FontStyles.UpperCase | FontStyles.Bold;
        sub.characterSpacing = 8f;

        // Boss name (centered, large).
        _nameLabel = UIFactory.CreateLabel(_root, "", 84, Color.white);
        var nRt = _nameLabel.rectTransform;
        nRt.anchorMin = nRt.anchorMax = new Vector2(0.5f, 0.5f);
        nRt.pivot            = new Vector2(0.5f, 0.5f);
        nRt.anchoredPosition = new Vector2(0f, -10f);
        nRt.sizeDelta        = new Vector2(1200f, 120f);
        _nameLabel.fontStyle = FontStyles.Bold;
        _nameLabel.enableWordWrapping = false;

        // Accent underline bar (plain image, not themed, so it stays a crisp line).
        var bar = UIFactory.CreateImage(_root, null, accent);
        var bRt = bar.rectTransform;
        bRt.anchorMin = bRt.anchorMax = new Vector2(0.5f, 0f);
        bRt.pivot            = new Vector2(0.5f, 0.5f);
        bRt.anchoredPosition = new Vector2(0f, 20f);
        bRt.sizeDelta        = new Vector2(420f, 4f);
        bar.raycastTarget    = false;
    }
}
