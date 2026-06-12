using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Randomiz.UI;

namespace Randomiz.UI
{
    /// <summary>
    /// Builds and drives the Main Menu screen: title, Play and Quit buttons.
    /// All positions, sizes, colors and sprites are exposed in the inspector.
    /// Each element (logo, title area, buttons card) is independently positionable.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        // ── Title ──────────────────────────────────────────────────────────────
        [Header("Title")]
        [SerializeField] private string _gameTitle     = "RANDOMIZ";
        [SerializeField] private int    _titleFontSize = 72;
        [SerializeField] private Color  _titleColor    = Color.white;

        [Header("Subtitle (optional)")]
        [Tooltip("Short tagline shown below the title. Leave blank to hide.")]
        [SerializeField] private string _subtitle      = "";
        [SerializeField] private Color  _subtitleColor = new Color(0.72f, 0.72f, 0.85f, 1f);

        // ── Logo ───────────────────────────────────────────────────────────────
        [Header("Logo (optional)")]
        [Tooltip("Sprite displayed independently from the title — drag it wherever you want.")]
        [SerializeField] private Sprite  _logoSprite;
        [Tooltip("Size of the logo image in pixels.")]
        [SerializeField] private Vector2 _logoSize     = new Vector2(400f, 120f);

        // ── Background ─────────────────────────────────────────────────────────
        [Header("Background")]
        [Tooltip("Full-screen background art. If blank, uses Background Tint instead.")]
        [SerializeField] private Sprite _backgroundSprite;
        [SerializeField] private Color  _backgroundTint = new Color(0.06f, 0.06f, 0.10f, 1f);
        [Tooltip("Dark overlay applied over the background sprite for readability.")]
        [SerializeField] private Color  _overlayColor   = new Color(0f, 0f, 0f, 0.45f);

        // ── Buttons ────────────────────────────────────────────────────────────
        [Header("Buttons")]
        [Tooltip("Semi-transparent card panel behind the buttons.")]
        [SerializeField] private Color   _cardColor        = new Color(0f, 0f, 0f, 0.40f);
        [SerializeField] private Color   _playButtonColor  = new Color(0.18f, 0.35f, 0.55f, 0.95f);
        [Tooltip("Size of the buttons card in pixels.")]
        [SerializeField] private Vector2 _buttonsCardSize  = new Vector2(340f, 192f);

        // ── Footer ─────────────────────────────────────────────────────────────
        [Header("Footer")]
        [Tooltip("Version string shown in the bottom-right corner. Leave blank to hide.")]
        [SerializeField] private string _versionText  = "";
        [SerializeField] private Color  _versionColor = new Color(0.50f, 0.50f, 0.55f, 1f);

        // ── Layout ─────────────────────────────────────────────────────────────
        [Header("Layout  (X/Y from screen center, in pixels)")]
        [Tooltip("Anchored position of the logo image. Positive Y = up.")]
        [SerializeField] private Vector2 _logoPosition        = new Vector2(0f,  220f);
        [Tooltip("Anchored position of the title + subtitle column.")]
        [SerializeField] private Vector2 _titleAreaPosition   = new Vector2(0f,   80f);
        [Tooltip("Anchored position of the buttons card.")]
        [SerializeField] private Vector2 _buttonsCardPosition = new Vector2(0f, -160f);

        // ── Runtime ────────────────────────────────────────────────────────────
        private Canvas        _canvas;
        private SaveSlotPanel _saveSlotPanel;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake() => BuildUI();

        // ── Build ──────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = UIFactory.CreateCanvas("MainMenuCanvas");
            _canvas.transform.SetParent(transform, false);

            BuildBackground(_canvas.transform);

            if (_logoSprite != null)
                BuildLogo(_canvas.transform);

            BuildTitleArea(_canvas.transform);
            BuildButtonsCard(_canvas.transform);

            if (!string.IsNullOrEmpty(_versionText))
                BuildVersionLabel(_canvas.transform);

            var slotGo = new GameObject("SaveSlotPanel");
            slotGo.transform.SetParent(transform, false);
            _saveSlotPanel = slotGo.AddComponent<SaveSlotPanel>();
        }

        // ── Background ─────────────────────────────────────────────────────────

        private void BuildBackground(Transform parent)
        {
            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(parent, false);
            UIFactory.StretchToParent(bgGo.GetComponent<RectTransform>());

            var img = bgGo.GetComponent<Image>();
            img.raycastTarget = false;

            if (_backgroundSprite != null)
            {
                img.sprite         = _backgroundSprite;
                img.color          = Color.white;
                img.type           = Image.Type.Simple;
                img.preserveAspect = false;

                if (_overlayColor.a > 0f)
                {
                    var ovGo = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
                    ovGo.transform.SetParent(parent, false);
                    UIFactory.StretchToParent(ovGo.GetComponent<RectTransform>());
                    var ovImg = ovGo.GetComponent<Image>();
                    ovImg.color         = _overlayColor;
                    ovImg.raycastTarget = false;
                }
            }
            else
            {
                img.color = _backgroundTint;
            }
        }

        // ── Logo (independent, freely positionable) ────────────────────────────

        private void BuildLogo(Transform parent)
        {
            var logoGo = new GameObject("Logo", typeof(RectTransform), typeof(Image));
            logoGo.transform.SetParent(parent, false);

            var rt = logoGo.GetComponent<RectTransform>();
            rt.anchorMin        = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = _logoPosition;
            rt.sizeDelta        = _logoSize;

            var img = logoGo.GetComponent<Image>();
            img.sprite         = _logoSprite;
            img.preserveAspect = true;
            img.raycastTarget  = false;
        }

        // ── Title area ─────────────────────────────────────────────────────────

        private void BuildTitleArea(Transform parent)
        {
            var col = UIFactory.CreateLayoutGroup(parent, vertical: true, spacing: 10f,
                padding: new RectOffset(0, 0, 0, 0));
            var rt  = col.GetComponent<RectTransform>();
            rt.anchorMin        = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = _titleAreaPosition;
            rt.sizeDelta        = new Vector2(700f, 300f);

            if (col.TryGetComponent<VerticalLayoutGroup>(out var vlg))
                vlg.childAlignment = TextAnchor.UpperCenter;

            var title = UIFactory.CreateLabel(col.transform, _gameTitle, _titleFontSize,
                _titleColor, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.GetComponent<RectTransform>().sizeDelta =
                new Vector2(700f, Mathf.CeilToInt(_titleFontSize * 1.6f));

            if (!string.IsNullOrEmpty(_subtitle))
            {
                var sub = UIFactory.CreateLabel(col.transform, _subtitle, 22,
                    _subtitleColor, TextAlignmentOptions.Center);
                sub.GetComponent<RectTransform>().sizeDelta = new Vector2(700f, 38f);
            }
        }

        // ── Buttons card ───────────────────────────────────────────────────────

        private void BuildButtonsCard(Transform parent)
        {
            var card = new GameObject("ButtonsCard", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(parent, false);
            var cardRt = card.GetComponent<RectTransform>();
            cardRt.anchorMin        = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot            = new Vector2(0.5f, 0.5f);
            cardRt.anchoredPosition = _buttonsCardPosition;
            cardRt.sizeDelta        = _buttonsCardSize;

            var cardImg = card.GetComponent<Image>();
            cardImg.color = _cardColor;
            var theme = UITheme.Current;
            if (theme != null && theme.panelSprite != null)
            {
                cardImg.sprite = theme.panelSprite;
                cardImg.type   = Image.Type.Sliced;
            }

            var col = UIFactory.CreateLayoutGroup(card.transform, vertical: true, spacing: 14f,
                padding: new RectOffset(28, 28, 28, 28));
            UIFactory.StretchToParent(col.GetComponent<RectTransform>());

            var playBtn = UIFactory.CreateButton(col.transform, "Play", 28, OnPlayClicked);
            playBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 64f);
            StyleButton(playBtn, _playButtonColor);

            var quitBtn = UIFactory.CreateButton(col.transform, "Quit", 22, OnQuitClicked);
            quitBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 56f);
        }

        // ── Version label ──────────────────────────────────────────────────────

        private void BuildVersionLabel(Transform parent)
        {
            var lbl = UIFactory.CreateLabel(parent, _versionText, 15,
                _versionColor, TextAlignmentOptions.BottomRight);
            var rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin        = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-16f, 12f);
            rt.sizeDelta        = new Vector2(200f, 30f);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void StyleButton(Button btn, Color normal)
        {
            btn.GetComponent<Image>().color = normal;
            var cb = btn.colors;
            cb.normalColor      = normal;
            cb.highlightedColor = Brighten(normal, 0.12f);
            cb.pressedColor     = Darken(normal, 0.12f);
            cb.selectedColor    = cb.highlightedColor;
            cb.fadeDuration     = 0.1f;
            btn.colors = cb;
        }

        private static Color Brighten(Color c, float d) =>
            new Color(Mathf.Clamp01(c.r + d), Mathf.Clamp01(c.g + d),
                      Mathf.Clamp01(c.b + d), c.a);

        private static Color Darken(Color c, float d) =>
            new Color(Mathf.Clamp01(c.r - d), Mathf.Clamp01(c.g - d),
                      Mathf.Clamp01(c.b - d), c.a);

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
