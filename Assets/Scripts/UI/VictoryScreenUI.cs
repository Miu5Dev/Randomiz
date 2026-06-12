using System.Collections;
using Randomiz.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Victory screen shown when the boss is defeated. Displays run time and death count
/// alongside Play Again and Exit to Main Menu buttons.
///
/// Built entirely in code via UIFactory. The Setup Wizard adds it automatically.
/// </summary>
public class VictoryScreenUI : MonoBehaviour
{
    [Header("Timing (seconds, unscaled)")]
    [Tooltip("Delay after boss death before the screen appears (lets death animation play).")]
    [SerializeField] private float victoryDelay = 2.0f;
    [SerializeField] private float fadeTime     = 1.0f;

    [Header("Scene")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    private Canvas      _canvas;
    private CanvasGroup _fadeGroup;
    private GameObject  _optionsRoot;
    private TMP_Text    _timeLabel;
    private TMP_Text    _deathsLabel;
    private GameObject  _firstSelected;
    private bool        _active;

    private void Awake()
    {
        BuildUI();
        _canvas.gameObject.SetActive(false);
    }

    private void OnEnable()  => EventBus.Subscribe<OnBossEncounterEndedEvent>(OnBossEncounterEnded);
    private void OnDisable() => EventBus.Unsubscribe<OnBossEncounterEndedEvent>(OnBossEncounterEnded);

    // ─── Boss defeated ────────────────────────────────────────────────────────

    private void OnBossEncounterEnded(OnBossEncounterEndedEvent e)
    {
        if (!e.defeated || _active) return;
        _active = true;
        StartCoroutine(VictorySequence());
    }

    private IEnumerator VictorySequence()
    {
        yield return new WaitForSecondsRealtime(victoryDelay);

        PopulateStats();

        EventBus.Raise(new OnSetMovementEnabledEvent { enabled = false });

        _canvas.gameObject.SetActive(true);
        _optionsRoot.SetActive(false);
        _fadeGroup.alpha = 0f;

        yield return Fade(0f, 1f, fadeTime);

        Time.timeScale = 0f;
        ShowCursor(true);
        _optionsRoot.SetActive(true);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_firstSelected);
    }

    private void PopulateStats()
    {
        var tracker = RunTracker.Instance;
        if (tracker == null) return;

        _timeLabel.text   = "Time: "   + FormatTime(tracker.ElapsedSeconds);
        _deathsLabel.text = "Deaths: " + tracker.DeathCount;
    }

    // ─── Buttons ──────────────────────────────────────────────────────────────

    private void OnPlayAgain()
    {
        Time.timeScale = 1f;
        ShowCursor(false);
        _active = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnExit()
    {
        Time.timeScale = 1f;
        ShowCursor(true);
        _active = false;
        SceneManager.LoadScene(mainMenuScene);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string FormatTime(float totalSeconds)
    {
        int m = (int)(totalSeconds / 60f);
        int s = (int)(totalSeconds % 60f);
        return $"{m:00}:{s:00}";
    }

    private static void ShowCursor(bool show)
    {
        Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = show;
    }

    private IEnumerator Fade(float from, float to, float dur)
    {
        float t = 0f;
        _fadeGroup.alpha = from;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            _fadeGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
            yield return null;
        }
        _fadeGroup.alpha = to;
    }

    // ─── Construction ─────────────────────────────────────────────────────────

    private void BuildUI()
    {
        _canvas = UIFactory.CreateCanvas("VictoryScreenCanvas");
        _canvas.sortingOrder = 310;  // above DeathScreen (300)
        _canvas.transform.SetParent(transform, false);

        // Dark overlay with gold tint
        var bg = UIFactory.CreatePanel(_canvas.transform, new Color(0.04f, 0.04f, 0.01f, 0.96f));
        _fadeGroup = bg.gameObject.AddComponent<CanvasGroup>();
        _fadeGroup.alpha = 0f;

        _optionsRoot = new GameObject("Options", typeof(RectTransform));
        _optionsRoot.transform.SetParent(_canvas.transform, false);
        UIFactory.StretchToParent(_optionsRoot.GetComponent<RectTransform>());

        // Title
        var title = UIFactory.CreateLabel(_optionsRoot.transform, "Victory!", 72,
            new Color(1f, 0.85f, 0.1f));
        var tRt = title.rectTransform;
        tRt.anchorMin = tRt.anchorMax = new Vector2(0.5f, 0.73f);
        tRt.pivot     = new Vector2(0.5f, 0.5f);
        tRt.sizeDelta = new Vector2(820f, 120f);

        // Time stat
        _timeLabel = UIFactory.CreateLabel(_optionsRoot.transform, "Time: --:--", 38, Color.white);
        var tlRt = _timeLabel.rectTransform;
        tlRt.anchorMin = tlRt.anchorMax = new Vector2(0.5f, 0.59f);
        tlRt.pivot     = new Vector2(0.5f, 0.5f);
        tlRt.sizeDelta = new Vector2(500f, 60f);

        // Deaths stat
        _deathsLabel = UIFactory.CreateLabel(_optionsRoot.transform, "Deaths: -", 38,
            new Color(0.9f, 0.38f, 0.38f));
        var dlRt = _deathsLabel.rectTransform;
        dlRt.anchorMin = dlRt.anchorMax = new Vector2(0.5f, 0.49f);
        dlRt.pivot     = new Vector2(0.5f, 0.5f);
        dlRt.sizeDelta = new Vector2(500f, 60f);

        // Buttons
        var col = UIFactory.CreateLayoutGroup(_optionsRoot.transform, vertical: true, spacing: 16f);
        var cRt = col.GetComponent<RectTransform>();
        cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0.3f);
        cRt.pivot     = new Vector2(0.5f, 0.5f);
        cRt.sizeDelta = new Vector2(340f, 150f);

        var playAgainBtn = UIFactory.CreateButton(col.transform, "Play Again", 26, OnPlayAgain);
        playAgainBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 60f);
        StyleVictory(playAgainBtn);
        _firstSelected = playAgainBtn.gameObject;

        var exitBtn = UIFactory.CreateButton(col.transform, "Exit to Main Menu", 22, OnExit);
        exitBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 60f);

        _optionsRoot.SetActive(false);
    }

    private static void StyleVictory(Button btn)
    {
        btn.GetComponent<Image>().color = new Color(0.12f, 0.32f, 0.08f, 0.95f);
        var cb = btn.colors;
        cb.normalColor      = new Color(0.12f, 0.32f, 0.08f, 0.95f);
        cb.highlightedColor = new Color(0.22f, 0.52f, 0.14f, 1.00f);
        cb.pressedColor     = new Color(0.07f, 0.18f, 0.04f, 1.00f);
        cb.selectedColor    = cb.highlightedColor;
        btn.colors = cb;
    }
}
