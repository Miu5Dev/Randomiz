/// <summary>Raised when an entity's health changes; drives the hearts HUD (current health, max hearts, target).</summary>
public class OnHealthChangedEvent
{
    public float currentHealth;
    public int maxHearts;
    public UnityEngine.GameObject target;
}
