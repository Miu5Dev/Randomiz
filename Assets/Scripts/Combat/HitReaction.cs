using UnityEngine;

/// <summary>
/// Hurt feedback for the player. On a landed hit:
///   • Knockback — shoves the player away from the attacker.
///   • Zelda blink — alternates _HitFlashAmount (red tint) and _HitAlpha (dither
///     invisible) on the custom cel shader via MaterialPropertyBlock.
///     No material switching, no render-mode changes, no pink artefacts.
///
/// Requires:
///   • HealthSystem.invincibilityDuration > 0 for the blink to run.
///   • Renderers using Custom/CelShading or Custom/PlayerProximity.
/// </summary>
[RequireComponent(typeof(HealthSystem))]
public class HitReaction : MonoBehaviour
{
    [Header("Knockback")]
    [SerializeField] private float knockbackForce    = 6f;
    [SerializeField] private float knockbackUp       = 2f;
    [SerializeField] private float knockbackDuration = 0.2f;

    [Header("Hurt Blink  (red ↔ invisible, Zelda-style)")]
    [Tooltip("Seconds per toggle between red and invisible.")]
    [SerializeField] private float blinkInterval = 0.07f;
    [Tooltip("Renderers to affect. Auto-collected from children if left empty.")]
    [SerializeField] private Renderer[] renderers;

    private HealthSystem   _health;
    private PlayerMovement _movement;
    private MaterialPropertyBlock _mpb;
    private float _blinkTimer;
    private bool  _showingRed;
    private bool  _wasInvincible;

    private static readonly int HitFlashAmountID = Shader.PropertyToID("_HitFlashAmount");
    private static readonly int HitAlphaID       = Shader.PropertyToID("_HitAlpha");

    private void Awake()
    {
        _health   = GetComponent<HealthSystem>();
        _movement = GetComponent<PlayerMovement>();
        _mpb      = new MaterialPropertyBlock();

        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void OnEnable()  => EventBus.Subscribe<OnDamagedEvent>(OnDamaged);
    private void OnDisable()
    {
        EventBus.Unsubscribe<OnDamagedEvent>(OnDamaged);
        Restore();
    }

    private void OnDamaged(OnDamagedEvent e)
    {
        if (e.victim != gameObject) return;
        if (_movement != null && e.attacker != null)
            _movement.ApplyKnockback(e.attacker.transform.position,
                                     knockbackForce, knockbackUp, knockbackDuration);
    }

    private void Update()
    {
        bool invincible = _health != null && _health.IsInvincible;

        if (invincible)
        {
            _blinkTimer -= Time.deltaTime;
            if (_blinkTimer <= 0f)
            {
                _showingRed = !_showingRed;
                _blinkTimer = blinkInterval;
                ApplyBlink(_showingRed);
            }
        }
        else if (_wasInvincible)
        {
            Restore();
        }

        _wasInvincible = invincible;
    }

    // ─── Shader property helpers ─────────────────────────────────────────────

    private void ApplyBlink(bool red)
    {
        // Red phase   → full tint, fully visible (dither passes all pixels)
        // Blink phase → no tint, fully invisible (dither clips all pixels)
        float flash = red ? 1f : 0f;
        float alpha = red ? 1f : 0f;
        SetProperties(flash, alpha);
    }

    private void Restore()
    {
        _showingRed = false;
        _blinkTimer = 0f;
        SetProperties(0f, 1f);  // no flash, fully visible
    }

    private void SetProperties(float flashAmount, float hitAlpha)
    {
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(HitFlashAmountID, flashAmount);
            _mpb.SetFloat(HitAlphaID,       hitAlpha);
            r.SetPropertyBlock(_mpb);
        }
    }
}
