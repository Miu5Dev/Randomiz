// SwordGizmosDebug.cs  — adjuntalo al GameObject del jugador
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