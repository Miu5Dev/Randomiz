// SwordGizmosDebug.cs — attach to the player GameObject to visualize the sword hitbox.
using UnityEngine;

/// <summary>Editor-only debug helper; draws the assigned sword's hitbox gizmo on the player when the object is selected.</summary>
public class SwordGizmosDebug : MonoBehaviour
{
    public SOSword sword;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (sword != null)
            sword.DrawGizmos(transform);
    }
#endif
}