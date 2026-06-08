using UnityEngine;

namespace Randomiz.UI
{
    /// <summary>
    /// Central look-and-feel asset for the code-built UI. <see cref="UIFactory"/> and the
    /// bespoke boss bars read the active theme (see <see cref="UITheme"/>) so designers
    /// can reskin every screen — panels, buttons, popups, boss bar — by assigning custom
    /// sprites and colors here, with no per-screen edits.
    ///
    /// Every sprite is optional: a null sprite leaves that element on its current flat
    /// look. Authored / assigned through the Game Setup Wizard's UI Theme editor, and
    /// resolved at runtime from Resources/UITheme or an in-scene <see cref="UIThemeProvider"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "UITheme", menuName = "Randomiz/UI Theme")]
    public class SOUITheme : ScriptableObject
    {
        [Header("Sprites  (null = flat default)")]
        [Tooltip("Background sprite applied to generic panels (UIFactory.CreatePanel).")]
        public Sprite panelSprite;
        [Tooltip("Background sprite applied to generic buttons (UIFactory.CreateButton).")]
        public Sprite buttonSprite;
        [Tooltip("Background sprite for popup cards (pickup / boss intro).")]
        public Sprite popupBackgroundSprite;
        [Tooltip("Background sprite for the boss health-bar track.")]
        public Sprite bossBarBackgroundSprite;
        [Tooltip("Fill sprite for the boss health bar.")]
        public Sprite bossBarFillSprite;

        [Header("Button colors")]
        public Color buttonNormal      = new Color(0.15f, 0.15f, 0.15f, 0.92f);
        public Color buttonHighlighted = new Color(0.28f, 0.28f, 0.28f, 1.00f);
        public Color buttonPressed     = new Color(0.08f, 0.08f, 0.08f, 1.00f);

        [Header("Accent / text")]
        [Tooltip("Default label color used when a caller does not specify one.")]
        public Color text   = Color.white;
        [Tooltip("Accent color for highlights (boss name, prompts).")]
        public Color accent = new Color(1f, 0.92f, 0.6f, 1f);
    }
}
