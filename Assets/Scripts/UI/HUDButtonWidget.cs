using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD cross button (north/south/east/west). Background + icon + optional label.
/// Background is set on the prefab; this script controls the icon and label only.
///
/// Performance: SetIcon / SetLabel short-circuit when the new value matches the
/// last applied one. Avoids redundant Image / TMP rebuilds when the HUD refreshes
/// on every event (which RefreshAllButtons does for safety).
/// </summary>
public class HUDButtonWidget : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text labelText;

    private Sprite _lastSprite;
    private bool   _hasLastSprite;
    private bool   _lastIconEnabled = true;
    private string _lastLabel;

    public void SetIcon(Sprite sprite)
    {
        if (iconImage == null) return;

        bool shouldEnable = sprite != null;
        if (_hasLastSprite && sprite == _lastSprite && shouldEnable == _lastIconEnabled) return;

        if (shouldEnable)
        {
            iconImage.sprite = sprite;
            if (!iconImage.enabled) iconImage.enabled = true;
        }
        else if (iconImage.enabled)
        {
            iconImage.enabled = false;
        }

        _lastSprite       = sprite;
        _hasLastSprite    = true;
        _lastIconEnabled  = shouldEnable;
    }

    public void SetLabel(string label)
    {
        if (labelText == null) return;
        string next = label ?? string.Empty;
        if (next == _lastLabel) return;
        labelText.text = next;
        _lastLabel = next;
    }
}
