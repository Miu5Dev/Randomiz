using System.IO;
using UnityEngine;

/// <summary>
/// Drives an NPC in the world. Hook <see cref="OnInteract"/> to the
/// <see cref="Interactable"/>'s onUse UnityEvent (or call it directly): it raises
/// <see cref="OnDialogueStartEvent"/> so the DialogueUI shows a conversation, and
/// — for shopkeepers — opens the shop once the dialogue ends.
///
/// Conversation counts per NPC are persisted (the SaveManager integration point).
/// </summary>
[RequireComponent(typeof(Interactable))]
public class NPCController : MonoBehaviour
{
    [SerializeField] private NPCData data;

    private ShopInventory _shop;
    private int _conversationCount;
    private bool _waitingForDialogueEnd;

    public NPCData Data => data;
    public ShopInventory Shop => _shop;

    /// <summary>The dialogue line chosen for the current/last conversation.</summary>
    public SODialogueLine CurrentLine { get; private set; }

    /// <summary>How many times the player has talked to this NPC (persisted).</summary>
    public int ConversationCount => _conversationCount;

    private void Awake()
    {
        _shop = GetComponent<ShopInventory>();
        LoadConversations();
    }

    private void OnEnable()  => EventBus.Subscribe<OnDialogueEndEvent>(OnDialogueEnd);
    private void OnDisable() => EventBus.Unsubscribe<OnDialogueEndEvent>(OnDialogueEnd);

    /// <summary>
    /// Entry point for interaction — wire this to the Interactable.onUse event.
    /// Picks a random conversation and starts the dialogue flow.
    /// </summary>
    public void OnInteract()
    {
        if (data == null) return;

        CurrentLine = PickDialogue();
        _conversationCount++;
        SaveConversations();

        if (data.isShopkeeper) _shop?.EnsureGenerated();

        _waitingForDialogueEnd = true;
        EventBus.Raise(new OnDialogueStartEvent { npc = this });
    }

    private SODialogueLine PickDialogue()
    {
        if (data.dialoguePool == null || data.dialoguePool.Length == 0) return null;
        int i = Random.Range(0, data.dialoguePool.Length);
        return data.dialoguePool[i];
    }

    private void OnDialogueEnd(OnDialogueEndEvent e)
    {
        // Only react to the conversation we started.
        if (e.npc != this || !_waitingForDialogueEnd) return;
        _waitingForDialogueEnd = false;

        if (data.isShopkeeper)
        {
            _shop?.EnsureGenerated();
            ShopUI.Open(this);
        }
    }

    // ─── Persistence (SaveManager integration point) ───────────────────────

    [System.Serializable]
    private class NPCSaveData { public int conversationCount; }

    private string SavePath =>
        Path.Combine(Application.persistentDataPath, $"npc_{data?.npcId}.json");

    private void SaveConversations()
    {
        if (data == null) return;
        var save = new NPCSaveData { conversationCount = _conversationCount };
        File.WriteAllText(SavePath, JsonUtility.ToJson(save));
    }

    private void LoadConversations()
    {
        if (data == null || !File.Exists(SavePath)) return;
        try
        {
            var save = JsonUtility.FromJson<NPCSaveData>(File.ReadAllText(SavePath));
            if (save != null) _conversationCount = save.conversationCount;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[NPCController] Load failed for {data.npcId}: {ex.Message}");
        }
    }
}
