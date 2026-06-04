using UnityEngine;

/// <summary>
/// Red flash when an entity takes damage. Drives the cel shader's _HitFlashAmount
/// (and _HitFlashColor) via MaterialPropertyBlock: snaps to full red on hit, then
/// fades back to zero over flashDuration for a clear, punchy tint.
///
/// Requires the renderers to use Custom/CelShading (or Custom/PlayerProximity) —
/// those expose _HitFlashAmount / _HitFlashColor.
/// </summary>
public class HitFlash : MonoBehaviour
{
    [Tooltip("Tint colour at the peak of the flash.")]
    [SerializeField] private Color flashColor = new Color(1f, 0.05f, 0.05f, 1f);
    [Tooltip("Peak flash strength (1 = fully replace albedo with the flash colour).")]
    [Range(0f, 1f)] [SerializeField] private float peakAmount = 1f;
    [Tooltip("How long the flash takes to fade back to normal.")]
    [SerializeField] private float flashDuration = 0.28f;
    [Tooltip("Renderers to affect. Auto-collected from children if left empty.")]
    [SerializeField] private Renderer[] renderers;

    private MaterialPropertyBlock _mpb;
    private float _timer;

    private static readonly int HitFlashAmountID = Shader.PropertyToID("_HitFlashAmount");
    private static readonly int HitFlashColorID  = Shader.PropertyToID("_HitFlashColor");

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void OnEnable()  => EventBus.Subscribe<OnDamagedEvent>(OnDamaged);
    private void OnDisable()
    {
        EventBus.Unsubscribe<OnDamagedEvent>(OnDamaged);
        SetAmount(0f);
    }

    private void OnDamaged(OnDamagedEvent e)
    {
        if (e.victim != gameObject) return;
        _timer = flashDuration;          // (re)trigger — snaps to full
        SetAmount(peakAmount);
    }

    private void Update()
    {
        if (_timer <= 0f) return;
        _timer -= Time.deltaTime;
        float t = Mathf.Clamp01(_timer / flashDuration);   // 1 -> 0
        SetAmount(peakAmount * t);
    }

    private void SetAmount(float amount)
    {
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(HitFlashAmountID, amount);
            _mpb.SetColor(HitFlashColorID, flashColor);
            r.SetPropertyBlock(_mpb);
        }
    }
}
