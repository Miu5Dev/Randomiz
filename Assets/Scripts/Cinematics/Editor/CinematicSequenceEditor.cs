#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom inspector for CinematicSequence.
/// Uses plain Unity Cameras — no Cinemachine required.
/// Provides a preview button, progress bar, and sequence info.
/// </summary>
[CustomEditor(typeof(CinematicSequence))]
public class CinematicSequenceEditor : Editor
{
    private CinematicSequence _sequence;
    private bool  _previewActive;
    private float _previewElapsed;
    private Camera _activePreviewCamera;

    private void OnEnable()
    {
        _sequence = (CinematicSequence)target;
        EditorApplication.update += EditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
        StopPreview();
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Cinematic Preview", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (!_previewActive)
        {
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("▶ Preview", GUILayout.Height(30)))
                StartPreview();
            GUI.backgroundColor = Color.white;
        }
        else
        {
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("■ Stop", GUILayout.Height(30)))
                StopPreview();
            GUI.backgroundColor = Color.white;
        }
        EditorGUILayout.EndHorizontal();

        // Progress bar while previewing.
        if (_previewActive && _sequence.HasShots)
        {
            float total = _sequence.TotalDuration;
            Rect rect = EditorGUILayout.GetControlRect(GUILayout.Height(20));
            float t = total > 0f ? _previewElapsed / total : 0f;
            EditorGUI.ProgressBar(rect, t, $"{_previewElapsed:F2}s / {total:F2}s");
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Sequence Info", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Total Duration : {_sequence.TotalDuration:F2} s");
        EditorGUILayout.LabelField($"Shot Count     : {_sequence.ShotCount}");
        EditorGUILayout.LabelField($"Disable Player : {_sequence.DisablePlayerDuringCinematic}");
        EditorGUILayout.LabelField($"Lock Camera    : {_sequence.LockCameraDuringCinematic}");
    }

    private void StartPreview()
    {
        if (!_sequence.HasShots)
        {
            EditorUtility.DisplayDialog("Preview", "Sequence has no shots.", "OK");
            return;
        }
        _previewActive   = true;
        _previewElapsed  = 0f;
        _activePreviewCamera = null;
    }

    private void StopPreview()
    {
        if (_activePreviewCamera != null)
        {
            _activePreviewCamera.enabled = false;
            _activePreviewCamera = null;
        }
        _previewActive  = false;
        _previewElapsed = 0f;
    }

    private void EditorUpdate()
    {
        if (!_previewActive || _sequence == null || !_sequence.HasShots) return;

        _previewElapsed += Time.deltaTime;

        if (_previewElapsed >= _sequence.TotalDuration)
        {
            StopPreview();
            return;
        }

        // Find which shot should be active at _previewElapsed.
        float cursor = 0f;
        foreach (var shot in _sequence.Shots)
        {
            if (_previewElapsed >= cursor && _previewElapsed < cursor + shot.duration)
            {
                if (_activePreviewCamera != shot.cam)
                {
                    if (_activePreviewCamera != null)
                        _activePreviewCamera.enabled = false;

                    _activePreviewCamera = shot.cam;

                    if (_activePreviewCamera != null)
                        _activePreviewCamera.enabled = true;
                }
                break;
            }
            cursor += shot.duration;
        }

        Repaint();
    }
}
#endif
