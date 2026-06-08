using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

namespace Randomiz.UI
{
    /// <summary>
    /// Static helper for building UI elements entirely in code — no prefabs required.
    /// All methods return the primary component for easy chaining.
    /// </summary>
    public static class UIFactory
    {
        // ── Text ─────────────────────────────────────────────────────────────────

        /// <summary>Creates a TMP_Text label parented to <paramref name="parent"/>.</summary>
        public static TMP_Text CreateLabel(
            Transform parent,
            string text,
            int fontSize = 24,
            Color color = default,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400f, 60f);

            var label = go.GetComponent<TMP_Text>();
            label.text = text;
            label.fontSize = fontSize;
            // Default (unspecified) color falls back to the active theme's text color.
            label.color = color == default
                ? (UITheme.Current != null ? UITheme.Current.text : Color.white)
                : color;
            label.alignment = alignment;
            label.overflowMode = TextOverflowModes.Overflow;

            return label;
        }

        // ── Button ────────────────────────────────────────────────────────────────

        /// <summary>Creates a styled Button with a TMP_Text label.</summary>
        public static Button CreateButton(
            Transform parent,
            string label,
            int fontSize = 22,
            UnityAction onClick = null)
        {
            // Container
            var go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(260f, 55f);

            // Themed colors (fall back to the original flat palette when no theme is active).
            var theme = UITheme.Current;
            Color normal      = theme != null ? theme.buttonNormal      : new Color(0.15f, 0.15f, 0.15f, 0.92f);
            Color highlighted = theme != null ? theme.buttonHighlighted : new Color(0.28f, 0.28f, 0.28f, 1.00f);
            Color pressed     = theme != null ? theme.buttonPressed     : new Color(0.08f, 0.08f, 0.08f, 1.00f);

            var img = go.GetComponent<Image>();
            img.color = normal;
            if (theme != null && theme.buttonSprite != null)
            {
                img.sprite = theme.buttonSprite;
                img.type   = Image.Type.Sliced;
            }

            var btn = go.GetComponent<Button>();

            // Hover / press colour block
            var cb = btn.colors;
            cb.normalColor      = normal;
            cb.highlightedColor = highlighted;
            cb.pressedColor     = pressed;
            cb.selectedColor    = highlighted;
            cb.fadeDuration     = 0.1f;
            btn.colors = cb;

            if (onClick != null)
                btn.onClick.AddListener(onClick);

            // Label child
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);

            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textGo.GetComponent<TMP_Text>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.color = theme != null ? theme.text : Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            return btn;
        }

        // ── Panel (Image) ─────────────────────────────────────────────────────────

        /// <summary>Creates a plain Image panel.</summary>
        public static Image CreatePanel(
            Transform parent,
            Color color,
            bool raycastTarget = true)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycastTarget;

            // Themed skin: apply the panel sprite (if any) but KEEP the caller's color so
            // functional overlays (e.g. the death-screen black fade) stay correct.
            var theme = UITheme.Current;
            if (theme != null && theme.panelSprite != null)
            {
                img.sprite = theme.panelSprite;
                img.type   = Image.Type.Sliced;
            }

            return img;
        }

        // ── Canvas ────────────────────────────────────────────────────────────────

        /// <summary>Creates a Canvas with a CanvasScaler and GraphicRaycaster.</summary>
        public static Canvas CreateCanvas(string name, RenderMode mode = RenderMode.ScreenSpaceOverlay)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = mode;
            canvas.sortingOrder = 0;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        // ── Layout Group ──────────────────────────────────────────────────────────

        /// <summary>Creates a GameObject with a Vertical or Horizontal Layout Group.</summary>
        public static GameObject CreateLayoutGroup(
            Transform parent,
            bool vertical = true,
            float spacing = 12f,
            RectOffset padding = null)
        {
            var go = new GameObject(vertical ? "VerticalLayout" : "HorizontalLayout", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(400f, 600f);

            if (vertical)
            {
                var vlg = go.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = spacing;
                vlg.padding = padding ?? new RectOffset(10, 10, 10, 10);
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.childControlWidth  = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth  = true;
                vlg.childForceExpandHeight = false;
            }
            else
            {
                var hlg = go.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = spacing;
                hlg.padding = padding ?? new RectOffset(10, 10, 10, 10);
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlWidth  = false;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth  = false;
                hlg.childForceExpandHeight = true;
            }

            return go;
        }

        // ── Image ─────────────────────────────────────────────────────────────────

        /// <summary>Creates an Image with an optional sprite and tint.</summary>
        public static Image CreateImage(
            Transform parent,
            Sprite sprite = null,
            Color tint = default)
        {
            var go = new GameObject("Image", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 100f);

            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color  = tint == default ? Color.white : tint;

            return img;
        }

        // ── Internal helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Stretches a RectTransform to fill its parent completely.
        /// </summary>
        public static void StretchToParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Centers a RectTransform at the parent's pivot with explicit size.
        /// </summary>
        public static void CenterWithSize(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
