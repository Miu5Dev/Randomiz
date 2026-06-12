/// <summary>
/// Raised when a conversation finishes. The NPCController listens for this to open
/// the shop UI afterwards if the NPC is a shopkeeper.
///
/// <see cref="cancelled"/> is true when the player backed out with Esc instead of
/// reading to the end — in that case the shop must NOT open.
/// </summary>
public class OnDialogueEndEvent
{
    public NPCController npc;
    public bool cancelled;
}
