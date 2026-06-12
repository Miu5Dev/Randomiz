using System.Collections;
using System.Collections.Generic;
using Randomiz.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shop panel, built entirely in code. Shows the NPC's stock as a grid of entries
/// (icon, name, price), the player's current coin count, and a selection highlight.
///
/// Input:
///   • Move navigates the selection (left/right/up/down).
///   • Interact OR a mouse click opens a buy confirmation; Interact again / the Buy
///     button confirms (with "Not enough coins" / "Sold out" feedback).
///   • Pause/back (Esc) backs out of the confirmation first, then closes the shop.
///
/// Opened via the static <see cref="Open"/> helper, which the NPCController calls
/// after a shopkeeper's dialogue ends.
/// </summary>
public class ShopUI : MonoBehaviour
{
    private const int Columns = 2;

    // ── Singleton-ish access so NPCController can open without a wired reference ──
    private static ShopUI _instance;

    private Canvas _canvas;
    private GameObject _root;
    private TMP_Text _titleLabel;
    private TMP_Text _coinLabel;
    private TMP_Text _feedbackLabel;
    private GameObject _gridGo;

    private NPCController _npc;
    private ShopInventory _shop;
    private readonly List<Entry> _entries = new();
    private int _selected;
    private bool _isOpen;

    private float _navCooldown;
    private Coroutine _feedbackRoutine;

    // Purchase confirmation dialog (built in code, shown over the grid).
    private GameObject _confirmRoot;
    private TMP_Text _confirmMessage;
    private bool _confirmOpen;

    private class Entry
    {
        public SOItem item;
        public Image background;
        public TMP_Text priceLabel;
        public TMP_Text nameLabel;
    }

    private void Awake()
    {
        _instance = this;
        BuildShell();
        HideImmediate();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnMoveInputEvent>(OnMove);
        // Priority 20: must run before Interactor (10) so a confirm press while shopping
        // is consumed here and doesn't re-trigger the NPC's dialogue.
        EventBus.Subscribe<OnInteractDodgeInputEvent>(OnConfirm, 20);
        // Priority 20: above PauseMenuUI (10) so a back/escape press while shopping
        // closes the shop and is consumed — the pause menu must NOT also open.
        EventBus.Subscribe<OnPauseInputEvent>(OnBack, 20);
        // Block all gameplay inputs that must not fire while the shop is open.
        // Priority 20 matches OnBack so they all run before InventoryWheelUI (10) and TargetingSystem (0).
        EventBus.Subscribe<OnTargetInputEvent>(OnBlockTarget, 20);
        EventBus.Subscribe<OnAttackInputEvent>(OnBlockAttack, 20);
        EventBus.Subscribe<OnInventoryInputEvent>(OnBlockInventory, 20);
        EventBus.Subscribe<OnItemOneInputEvent>(OnBlockItemOne, 20);
        EventBus.Subscribe<OnItemTwoInputEvent>(OnBlockItemTwo, 20);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnMoveInputEvent>(OnMove);
        EventBus.Unsubscribe<OnInteractDodgeInputEvent>(OnConfirm);
        EventBus.Unsubscribe<OnPauseInputEvent>(OnBack);
        EventBus.Unsubscribe<OnTargetInputEvent>(OnBlockTarget);
        EventBus.Unsubscribe<OnAttackInputEvent>(OnBlockAttack);
        EventBus.Unsubscribe<OnInventoryInputEvent>(OnBlockInventory);
        EventBus.Unsubscribe<OnItemOneInputEvent>(OnBlockItemOne);
        EventBus.Unsubscribe<OnItemTwoInputEvent>(OnBlockItemTwo);
    }

    /// <summary>Open the shop for the given shopkeeper NPC.</summary>
    public static void Open(NPCController npc)
    {
        if (_instance == null)
        {
            Debug.LogWarning("[ShopUI] No ShopUI in scene to open.");
            return;
        }
        _instance.OpenInternal(npc);
    }

    // ─── Construction ──────────────────────────────────────────────────────

    private void BuildShell()
    {
        _canvas = UIFactory.CreateCanvas("ShopCanvas");
        _canvas.sortingOrder = 210;
        _canvas.transform.SetParent(transform, false);
        _root = _canvas.gameObject;

        UIFactory.CreatePanel(_canvas.transform, new Color(0f, 0f, 0f, 0.6f));

        var window = UIFactory.CreatePanel(_canvas.transform, new Color(0.08f, 0.08f, 0.12f, 0.98f));
        var wRt = window.rectTransform;
        wRt.anchorMin = new Vector2(0.2f, 0.15f);
        wRt.anchorMax = new Vector2(0.8f, 0.85f);
        wRt.offsetMin = Vector2.zero;
        wRt.offsetMax = Vector2.zero;

        _titleLabel = UIFactory.CreateLabel(window.transform, "Shop", 34, new Color(1f, 0.85f, 0.4f));
        var tRt = _titleLabel.rectTransform;
        tRt.anchorMin = new Vector2(0.05f, 0.88f);
        tRt.anchorMax = new Vector2(0.95f, 0.98f);
        tRt.offsetMin = Vector2.zero;
        tRt.offsetMax = Vector2.zero;

        _coinLabel = UIFactory.CreateLabel(window.transform, "Coins: 0", 24,
            new Color(1f, 0.9f, 0.5f), TextAlignmentOptions.TopRight);
        var cRt = _coinLabel.rectTransform;
        cRt.anchorMin = new Vector2(0.55f, 0.82f);
        cRt.anchorMax = new Vector2(0.95f, 0.9f);
        cRt.offsetMin = Vector2.zero;
        cRt.offsetMax = Vector2.zero;

        // Grid container.
        _gridGo = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
        _gridGo.transform.SetParent(window.transform, false);
        var gRt = _gridGo.GetComponent<RectTransform>();
        gRt.anchorMin = new Vector2(0.05f, 0.18f);
        gRt.anchorMax = new Vector2(0.95f, 0.8f);
        gRt.offsetMin = Vector2.zero;
        gRt.offsetMax = Vector2.zero;
        var grid = _gridGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(380f, 110f);
        grid.spacing = new Vector2(20f, 20f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;
        grid.childAlignment = TextAnchor.UpperCenter;

        _feedbackLabel = UIFactory.CreateLabel(window.transform, "", 22, new Color(1f, 0.4f, 0.4f));
        var fRt = _feedbackLabel.rectTransform;
        fRt.anchorMin = new Vector2(0.05f, 0.1f);
        fRt.anchorMax = new Vector2(0.95f, 0.17f);
        fRt.offsetMin = Vector2.zero;
        fRt.offsetMax = Vector2.zero;

        var hint = UIFactory.CreateLabel(window.transform,
            "[Interact] / Click Buy    [Esc] Close", 18, new Color(0.6f, 0.6f, 0.6f));
        var hRt = hint.rectTransform;
        hRt.anchorMin = new Vector2(0.05f, 0.02f);
        hRt.anchorMax = new Vector2(0.95f, 0.09f);
        hRt.offsetMin = Vector2.zero;
        hRt.offsetMax = Vector2.zero;

        BuildConfirmDialog();
    }

    /// <summary>
    /// Full-screen confirm overlay shown when the player picks an item. Its panel
    /// blocks clicks to the grid beneath; Buy/Cancel resolve the pending purchase.
    /// </summary>
    private void BuildConfirmDialog()
    {
        var overlay = UIFactory.CreatePanel(_canvas.transform, new Color(0f, 0f, 0f, 0.7f));
        UIFactory.StretchToParent(overlay.rectTransform);
        _confirmRoot = overlay.gameObject;

        var card = UIFactory.CreatePanel(overlay.transform, new Color(0.12f, 0.12f, 0.16f, 1f));
        UIFactory.CenterWithSize(card.rectTransform, new Vector2(480f, 240f));

        _confirmMessage = UIFactory.CreateLabel(card.transform, "", 26, Color.white);
        var mRt = _confirmMessage.rectTransform;
        mRt.anchorMin = new Vector2(0.05f, 0.42f);
        mRt.anchorMax = new Vector2(0.95f, 0.95f);
        mRt.offsetMin = Vector2.zero;
        mRt.offsetMax = Vector2.zero;

        var btnRow = UIFactory.CreateLayoutGroup(card.transform, vertical: false, spacing: 24f);
        var rRt = btnRow.GetComponent<RectTransform>();
        rRt.anchorMin = new Vector2(0.05f, 0.08f);
        rRt.anchorMax = new Vector2(0.95f, 0.4f);
        rRt.offsetMin = Vector2.zero;
        rRt.offsetMax = Vector2.zero;

        UIFactory.CreateButton(btnRow.transform, "Buy", 22, ConfirmPurchase)
            .GetComponent<RectTransform>().sizeDelta = new Vector2(180f, 58f);
        UIFactory.CreateButton(btnRow.transform, "Cancel", 22, CancelPurchase)
            .GetComponent<RectTransform>().sizeDelta = new Vector2(180f, 58f);

        _confirmRoot.SetActive(false);
    }

    private void BuildEntries()
    {
        // Clear previous entries.
        for (int i = _gridGo.transform.childCount - 1; i >= 0; i--)
            Destroy(_gridGo.transform.GetChild(i).gameObject);
        _entries.Clear();

        if (_shop == null) return;

        foreach (var item in _shop.Stock)
        {
            var cell = UIFactory.CreatePanel(_gridGo.transform, new Color(0.15f, 0.15f, 0.2f, 1f));
            cell.gameObject.name = "ShopEntry";

            var icon = UIFactory.CreateImage(cell.transform, item.itemSprite);
            icon.preserveAspect = true;
            var iRt = icon.rectTransform;
            iRt.anchorMin = new Vector2(0.02f, 0.1f);
            iRt.anchorMax = new Vector2(0.28f, 0.9f);
            iRt.offsetMin = Vector2.zero;
            iRt.offsetMax = Vector2.zero;

            var nameLabel = UIFactory.CreateLabel(cell.transform, item.itemName, 22, Color.white,
                TextAlignmentOptions.Left);
            var nRt = nameLabel.rectTransform;
            nRt.anchorMin = new Vector2(0.32f, 0.5f);
            nRt.anchorMax = new Vector2(0.98f, 0.9f);
            nRt.offsetMin = Vector2.zero;
            nRt.offsetMax = Vector2.zero;

            int price = _shop.GetPrice(item);
            var priceLabel = UIFactory.CreateLabel(cell.transform, $"{price} coins", 20,
                new Color(1f, 0.9f, 0.5f), TextAlignmentOptions.Left);
            var pRt = priceLabel.rectTransform;
            pRt.anchorMin = new Vector2(0.32f, 0.1f);
            pRt.anchorMax = new Vector2(0.98f, 0.5f);
            pRt.offsetMin = Vector2.zero;
            pRt.offsetMax = Vector2.zero;

            // Mouse support: clicking an entry selects it; clicking the selected entry buys.
            // Transition = None so Unity's tint doesn't fight our manual highlight in RefreshState.
            var btn = cell.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            int index = _entries.Count;
            btn.onClick.AddListener(() => OnEntryClicked(index));

            _entries.Add(new Entry
            {
                item = item,
                background = cell,
                priceLabel = priceLabel,
                nameLabel = nameLabel,
            });
        }
    }

    // ─── Open / close ──────────────────────────────────────────────────────

    private void OpenInternal(NPCController npc)
    {
        if (npc == null || npc.Shop == null) return;

        _npc = npc;
        _shop = npc.Shop;
        _shop.EnsureGenerated();

        _titleLabel.text = $"{npc.Data.npcName}'s Shop";
        BuildEntries();
        _selected = 0;
        RefreshState();
        SetFeedback("");
        CloseConfirm();

        _isOpen = true;
        _root.SetActive(true);

        // Gameplay hides/locks the cursor — free it so the player can click entries.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Freeze the player AND the camera while shopping (time isn't paused, so the
        // mouse would otherwise still drive look and spin the camera behind the panel).
        EventBus.Raise(new OnSetMovementEnabledEvent { enabled = false });
        EventBus.Raise(new OnSetCameraEnabledEvent { enabled = false });
    }

    private void Close()
    {
        HideImmediate();

        EventBus.Raise(new OnSetMovementEnabledEvent { enabled = true });
        EventBus.Raise(new OnSetCameraEnabledEvent { enabled = true });
        // Defer cursor lock by one frame: Unity processes ESC internally on the same
        // frame and would override an immediate CursorLockMode.Locked assignment.
        StartCoroutine(RestoreCursorLock());
    }

    private IEnumerator RestoreCursorLock()
    {
        yield return null;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void HideImmediate()
    {
        _isOpen = false;
        CloseConfirm();
        if (_root != null) _root.SetActive(false);
    }

    private void CloseConfirm()
    {
        _confirmOpen = false;
        if (_confirmRoot != null) _confirmRoot.SetActive(false);
    }

    // ─── Input ─────────────────────────────────────────────────────────────

    private void OnMove(OnMoveInputEvent e)
    {
        if (!_isOpen || _confirmOpen || !e.pressed || _entries.Count == 0) return;
        if (Time.unscaledTime < _navCooldown) return;

        Vector2 d = e.Direction;
        if (d.sqrMagnitude < 0.25f) return;

        int next = _selected;
        if (Mathf.Abs(d.x) > Mathf.Abs(d.y))
            next += d.x > 0 ? 1 : -1;          // horizontal
        else
            next += d.y > 0 ? -Columns : Columns; // vertical (up = earlier rows)

        next = Mathf.Clamp(next, 0, _entries.Count - 1);
        if (next != _selected)
        {
            _selected = next;
            RefreshState();
        }
        _navCooldown = Time.unscaledTime + 0.18f;
    }

    private void OnConfirm(OnInteractDodgeInputEvent e)
    {
        if (!_isOpen || !e.pressed) return;
        // Consume the press so Interactor (priority 10) doesn't re-trigger the NPC.
        EventBus.Cancel<OnInteractDodgeInputEvent>();
        if (_entries.Count == 0) return;
        // Interact/dodge doubles as the dialog's confirm button while it's open.
        if (_confirmOpen) ConfirmPurchase();
        else RequestPurchase();
    }

    /// <summary>Mouse click on an entry: selects it and opens the buy confirmation.</summary>
    private void OnEntryClicked(int index)
    {
        if (!_isOpen || _confirmOpen || index < 0 || index >= _entries.Count) return;
        _selected = index;
        RefreshState();
        RequestPurchase();
    }

    /// <summary>Validates the selected entry and pops the confirm dialog (or feedback).</summary>
    private void RequestPurchase()
    {
        if (_selected < 0 || _selected >= _entries.Count) return;

        var entry = _entries[_selected];
        if (_shop.IsSold(entry.item))
        {
            SetFeedback("Sold out", new Color(0.7f, 0.7f, 0.7f));
            return;
        }

        int price = _shop.GetPrice(entry.item);
        _confirmMessage.text = $"Buy {entry.item.itemName}\nfor {price} coins?";
        _confirmOpen = true;
        _confirmRoot.SetActive(true);
    }

    /// <summary>Dialog "Buy" pressed — charge and grant, with the usual feedback.</summary>
    private void ConfirmPurchase()
    {
        CloseConfirm();
        if (_selected < 0 || _selected >= _entries.Count) return;

        var entry = _entries[_selected];
        if (_shop.IsSold(entry.item))
        {
            SetFeedback("Sold out", new Color(0.7f, 0.7f, 0.7f));
            return;
        }

        int price = _shop.GetPrice(entry.item);
        if (InventoryHandler.Instance == null || InventoryHandler.Instance.Coins < price)
        {
            SetFeedback("Not enough coins", new Color(1f, 0.4f, 0.4f));
            return;
        }

        if (_shop.TryPurchase(entry.item))
        {
            SetFeedback($"Bought {entry.item.itemName}", new Color(0.5f, 1f, 0.5f));
            RefreshState();
        }
        else
        {
            SetFeedback("Can't buy that", new Color(1f, 0.4f, 0.4f));
        }
    }

    /// <summary>Dialog "Cancel" pressed — dismiss without buying.</summary>
    private void CancelPurchase() => CloseConfirm();

    private void OnBack(OnPauseInputEvent e)
    {
        if (!_isOpen || !e.pressed) return;
        // Consume the press so PauseMenuUI (lower priority) doesn't also open.
        EventBus.Cancel<OnPauseInputEvent>();
        // Esc backs out of the confirm dialog first, then out of the shop.
        if (_confirmOpen) CancelPurchase();
        else Close();
    }

    private void OnBlockTarget(OnTargetInputEvent e)    { if (_isOpen) EventBus.Cancel<OnTargetInputEvent>(); }
    private void OnBlockAttack(OnAttackInputEvent e)    { if (_isOpen) EventBus.Cancel<OnAttackInputEvent>(); }
    private void OnBlockInventory(OnInventoryInputEvent e) { if (_isOpen) EventBus.Cancel<OnInventoryInputEvent>(); }
    private void OnBlockItemOne(OnItemOneInputEvent e)  { if (_isOpen) EventBus.Cancel<OnItemOneInputEvent>(); }
    private void OnBlockItemTwo(OnItemTwoInputEvent e)  { if (_isOpen) EventBus.Cancel<OnItemTwoInputEvent>(); }

    // ─── Visuals ───────────────────────────────────────────────────────────

    private void RefreshState()
    {
        if (InventoryHandler.Instance != null)
            _coinLabel.text = $"Coins: {InventoryHandler.Instance.Coins}";

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            bool sold = _shop.IsSold(entry.item);
            bool sel = i == _selected;

            entry.background.color = sel
                ? new Color(0.3f, 0.35f, 0.5f, 1f)
                : new Color(0.15f, 0.15f, 0.2f, 1f);

            entry.priceLabel.text = sold ? "SOLD" : $"{_shop.GetPrice(entry.item)} coins";
            entry.nameLabel.color = sold ? new Color(0.5f, 0.5f, 0.5f) : Color.white;
        }
    }

    private void SetFeedback(string text, Color color = default)
    {
        if (_feedbackRoutine != null) StopCoroutine(_feedbackRoutine);
        _feedbackLabel.text = text;
        _feedbackLabel.color = color == default ? Color.white : color;
        if (!string.IsNullOrEmpty(text))
            _feedbackRoutine = StartCoroutine(ClearFeedback());
    }

    private IEnumerator ClearFeedback()
    {
        yield return new WaitForSecondsRealtime(2f);
        _feedbackLabel.text = "";
        _feedbackRoutine = null;
    }
}
