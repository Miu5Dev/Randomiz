using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple component on a pooled icon Image used by the minimap.
/// Holds state and provides a method to update position, color, and rotation.
/// </summary>
public class MinimapIcon : MonoBehaviour
{
    private Image image;
    private RectTransform rectTransform;

    private void Awake()
    {
        image = GetComponent<Image>();
        rectTransform = GetComponent<RectTransform>();
    }

    /// <summary>
    /// Updates this icon's state on the minimap.
    /// </summary>
    /// <param name="anchoredPos">Position in anchored coordinates (relative to parent).</param>
    /// <param name="color">Color to display (white for closed chests, gold for opened).</param>
    /// <param name="rotation">Rotation in degrees (mainly for player arrow; 0 for chest icons).</param>
    public void SetState(Vector2 anchoredPos, Color color, float rotation = 0f)
    {
        rectTransform.anchoredPosition = anchoredPos;
        image.color = color;
        // Negative sign converts from Unity world-space clockwise convention
        // to UI AngleAxis counter-clockwise convention
        rectTransform.localRotation = Quaternion.AngleAxis(-rotation, Vector3.forward);
    }

    /// <summary>
    /// Disables the icon (used during pooling).
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Enables the icon.
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
    }
}
