/// <summary>
/// Raised when a conversation finishes (last line dismissed). The NPCController
/// listens for this to open the shop UI afterwards if the NPC is a shopkeeper.
/// </summary>
public class OnDialogueEndEvent
{
    public NPCController npc;
}
