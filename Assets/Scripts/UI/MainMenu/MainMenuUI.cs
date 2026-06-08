using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Randomiz.UI;

// Attach this MonoBehaviour to an empty GameObject in the MainMenu scene.
// The full canvas hierarchy is built procedurally in Awake — no prefabs needed.

namespace Randomiz.UI
{
    /// <summary>
    /// Builds and drives the Main Menu screen: title, Play and Quit buttons.
    /// "Play" opens the <see cref="SaveSlotPanel"/>; "Quit" exits the application.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        // ── Inspector tunables ─────────────────────────────────────────────────
        [Header("Appearance")]
        [SerializeField] private Color _backgroundTint  = new Color(0.06f, 0.06f, 0.10f, 1f);
        [SerializeField] private Color _titleColor      = Color.white;
        [SerializeField] private string _gameTitle      = "RANDOMIZ";

        // ── Runtime refs ───────────────────────────────────────────────────────
        private Canvas         _canvas;
        private SaveSlotPanel  _saveSlotPanel;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            BuildUI();
        }

        // ── Build ──────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            // Root canvas — Screen Space Overlay, scales with screen size
            _canvas = UIFactory.CreateCanvas("MainMenuCanvas");
            _canvas.gameObject.transform.SetParent(transform, false);

            // Full-screen background
            var bg = UIFactory.CreatePanel(_canvas.transform, _backgroundTint);
            UIFactory.StretchToParent(bg.GetComponent<RectTransform>());

            // Centre column for title + buttons
            var column = UIFactory.CreateLayoutGroup(bg.transform, vertical: true, spacing: 24f,
                padding: new RectOffset(0, 0, 0, 0));
            UIFactory.CenterWithSize(column.GetComponent<RectTransform>(), new Vector2(320f, 500f));

            // Game title
            var titleLabel = UIFactory.CreateLabel(column.transform, _gameTitle, 56, _titleColor,
                TextAlignmentOptions.Center);
            var titleRt = titleLabel.GetComponent<RectTransform>();
            titleRt.sizeDelta = new Vector2(320f, 80f);

            // Spacer
            AddSpacer(column.transform, 30f);

            // Play button
            var playBtn = UIFactory.CreateButton(column.transform, "Play", 26, OnPlayClicked);
            playBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, 60f);
            StylePrimaryButton(playBtn);

            // Quit button
            var quitBtn = UIFactory.CreateButton(column.transform, "Quit", 22, OnQuitClicked);
            quitBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, 55f);

            // Save slot panel — lives on a higher-sorted canvas, starts inactive
            var slotGo = new GameObject("SaveSlotPanel");
            slotGo.transform.SetParent(transform, false);
            _saveSlotPanel = slotGo.AddComponent<SaveSlotPanel>();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void AddSpacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
        }

        private static void StylePrimaryButton(UnityEngine.UI.Button btn)
        {
            // Give the primary button a slightly brighter tint so it stands out
            var img = btn.GetComponent<Image>();
            img.color = new Color(0.18f, 0.35f, 0.55f, 0.95f);

            var cb = btn.colors;
            cb.normalColor      = new Color(0.18f, 0.35f, 0.55f, 0.95f);
            cb.highlightedColor = new Color(0.24f, 0.45f, 0.70f, 1.00f);
            cb.pressedColor     = new Color(0.12f, 0.25f, 0.40f, 1.00f);
            btn.colors = cb;
        }

        // ── Button handlers ────────────────────────────────────────────────────

        private void OnPlayClicked()
        {
            if (_saveSlotPanel != null)
                _saveSlotPanel.gameObject.SetActive(true);
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
