using UnityEngine;

namespace Randomiz.UI
{
    /// <summary>
    /// Process-wide pointer to the active <see cref="SOUITheme"/>. <see cref="UIFactory"/>
    /// and the boss bars read <see cref="Current"/> when building UI.
    ///
    /// Resolution order:
    ///   1. An explicit theme set via <see cref="UIThemeProvider"/> (or code).
    ///   2. Lazy fallback: <c>Resources.Load&lt;SOUITheme&gt;("UITheme")</c>.
    /// Both are optional — when no theme exists, UI keeps its built-in flat look.
    /// </summary>
    public static class UITheme
    {
        private static SOUITheme _current;
        private static bool      _resourceChecked;

        public static SOUITheme Current
        {
            get
            {
                // Lazy one-shot fallback to a Resources asset when nobody set one explicitly.
                if (_current == null && !_resourceChecked)
                {
                    _resourceChecked = true;
                    _current = Resources.Load<SOUITheme>("UITheme");
                }
                return _current;
            }
            set
            {
                _current = value;
                _resourceChecked = true; // an explicit set wins over the Resources fallback
            }
        }

        public static bool HasTheme => Current != null;

        // Cleared on Play-mode enter so a stale editor reference never leaks between sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            _resourceChecked = false;
        }
    }
}
