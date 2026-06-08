using UnityEngine;

namespace Randomiz.UI
{
    /// <summary>
    /// Scene component that makes a chosen <see cref="SOUITheme"/> the active theme at
    /// runtime. Runs very early (before UI builds in its own Awake) so <see cref="UIFactory"/>
    /// and the boss bars pick it up. Optional — without one, <see cref="UITheme"/> falls
    /// back to Resources/UITheme.
    ///
    /// Added / assigned by the Game Setup Wizard's UI Theme editor.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class UIThemeProvider : MonoBehaviour
    {
        [SerializeField] private SOUITheme theme;

        public SOUITheme Theme => theme;

        private void Awake()
        {
            if (theme != null)
                UITheme.Current = theme;
        }
    }
}
