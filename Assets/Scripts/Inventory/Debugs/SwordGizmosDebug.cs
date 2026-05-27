// SwordGizmosDebug.cs — attach to the player GameObject to visualize the sword hitbox.
using UnityEngine;

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