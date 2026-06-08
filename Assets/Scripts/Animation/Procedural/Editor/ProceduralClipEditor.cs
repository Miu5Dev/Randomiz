using UnityEditor;
using UnityEngine;

/// <summary>
/// Friendlier inspector for ProceduralClip: quick-preset buttons (hop, dip, recoil,
/// lean), a live scrubber that previews the offset on a selected scene object, and
/// the raw curve fields. Lets you tune procedural moves (e.g. the step-up hop)
/// visually instead of editing keyframes blind.
/// </summary>
[CustomEditor(typeof(ProceduralClip))]
public class ProceduralClipEditor : Editor
{
    private Transform _previewTarget;
    private float _scrub;
    private Vector3 _previewBasePos;
    private bool _previewActive;

    public override void OnInspectorGUI()
    {
        var clip = (ProceduralClip)target;

        EditorGUILayout.LabelField("Quick Presets", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Hop"))    ApplyHop(clip, 0.18f);
        if (GUILayout.Button("Dip"))    ApplyHop(clip, -0.12f);
        if (GUILayout.Button("Recoil")) ApplyRecoil(clip);
        if (GUILayout.Button("Lean"))   ApplyLean(clip);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        DrawDefaultInspector();

        // ── Live preview scrubber ───────────────────────────────────────────
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Live Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Assign a scene object, then scrub to see the offset " +
                                "applied. Reset restores its position.", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        _previewTarget = (Transform)EditorGUILayout.ObjectField("Preview Target", _previewTarget, typeof(Transform), true);
        if (EditorGUI.EndChangeCheck() && _previewTarget != null)
        {
            _previewBasePos = _previewTarget.localPosition;
            _previewActive = true;
        }

        using (new EditorGUI.DisabledScope(_previewTarget == null))
        {
            _scrub = EditorGUILayout.Slider("Time (0..1)", _scrub, 0f, 1f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply at Time")) PreviewAt(clip, _scrub);
            if (GUILayout.Button("Reset")) ResetPreview();
            EditorGUILayout.EndHorizontal();
        }
    }

    private void PreviewAt(ProceduralClip clip, float t)
    {
        if (_previewTarget == null) return;
        if (!_previewActive) { _previewBasePos = _previewTarget.localPosition; _previewActive = true; }
        _previewTarget.localPosition = _previewBasePos + clip.SamplePosition(t);
        _previewTarget.localRotation = Quaternion.Euler(clip.SampleRotationEuler(t)) * Quaternion.identity;
        SceneView.RepaintAll();
    }

    private void ResetPreview()
    {
        if (_previewTarget != null && _previewActive)
            _previewTarget.localPosition = _previewBasePos;
    }

    // ── Presets ─────────────────────────────────────────────────────────────
    private void ApplyHop(ProceduralClip c, float height)
    {
        Undo.RecordObject(c, "Hop preset");
        c.duration = 0.25f; c.affectPosition = true; c.positionScale = 1f;
        c.posY = new AnimationCurve(new Keyframe(0,0), new Keyframe(0.5f, height), new Keyframe(1,0));
        EditorUtility.SetDirty(c);
    }

    private void ApplyRecoil(ProceduralClip c)
    {
        Undo.RecordObject(c, "Recoil preset");
        c.duration = 0.2f; c.affectPosition = true;
        c.posZ = new AnimationCurve(new Keyframe(0,0), new Keyframe(0.25f, -0.12f), new Keyframe(1,0));
        c.affectRotation = true;
        c.rotX = new AnimationCurve(new Keyframe(0,0), new Keyframe(0.25f, -12f), new Keyframe(1,0));
        EditorUtility.SetDirty(c);
    }

    private void ApplyLean(ProceduralClip c)
    {
        Undo.RecordObject(c, "Lean preset");
        c.duration = 0.3f; c.affectRotation = true;
        c.rotZ = new AnimationCurve(new Keyframe(0,0), new Keyframe(0.5f, 10f), new Keyframe(1,0));
        EditorUtility.SetDirty(c);
    }
}
