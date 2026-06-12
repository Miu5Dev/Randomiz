using UnityEngine;
using UnityEngine.UI;

/// <summary>One heart in the hearts HUD; <see cref="SetFill"/> sets its fill (0-1) to show partial hearts.</summary>
public class HeartWidget : MonoBehaviour
{
    [SerializeField] private Image fill;

    public void SetFill(float amount)
    {
        fill.fillAmount = Mathf.Clamp01(amount);
    }
}
