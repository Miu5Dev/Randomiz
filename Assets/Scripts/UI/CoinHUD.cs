using Randomiz.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Always-on coin counter pinned to the bottom-right corner. Built entirely in code
/// (no prefab wiring), mirroring DialogueUI / ShopUI. Updates are event-driven:
/// it subscribes to <see cref="OnCoinsChangedEvent"/> (raised by InventoryHandler.Coins)
/// and reads the current total once on Start so it's correct even if coins were
/// loaded before this HUD existed.
/// </summary>
public class CoinHUD : MonoBehaviour
{
    private TMP_Text _label;

    private void Awake()
    {
        BuildUI();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnCoinsChangedEvent>(OnCoinsChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnCoinsChangedEvent>(OnCoinsChanged);
    }

    private void Start()
    {
        // Pull the initial value — the InventoryHandler may have loaded coins before
        // this HUD subscribed, in which case we missed the change event.
        int coins = InventoryHandler.Instance != null ? InventoryHandler.Instance.Coins : 0;
        SetCoins(coins);
    }

    private void BuildUI()
    {
        var canvas = UIFactory.CreateCanvas("CoinHUDCanvas");
        canvas.sortingOrder = 50; // above gameplay, below dialogue/pause overlays
        canvas.transform.SetParent(transform, false);

        // Background pill for readability over bright terrain.
        var bg = UIFactory.CreatePanel(canvas.transform, new Color(0f, 0f, 0f, 0.55f), raycastTarget: false);
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = new Vector2(1f, 0f);
        bgRt.anchorMax = new Vector2(1f, 0f);
        bgRt.pivot     = new Vector2(1f, 0f);
        bgRt.anchoredPosition = new Vector2(-24f, 24f);
        bgRt.sizeDelta = new Vector2(220f, 56f);

        _label = UIFactory.CreateLabel(bg.transform, "0", 30,
            new Color(1f, 0.85f, 0.35f), TextAlignmentOptions.Right);
        var lRt = _label.rectTransform;
        lRt.anchorMin = Vector2.zero;
        lRt.anchorMax = Vector2.one;
        lRt.offsetMin = new Vector2(16f, 0f);
        lRt.offsetMax = new Vector2(-16f, 0f);
    }

    private void OnCoinsChanged(OnCoinsChangedEvent e) => SetCoins(e.coins);

    private void SetCoins(int coins)
    {
        if (_label != null) _label.text = $"{coins} coins";
    }
}
