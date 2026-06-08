using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

/// <summary>
/// ScriptableObject that defines a cinematic sequence as a series of shots.
/// Uses plain Unity Cameras — no Cinemachine required.
/// Each shot specifies which Camera to activate and for how long.
/// </summary>
[CreateAssetMenu(fileName = "CinematicSequence", menuName = "Cinematics/Cinematic Sequence")]
public class CinematicSequence : ScriptableObject
{
    [System.Serializable]
    public class CinematicShot
    {
        [Tooltip("The Camera to activate for this shot. The CinematicPlayer will enable it and disable all others.")]
        public Camera cam;
        [Tooltip("How many seconds this shot lasts.")]
        public float duration = 2f;
        [Tooltip("Optional curve used to lerp camera FOV from start to end of shot.")]
        public AnimationCurve blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    [Header("Shots")]
    [SerializeField] private List<CinematicShot> shots = new();

    [Header("Player Control")]
    [Tooltip("Disable player movement while the cinematic plays.")]
    [SerializeField] private bool disablePlayerDuringCinematic = true;
    [Tooltip("Disable the gameplay camera while the cinematic plays.")]
    [SerializeField] private bool lockCameraDuringCinematic = true;

    [Header("Events")]
    public UnityEvent onCinematicStart = new();
    public UnityEvent onCinematicEnd   = new();

    // ── Properties ────────────────────────────────────────────────────────────

    public IReadOnlyList<CinematicShot> Shots                    => shots.AsReadOnly();
    public bool DisablePlayerDuringCinematic                     => disablePlayerDuringCinematic;
    public bool LockCameraDuringCinematic                        => lockCameraDuringCinematic;
    public bool HasShots                                         => shots.Count > 0;
    public int  ShotCount                                        => shots.Count;

    public float TotalDuration
    {
        get
        {
            float t = 0f;
            foreach (var s in shots) t += s.duration;
            return t;
        }
    }
}
