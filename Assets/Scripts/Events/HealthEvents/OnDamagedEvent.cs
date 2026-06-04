using UnityEngine;

/// <summary>
/// Raised by <see cref="HealthSystem"/> when damage actually lands (i.e. it was
/// not blocked by invincibility frames and didn't kill the entity). Carries the
/// attacker so reactions like knockback can compute a direction.
/// Hit/death feedback (knockback, hurt flash) subscribes to this.
/// </summary>
public class OnDamagedEvent
{
    public GameObject victim;
    public GameObject attacker;
    public float      damage;
}
