using System.Collections;
using Randomiz.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the dialogue panel. Builds its Canvas/Panel/labels entirely in code.
///
/// Flow:
///   • Listens for <see cref="OnDialogueStartEvent"/> → shows the panel, freezes the
///     player (<see cref="OnSetMovementEnabledEvent"/> false), and types the first line.
///   • Each interact press either finishes the current typewriter line instantly or
///     advances to the next line.
///   • After the last line it hides the panel, re-enables movement, and raises
///     <see cref="OnDialogueEndEvent"/> (which the NPCController uses to open the shop).
/// </summary>
public class DialogueUI : MonoBehaviour
{
    [Header("Typewriter")]
    [SerializeField] private float charDelay = 0.02f;

    private Canvas _canvas;
    private GameObject _root;
    private TMP_Text _speakerLabel;
    private TMP_Text _bodyLabel;
    private TMP_Text _continueLabel;

    private NPCController _npc;
    private string[] _lines;
    private int _lineIndex;
    private Coroutine _typing;
    private bool _isTyping;
    private bool _isOpen;
    private AudioSource _audio;

    private void Awake()
    {
        BuildUI();
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        Hide();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnDialogueStartEvent>(OnDialogueStart);
        EventBus.Subscribe<OnInteractDodgeInputEvent>(OnInteractInput);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnDialogueStartEvent>(OnDialogueStart);
        EventBus.Unsubscribe<OnInteractDodgeInputEvent>(OnInteractInput);
    }

    // ─── UI construction ───────────────────────────────────────────────────

    private void BuildUI()
    {
        _canvas = UIFactory.CreateCanvas("DialogueCanvas");
        _canvas.sortingOrder = 200;
        _canvas.transform.SetParent(transform, false);

        // Dim backdrop + bottom dialogue box.
        _root = _canvas.gameObject;

        var box = UIFactory.CreatePanel(_canvas.transform, new Color(0.05f, 0.05f, 0.08f, 0.95f));
        var boxRt = box.rectTransform;
        boxRt.anchorMin = new Vector2(0.1f, 0.05f);
        boxRt.anchorMax = new Vector2(0.9f, 0.32f);
        boxRt.offsetMin = Vector2.zero;
        boxRt.offsetMax = Vector2.zero;

        _speakerLabel = UIFactory.CreateLabel(box.transform, "", 32, new Color(1f, 0.85f, 0.4f),
            TextAlignmentOptions.TopLeft);
        var sRt = _speakerLabel.rectTransform;
        sRt.anchorMin = new Vector2(0.03f, 0.72f);
        sRt.anchorMax = new Vector2(0.97f, 0.97f);
        sRt.offsetMin = Vector2.zero;
        sRt.offsetMax = Vector2.zero;

        _bodyLabel = UIFactory.CreateLabel(box.transform, "", 26, Color.white,
            TextAlignmentOptions.TopLeft);
        var bRt = _bodyLabel.rectTransform;
        bRt.anchorMin = new Vector2(0.03f, 0.22f);
        bRt.anchorMax = new Vector2(0.97f, 0.72f);
        bRt.offsetMin = Vector2.zero;
        bRt.offsetMax = Vector2.zero;
        _bodyLabel.enableWordWrapping = true;

        _continueLabel = UIFactory.CreateLabel(box.transform, "Press [Interact] to continue", 20,
            new Color(0.7f, 0.7f, 0.7f), TextAlignmentOptions.BottomRight);
        var cRt = _continueLabel.rectTransform;
        cRt.anchorMin = new Vector2(0.03f, 0.03f);
        cRt.anchorMax = new Vector2(0.97f, 0.2f);
        cRt.offsetMin = Vector2.zero;
        cRt.offsetMax = Vector2.zero;
    }

    // ─── Flow ──────────────────────────────────────────────────────────────

    private void OnDialogueStart(OnDialogueStartEvent e)
    {
        if (e?.npc == null || e.npc.Data == null) return;

        _npc = e.npc;
        var line = _npc.CurrentLine;
        _lines = line != null ? line.lines : null;
        _lineIndex = 0;

        if (_lines == null || _lines.Length == 0)
        {
            // Nothing to say — end immediately so shop (if any) still opens.
            EndDialogue();
            return;
        }

        Show();
        _speakerLabel.text = _npc.Data.npcName;

        if (line.voiceClip != null)
            _audio.PlayOneShot(line.voiceClip);

        StartTyping(_lines[_lineIndex]);

        // Freeze the player while the dialogue is open.
        EventBus.Raise(new OnSetMovementEnabledEvent { enabled = false });
    }

    private void OnInteractInput(OnInteractDodgeInputEvent e)
    {
        if (!_isOpen || !e.pressed) return;

        if (_isTyping)
        {
            // First press completes the current line instantly.
            FinishTyping();
            return;
        }

        _lineIndex++;
        if (_lineIndex >= _lines.Length)
        {
            EndDialogue();
            return;
        }
        StartTyping(_lines[_lineIndex]);
    }

    private void EndDialogue()
    {
        Hide();
        EventBus.Raise(new OnSetMovementEnabledEvent { enabled = true });

        var npc = _npc;
        _npc = null;
        _lines = null;
        EventBus.Raise(new OnDialogueEndEvent { npc = npc });
    }

    // ─── Typewriter ────────────────────────────────────────────────────────

    private void StartTyping(string text)
    {
        if (_typing != null) StopCoroutine(_typing);
        _typing = StartCoroutine(TypeRoutine(text));
    }

    private IEnumerator TypeRoutine(string text)
    {
        _isTyping = true;
        _bodyLabel.text = "";
        for (int i = 0; i < text.Length; i++)
        {
            _bodyLabel.text += text[i];
            yield return new WaitForSecondsRealtime(charDelay);
        }
        _isTyping = false;
        _typing = null;
    }

    private void FinishTyping()
    {
        if (_typing != null) StopCoroutine(_typing);
        _typing = null;
        _bodyLabel.text = _lines[_lineIndex];
        _isTyping = false;
    }

    // ─── Visibility ────────────────────────────────────────────────────────

    private void Show()
    {
        _isOpen = true;
        _root.SetActive(true);
    }

    private void Hide()
    {
        _isOpen = false;
        _isTyping = false;
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        _root.SetActive(false);
    }
}
