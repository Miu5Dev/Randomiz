using UnityEngine;

/// <summary>
/// Marker component that designates a GameObject as a valid grapple attachment
/// point for <see cref="GrappleHookBehaviour"/>.
///
/// Usage:
///   • Add this component to any static or dynamic object the player should be
///     able to hook onto (overhanging beams, rock faces, ceiling rings, etc.).
///   • Assign the object's layer to match <see cref="SOGrappleHook.hookableLayers"/>
///     so the raycast can detect it.
///   • No additional logic is required — the component is intentionally a pure
///     marker so the hook system stays decoupled from level geometry.
/// </summary>
public class GrappleAnchor : MonoBehaviour
{
    [Tooltip("Visual radius of the anchor gizmo drawn in the editor (does not affect gameplay).")]
    [SerializeField] private float gizmoRadius = 0.25f;

    [Tooltip("Colour of the anchor gizmo in the Scene view.")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.85f);

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;

        // Outer ring to mark the anchor visually.
        DrawGizmoRing(transform.position, gizmoRadius);

        // Small filled sphere at the center.
        Gizmos.DrawSphere(transform.position, gizmoRadius * 0.2f);

        // Downward "drop" line to indicate the attach direction.
        Gizmos.DrawLine(transform.position,
                        transform.position + Vector3.down * gizmoRadius * 1.5f);
    }

    private void OnDrawGizmosSelected()
    {
        // Highlight when selected with a larger, brighter ring.
        Gizmos.color = Color.cyan;
        DrawGizmoRing(transform.position, gizmoRadius * 1.4f);
    }

    /// <summary>
    /// Draws an approximated circle in the XZ plane at <paramref name="center"/>
    /// using line segments (Gizmos has no built-in DrawCircle).
    /// </summary>
    private static void DrawGizmoRing(Vector3 center, float radius, int segments = 24)
    {
        float step = 360f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Deg2Rad * (i * step);
            float a1 = Mathf.Deg2Rad * ((i + 1) * step);

            Vector3 p0 = center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
            Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            Gizmos.DrawLine(p0, p1);
        }
    }
#endif
}
