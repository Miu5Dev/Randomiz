using System;
using UnityEngine;

public class HealthSystem : MonoBehaviour
{
    [SerializeField] private int maxHearts;
                     private float maxHealth;
    [SerializeField] private float currentHealth;

    
    public void OnDamageEvent(OnDamageDealtEvent e)
    {
        if (e.Target != this.gameObject) return;
        Damage(e.Damage);
    }
    
    public void OnHealEvent(OnHealReceivedEvent e)
    {
        if (e.Target != this.gameObject) return;
        Heal(e.Amount);
    }
    
    public void Awake()
    {
        if (maxHearts <= 0) maxHearts = 3;
        UpdateMaxHealth();
        currentHealth = maxHealth;
        PublishHealthChanged();
    }

    public void Damage(float amount)
    {
        currentHealth = Mathf.Max(0f, currentHealth - amount);
        PublishHealthChanged();
        if (currentHealth <= 0) OnDie();
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

    private void PublishHealthChanged()
    {
        EventBus.Raise(new OnHealthChangedEvent
        {
            currentHealth = currentHealth,
            maxHearts = maxHearts
        });
    }   
    
    private void UpdateMaxHealth()
    {
        maxHealth = maxHearts * 4;
    }
    
    
    private void OnDie()
    {
        EventBus.Raise(new OnDieEvent()
        {
            murdered = this.gameObject
        });
    }
    
}
