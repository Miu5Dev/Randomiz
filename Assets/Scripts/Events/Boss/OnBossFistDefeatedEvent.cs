/// <summary>
/// Raised when a boss fist's HP reaches zero.
/// BossBodyController listens to this to count fist kills and trigger phase 2.
/// </summary>
public class OnBossFistDefeatedEvent
{
    /// <summary>The fist that was just destroyed.</summary>
    public BossFistController fist;
}
