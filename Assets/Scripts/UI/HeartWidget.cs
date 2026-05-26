using UnityEngine;
using UnityEngine.UI;

public class HeartWidget : MonoBehaviour
{
    [SerializeField] private Image fill;

    public void SetFill(float amount)
    {
        fill.fillAmount = Mathf.Clamp01(amount);
    }
}
