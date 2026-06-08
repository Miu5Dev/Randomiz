using UnityEngine;

/// <summary>
/// Static data describing an NPC: identity, personality, dialogue pool, and
/// (if a shopkeeper) the item pool and stock size used to generate the shop.
/// </summary>
[CreateAssetMenu(fileName = "NPCData", menuName = "NPC/NPC Data")]
public class NPCData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable unique id — used as the save key and as part of the shop RNG seed.")]
    public string npcId = "npc_unknown";
    public string npcName = "Unnamed";

    [Header("Personality")]
    public NPCPersonality personality = NPCPersonality.FairMerchant;

    [Header("Dialogue")]
    [Tooltip("One of these conversations is picked at random each time the player talks.")]
    public SODialogueLine[] dialoguePool;

    [Header("Shop")]
    public bool isShopkeeper;

    [Tooltip("Pool the shop stock is rolled from. Reuses the randomizer item pool.")]
    public SOItemPool shopItemPool;

    [Min(1)]
    public int shopInventorySize = 4;

    /// <summary>Price multiplier derived from the personality.</summary>
    public float PriceMultiplier => personality switch
    {
        NPCPersonality.Undervalues  => 0.6f,
        NPCPersonality.Overcharges  => 2.0f,
        _                           => 1.0f,
    };
}
