using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
using Randomiz.UI;

// Attach to a persistent GameObject in the Game scene (or instantiate via a
// GameManager). The component manages Time.timeScale and the pause overlay.

namespace Randomiz.UI
{
    /// <summary>
    /// Pause menu toggled by the pause button (press once to open, press again to close).
    /// Subscribes to <see cref="OnPauseInputEvent"/>: only reacts on pressed=true (ignores release).
    ///
    /// When open  → Time.timeScale = 0
    /// When closed → Time.timeScale = 1
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const string MAIN_MENU_SCENE    = "MainMenu";
        private const float  SAVE_FEEDBACK_TIME = 2f;

        // ── State ─────────────────────────────────────────────────────────────
        private bool  _isOpen;
        private int   _currentSlot;       // read from SaveManager when the menu opens
        private float _lastToggleTime = -1f; // debounce guard against double-fired events

        // ── Runtime UI refs ───────────────────────────────────────────────────
        private Canvas     _canvas;
        private GameObject _menuPanel;
        private GameObject _confirmDialog;
        private GameObject _firstSelected;   // button focused on open (gamepad/keyboard nav)
        private TMP_Text   _seedSlotLabel;
        private TMP_Text   _saveFeedbackLabel;
        private Coroutine  _saveFeedbackRoutine;
        private System.Action _pendingConfirmAction;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            BuildUI();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnPauseInputEvent>(OnPauseInput, priority: 10);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnPauseInputEvent>(OnPauseInput);
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            // Canvas on top of gameplay (high sorting order)
            _canvas = UIFactory.CreateCanvas("PauseMenuCanvas");
            _canvas.sortingOrder = 100;
            _canvas.gameObject.transform.SetParent(transform, false);

            // Full-screen dimmer
            var dimmer = UIFactory.CreatePanel(_canvas.transform, new Color(0f, 0f, 0f, 0.65f));
            UIFactory.StretchToParent(dimmer.GetComponent<RectTransform>());

            // Centre card
            var card = UIFactory.CreatePanel(dimmer.transform, new Color(0.10f, 0.10f, 0.13f, 0.97f));
            UIFactory.CenterWithSize(card.GetComponent<RectTransform>(), new Vector2(500f, 520f));
            _menuPanel = card.gameObject;

            // "PAUSED" header
            var header = UIFactory.CreateLabel(card.transform, "PAUSED", 38, Color.white);
            var headerRt = header.GetComponent<RectTransform>();
            headerRt.anchorMin        = new Vector2(0f, 1f);
            headerRt.anchorMax        = new Vector2(1f, 1f);
            headerRt.pivot            = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -16f);
            headerRt.sizeDelta        = new Vector2(0f, 60f);

            // Seed / slot info label
            _seedSlotLabel = UIFactory.CreateLabel(card.transform, "", 16, new Color(0.7f, 0.7f, 0.7f));
            var infoRt = _seedSlotLabel.GetComponent<RectTransform>();
            infoRt.anchorMin        = new Vector2(0f, 1f);
            infoRt.anchorMax        = new Vector2(1f, 1f);
            infoRt.pivot            = new Vector2(0.5f, 1f);
            infoRt.anchoredPosition = new Vector2(0f, -82f);
            infoRt.sizeDelta        = new Vector2(0f, 36f);

            // Button column
            var col = UIFactory.CreateLayoutGroup(card.transform, vertical: true, spacing: 16f,
                padding: new RectOffset(60, 60, 140, 80));
            UIFactory.StretchToParent(col.GetComponent<RectTransform>());

            // Resume
            var resumeBtn = UIFactory.CreateButton(col.transform, "Resume", 24, OnResumeClicked);
            resumeBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 58f);
            StylePrimary(resumeBtn);
            _firstSelected = resumeBtn.gameObject; // focus target for gamepad/keyboard

            // Save Game
            var saveBtn = UIFactory.CreateButton(col.transform, "Save Game", 22, OnSaveClicked);
            saveBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 55f);

            // "Saved!" feedback label (hidden until a save occurs)
            _saveFeedbackLabel = UIFactory.CreateLabel(col.transform, "Saved!", 18,
                new Color(0.4f, 0.9f, 0.4f));
            _saveFeedbackLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 28f);
            _saveFeedbackLabel.gameObject.SetActive(false);

            // Return to Main Menu
            var menuBtn = UIFactory.CreateButton(col.transform, "Return to Main Menu", 20,
                OnReturnToMenuClicked);
            menuBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 55f);

            // Confirmation dialog
            BuildConfirmDialog(dimmer.transform);

            // Start closed
            _canvas.gameObject.SetActive(false);
        }

        private void BuildConfirmDialog(Transform parent)
        {
            var overlay = UIFactory.CreatePanel(parent, new Color(0f, 0f, 0f, 0.55f));
            UIFactory.StretchToParent(overlay.GetComponent<RectTransform>());
            _confirmDialog = overlay.gameObject;

            var card = UIFactory.CreatePanel(overlay.transform, new Color(0.14f, 0.14f, 0.18f, 1f));
            UIFactory.CenterWithSize(card.GetComponent<RectTransform>(), new Vector2(420f, 200f));

            var msg = UIFactory.CreateLabel(card.transform, "Return to Main Menu?", 22, Color.white);
            msg.name = "ConfirmMessage";
            var msgRt = msg.GetComponent<RectTransform>();
            msgRt.anchorMin        = new Vector2(0f, 0.5f);
            msgRt.anchorMax        = new Vector2(1f, 1f);
            msgRt.offsetMin        = Vector2.zero;
            msgRt.offsetMax        = Vector2.zero;

            var btnRow = UIFactory.CreateLayoutGroup(card.transform, vertical: false, spacing: 20f);
            var btnRt  = btnRow.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0f, 0f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.offsetMin = Vector2.zero;
            btnRt.offsetMax = Vector2.zero;

            UIFactory.CreateButton(btnRow.transform, "Confirm", 20, OnConfirmAccepted)
                .GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 50f);

            UIFactory.CreateButton(btnRow.transform, "Cancel", 20, OnConfirmCancelled)
                .GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 50f);

            _confirmDialog.SetActive(false);
        }

        // ── Open / Close ──────────────────────────────────────────────────────

        private void OpenMenu()
        {
            _isOpen = true;
            Time.timeScale = 0f;
            RefreshInfoLabel();
            _canvas.gameObject.SetActive(true);

            // Gameplay locks/hides the cursor — free it so the player can click buttons.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Focus the first button so gamepad/keyboard can navigate without a mouse.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_firstSelected);
        }

        private void CloseMenu()
        {
            _isOpen = false;
            Time.timeScale = 1f;
            _confirmDialog.SetActive(false);
            _canvas.gameObject.SetActive(false);

            // Restore gameplay cursor state.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void RefreshInfoLabel()
        {
            // Pull current slot from SaveManager if available
            if (SaveManager.Instance != null)
            {
                _currentSlot = SaveManager.Instance.CurrentSlot;
                // Seed lives on RandomizerSystem, not SaveManager.
                var rnd = UnityEngine.Object.FindFirstObjectByType<RandomizerSystem>();
                int seed = rnd != null ? rnd.CurrentSeed : -1;
                _seedSlotLabel.text = $"Slot {_currentSlot + 1}  |  Seed: {seed}";
            }
            else
            {
                _seedSlotLabel.text = "Slot: -  |  Seed: -";
            }
        }

        // ── Event handler ─────────────────────────────────────────────────────

        private void OnPauseInput(OnPauseInputEvent e)
        {
            if (!e.pressed) return;

            // Ignore a second press within the debounce window. Without this, two
            // events from the same physical press would toggle open→closed and the
            // menu would never appear. Uses unscaled time (timeScale is 0 while paused).
            if (Time.unscaledTime - _lastToggleTime < 0.15f) return;
            _lastToggleTime = Time.unscaledTime;

            if (_isOpen) CloseMenu();
            else OpenMenu();
        }

        // ── Button handlers ───────────────────────────────────────────────────

        private void OnResumeClicked()
        {
            CloseMenu();
        }

        private void OnSaveClicked()
        {
            if (SaveManager.Instance != null)
                SaveManager.Instance.SaveGame(_currentSlot);

            ShowSaveFeedback();
        }

        private void OnReturnToMenuClicked()
        {
            ShowConfirm("Return to Main Menu?\nUnsaved progress will be lost.", () =>
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(MAIN_MENU_SCENE);
            });
        }

        // ── Confirmation dialog ───────────────────────────────────────────────

        private void ShowConfirm(string message, System.Action onConfirm)
        {
            _pendingConfirmAction = onConfirm;

            var msgLabel = _confirmDialog.GetComponentInChildren<TMP_Text>();
            if (msgLabel != null) msgLabel.text = message;

            _confirmDialog.SetActive(true);
        }

        private void OnConfirmAccepted()
        {
            _confirmDialog.SetActive(false);
            _pendingConfirmAction?.Invoke();
            _pendingConfirmAction = null;
        }

        private void OnConfirmCancelled()
        {
            _confirmDialog.SetActive(false);
            _pendingConfirmAction = null;
        }

        // ── Save feedback ─────────────────────────────────────────────────────

        private void ShowSaveFeedback()
        {
            if (_saveFeedbackRoutine != null)
                StopCoroutine(_saveFeedbackRoutine);
            _saveFeedbackRoutine = StartCoroutine(SaveFeedbackRoutine());
        }

        private IEnumerator SaveFeedbackRoutine()
        {
            _saveFeedbackLabel.gameObject.SetActive(true);
            // Use unscaled time because timeScale == 0 while paused
            yield return new WaitForSecondsRealtime(SAVE_FEEDBACK_TIME);
            _saveFeedbackLabel.gameObject.SetActive(false);
            _saveFeedbackRoutine = null;
        }

        // ── Style helpers ─────────────────────────────────────────────────────

        private static void StylePrimary(Button btn)
        {
            btn.GetComponent<Image>().color = new Color(0.18f, 0.38f, 0.58f, 0.95f);
            var cb = btn.colors;
            cb.normalColor      = new Color(0.18f, 0.38f, 0.58f, 0.95f);
            cb.highlightedColor = new Color(0.24f, 0.48f, 0.72f, 1.00f);
            cb.pressedColor     = new Color(0.12f, 0.26f, 0.42f, 1.00f);
            btn.colors = cb;
        }
    }
}
