/// <summary>
/// Merchant temperament. Drives the shop price multiplier:
///   FairMerchant = 1.0, Undervalues = 0.6, Overcharges = 2.0.
///   RandomPerRun = one of the above, chosen once per seed (deterministic).
/// </summary>
public enum NPCPersonality
{
    FairMerchant,
    Undervalues,
    Overcharges,
    RandomPerRun,   // resolved once per run from the randomizer seed + npcId
}
