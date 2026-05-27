using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Botón visual del HUD (norte/sur/este/oeste). Fondo + icono + texto opcional.
/// El fondo lo gestiona el setup de la prefab; este script controla icono y label.
/// </summary>
public class HUDButtonWidget : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text labelText;

    public void SetIcon(Sprite sprite)
    {
        if (iconImage == null) return;

        if (sprite != null)
        {
            iconImage.sprite = sprite;
            iconImage.enabled = true;
        }
        else
        {
            iconImage.enabled = false;
        }
    }

    public void SetLabel(string label)
    {
        if (labelText != null) labelText.text = label ?? string.Empty;
    }
}
