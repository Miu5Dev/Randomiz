using UnityEngine;

/// <summary>
/// A single conversation: a sequence of spoken lines plus an optional voice clip.
/// An NPC owns a pool of these and randomly picks one per conversation.
/// </summary>
[CreateAssetMenu(fileName = "DialogueLine", menuName = "NPC/Dialogue Line")]
public class SODialogueLine : ScriptableObject
{
    [TextArea(2, 5)]
    [Tooltip("Lines spoken in sequence; each advances on the interact press.")]
    public string[] lines;

    [Tooltip("Optional voice clip played when this conversation starts.")]
    public AudioClip voiceClip;
}
