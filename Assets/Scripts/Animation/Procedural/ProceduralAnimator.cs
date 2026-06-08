using UnityEngine;

/// <summary>
/// Plays <see cref="ProceduralClip"/>s as additive offsets on a target transform
/// (the model). Layered ON TOP of the Animator's pose: each LateUpdate it removes
/// last frame's offset and applies the current one, so it never fights or
/// accumulates with the base animation.
///
/// Use for short flourishes the base clips don't cover: a step-up hop, a landing
/// dip, a recoil. Driven by code (PlayerAnimator) or any caller via Play().
///
/// Put it on the model object (same as the Animator). LateUpdate runs after the
/// Animator writes the pose, so the offset sits on top.
/// </summary>
public class ProceduralAnimator : MonoBehaviour
{
    [Tooltip("Transform to offset. Defaults to this object's transform (the model).")]
    [SerializeField] private Transform target;

    [Tooltip("Blend in/out speed for the offset weight (higher = snappier).")]
    [SerializeField] private float weightBlend = 16f;

    private ProceduralClip _clip;
    private float _time;
    private float _weight;          // eased 0..1 so clips fade in/out
    private bool  _playing;

    // Offset applied last frame, so we can undo it cleanly.
    private Vector3 _appliedPos;
    private Quaternion _appliedRot = Quaternion.identity;

    public bool IsPlaying => _playing;
    public ProceduralClip Current => _clip;

    private void Awake()
    {
        if (target == null) target = transform;
    }

    /// <summary>Start (or restart) a procedural clip.</summary>
    public void Play(ProceduralClip clip)
    {
        if (clip == null) return;
        _clip = clip;
        _time = 0f;
        _playing = true;
    }

    /// <summary>Stop the current clip; the offset eases back to zero.</summary>
    public void Stop() => _playing = false;

    private void LateUpdate()
    {
        // 1. Undo last frame's offset so the base pose is clean.
        if (target != null)
        {
            target.localPosition -= _appliedPos;
            target.localRotation  = Quaternion.Inverse(_appliedRot) * target.localRotation;
        }
        _appliedPos = Vector3.zero;
        _appliedRot = Quaternion.identity;

        // 2. Advance / fade.
        float targetWeight = _playing && _clip != null ? 1f : 0f;
        _weight = Mathf.MoveTowards(_weight, targetWeight, weightBlend * Time.deltaTime);

        if (_clip == null || (_weight <= 0.001f && !_playing)) return;

        if (_playing)
        {
            _time += Time.deltaTime;
            if (_time >= _clip.duration)
            {
                if (_clip.loop) _time = Mathf.Repeat(_time, _clip.duration);
                else { _time = _clip.duration; _playing = false; }
            }
        }

        float tN = Mathf.Clamp01(_time / Mathf.Max(_clip.duration, 0.01f));

        // 3. Apply the new offset (weighted).
        if (target != null)
        {
            Vector3 pos = _clip.SamplePosition(tN) * _weight;
            Vector3 eul = _clip.SampleRotationEuler(tN) * _weight;

            _appliedPos = pos;
            _appliedRot = Quaternion.Euler(eul);

            target.localPosition += _appliedPos;
            target.localRotation  = _appliedRot * target.localRotation;
        }
    }
}
