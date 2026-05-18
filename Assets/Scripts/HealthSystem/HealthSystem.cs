using System;
using UnityEngine;

public class HealthSystem : MonoBehaviour
{
    [SerializeField] private int maxHearts;
                     private float maxHealth;
    [SerializeField] private float currentHealth;

    public void Awake()
    {
        
        if (maxHearts <= 0) maxHearts = 3;
        
        UpdateMaxHealth();
        
        currentHealth = maxHealth;
    }

    public void Damage(float amount)
    {
        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            OnDie();
        }
    }
    
    public void Heal(float amount)
    {
        currentHealth += amount;
        if (currentHealth > maxHealth)
        {
            currentHealth = maxHealth;
        }
    }

    public void ChangeMaxHearts(int amount)
    {
        maxHearts = amount;
        UpdateMaxHealth();
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
