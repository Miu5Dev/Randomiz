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
public class HealthSystem : MonoBehaviour
{
    [SerializeField] private int maxHearts;
                     private float maxHealth;
    [SerializeField] private float currentHealth;

    private void Awake()
    {
        if (maxHearts <= 0) maxHearts = 3;
        UpdateMaxHealth();
        currentHealth = maxHealth;
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
        Damage(e.Damage);
    }

    private void OnHealEvent(OnHealReceivedEvent e)
    {
        if (e.Target != gameObject) return;
        Heal(e.Amount);
    }

    // ─── Public API ────────────────────────────────────────────────────────

    public void Damage(float amount)
    {
        currentHealth = Mathf.Max(0f, currentHealth - amount);
        PublishHealthChanged();
        if (currentHealth <= 0f) OnDie();
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

    private void PublishHealthChanged()
    {
        EventBus.Raise(new OnHealthChangedEvent
        {
            currentHealth = currentHealth,
            maxHearts = maxHearts
        });
    }

    private void UpdateMaxHealth() => maxHealth = maxHearts * 4;

    private void OnDie() => EventBus.Raise(new OnDieEvent { murdered = gameObject });
}
