using System;
using UnityEngine;

/// <summary>
/// An editable procedural animation: time-based offset curves applied on top of the
/// base animation. Author it as an asset (Create → Animation → Procedural Clip) and
/// tune the curves in the inspector (or the custom editor). A ProceduralAnimator
/// plays it - e.g. a small "hop" for an auto step-up, a recoil, a landing dip.
///
/// Offsets are LOCAL to the model: +X right, +Y up, +Z forward. Position is in
/// metres, rotation in degrees. Curves are sampled over normalised time 0..1 and
/// scaled by <see cref="duration"/>.
/// </summary>
[CreateAssetMenu(fileName = "ProcClip", menuName = "Animation/Procedural Clip")]
public class ProceduralClip : ScriptableObject
{
    [Tooltip("Total play time in seconds. Curves are sampled over normalised 0..1.")]
    [Min(0.01f)] public float duration = 0.25f;

    [Tooltip("If set, the clip loops until stopped instead of playing once.")]
    public bool loop = false;

    [Header("Root / model position offset (metres, local)")]
    public bool affectPosition = true;
    public AnimationCurve posX = Flat();
    public AnimationCurve posY = Flat();   // e.g. a hop: 0 -> up -> 0
    public AnimationCurve posZ = Flat();
    [Tooltip("Multiplier applied to all position curves (scale the whole motion).")]
    public float positionScale = 1f;

    [Header("Root / model rotation offset (degrees, local euler)")]
    public bool affectRotation = false;
    public AnimationCurve rotX = Flat();   // pitch (lean fwd/back)
    public AnimationCurve rotY = Flat();   // yaw
    public AnimationCurve rotZ = Flat();   // roll
    public float rotationScale = 1f;

    /// <summary>Sample the position offset at normalised time t (0..1).</summary>
    public Vector3 SamplePosition(float t)
    {
        if (!affectPosition) return Vector3.zero;
        return new Vector3(posX.Evaluate(t), posY.Evaluate(t), posZ.Evaluate(t)) * positionScale;
    }

    /// <summary>Sample the rotation offset (euler degrees) at normalised time t.</summary>
    public Vector3 SampleRotationEuler(float t)
    {
        if (!affectRotation) return Vector3.zero;
        return new Vector3(rotX.Evaluate(t), rotY.Evaluate(t), rotZ.Evaluate(t)) * rotationScale;
    }

    private static AnimationCurve Flat() => AnimationCurve.Constant(0f, 1f, 0f);

    /// <summary>Convenience preset: a single up-hop on posY (used by step-up).</summary>
    public static ProceduralClip CreateHop(float height, float dur)
    {
        var c = CreateInstance<ProceduralClip>();
        c.duration = dur;
        c.affectPosition = true;
        c.posY = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, height),
            new Keyframe(1f, 0f));
        return c;
    }
}
