using UnityEngine;

/// <summary>
/// Health container for the player (or any other entity).
///
/// Self-subscribes to OnDamageDealtEvent and OnHealReceivedEvent — no inspector
/// binding required. Publishes OnHealthChangedEvent whenever health changes so
/// UI (HeartsDisplay) and other listeners stay in sync.
///
/// Health is stored as a float; each "heart" represents 4 health units (quarter-heart
/// resolution). Set <see cref="maxHearts"/> in the inspector or change at runtime via
/// <see cref="ChangeMaxHearts"/>.
/// </summary>
[DisallowMultipleComponent]
public class HealthSystem : MonoBehaviour
{
    [SerializeField] private int maxHearts;
                     private float maxHealth;
    [SerializeField] private float currentHealth;

    [Header("Invincibility")]
    [Tooltip("Seconds of invincibility after taking a hit (i-frames). 0 = disabled (e.g. enemies).")]
    [SerializeField] private float invincibilityDuration = 0f;

    private float _invincibleUntil;

    /// <summary>Current health as a 0..1 fraction of max. Safe before init.</summary>
    public float Normalized => maxHealth > 0f ? currentHealth / maxHealth : 0f;

    /// <summary>Raw current health value. Exposed for the save system to snapshot/restore.</summary>
    public float CurrentHealth => currentHealth;

    /// <summary>
    /// Restores health to an exact value (clamped to max). Used by the save system on
    /// load. Publishes OnHealthChangedEvent so the HUD stays in sync.
    /// </summary>
    public void SetHealth(float value)
    {
        currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        _invincibleUntil = 0f;   // clear any lingering i-frames (e.g. after respawn)
        PublishHealthChanged();
    }

    /// <summary>True while this entity still has health left.</summary>
    public bool IsAlive => currentHealth > 0f;

    /// <summary>True while i-frames are active (damage is ignored, used to drive the hurt flash).</summary>
    public bool IsInvincible => invincibilityDuration > 0f && Time.time < _invincibleUntil;

    private void Awake()
    {
        // Guard against duplicate HealthSystem components on the same GameObject.
        // [DisallowMultipleComponent] prevents adding new ones, but existing duplicates
        // in saved scenes are destroyed here so they never subscribe to OnDamageDealtEvent.
        HealthSystem[] siblings = GetComponents<HealthSystem>();
        if (siblings.Length > 1 && siblings[0] != this)
        {
            Debug.LogWarning($"[HealthSystem] Duplicate on '{gameObject.name}' — destroying extra instance.", this);
            Destroy(this);
            return;
        }

        if (maxHearts <= 0) maxHearts = 3;
        UpdateMaxHealth();
        // Only set to full when uninitialized; preserves values loaded from a save file.
        if (currentHealth <= 0f) currentHealth = maxHealth;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnDamageDealtEvent>(OnDamageEvent);
        EventBus.Subscribe<OnHealReceivedEvent>(OnHealEvent);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnDamageDealtEvent>(OnDamageEvent);
        EventBus.Unsubscribe<OnHealReceivedEvent>(OnHealEvent);
    }

    private void Start()
    {
        // Published in Start (not Awake) so UI subscribers attached in their own OnEnable
        // are guaranteed to receive the initial value.
        PublishHealthChanged();
    }

    // ─── Event handlers ────────────────────────────────────────────────────

    private void OnDamageEvent(OnDamageDealtEvent e)
    {
        if (e.Target != gameObject) return;
        if (!IsAlive) return;                // dead entities ignore further damage
        if (IsInvincible) return;            // i-frames absorb the hit
        Damage(e.Damage, e.Attacker);
    }

    private void OnHealEvent(OnHealReceivedEvent e)
    {
        if (e.Target != gameObject) return;
        Heal(e.Amount);
    }

    // ─── Public API ────────────────────────────────────────────────────────

    public void Damage(float amount) => Damage(amount, null);

    public void Damage(float amount, GameObject attacker)
    {
        if (amount <= 0f) return;

        // Start i-frames immediately — before health reduction and before OnDie.
        // This ensures that even a fatal hit grants i-frames so combo follow-ups
        // are blocked while the death animation plays.
        if (invincibilityDuration > 0f)
            _invincibleUntil = Time.time + invincibilityDuration;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        PublishHealthChanged();

        // Always announce the hit (HitFlash on enemies, HitReaction on player all listen here).
        EventBus.Raise(new OnDamagedEvent { victim = gameObject, attacker = attacker, damage = amount });

        if (currentHealth <= 0f) { OnDie(); }
    }

    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        PublishHealthChanged();
    }

    public void ChangeMaxHearts(int amount)
    {
        maxHearts = amount;
        UpdateMaxHealth();
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        PublishHealthChanged();
    }

    // ─── Internals ─────────────────────────────────────────────────────────

    /// <summary>
    /// Sets max hearts and restores health to full. Use this to initialize enemies from SOEnemy data.
    /// </summary>
    public void Initialize(int hearts)
    {
        maxHearts = Mathf.Max(1, hearts);
        UpdateMaxHealth();
        currentHealth = maxHealth;
        PublishHealthChanged();
    }

    private void PublishHealthChanged()
    {
        EventBus.Raise(new OnHealthChangedEvent
        {
            currentHealth = currentHealth,
            maxHearts     = maxHearts,
            target        = gameObject
        });
    }

    private void UpdateMaxHealth() => maxHealth = maxHearts * 4;

    private void OnDie() => EventBus.Raise(new OnDieEvent { murdered = gameObject });
}
