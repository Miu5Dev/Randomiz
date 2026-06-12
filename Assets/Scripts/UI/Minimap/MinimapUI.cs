using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class MinimapUI : MonoBehaviour
{
    [Header("Icons")]
    [SerializeField] private Sprite chestIcon;
    [SerializeField] private Sprite shopIcon;
    [SerializeField] private Sprite playerArrowIcon;

    [Header("Size")]
    [Tooltip("Side length of the minimap panel in pixels. Change at runtime to resize.")]
    [SerializeField] private float mapSizePixels = 150f;
    [SerializeField] private float iconSizePixels = 16f;

    [Header("Chest Colors")]
    [Tooltip("Unopened chests — bright so the player can spot loot still worth visiting.")]
    [SerializeField] private Color closedChestColor = new Color(1f, 0.85f, 0.3f, 1f);
    [Tooltip("Opened chests — dimmed since there's nothing left to grab.")]
    [SerializeField] private Color openChestColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);

    [Header("Shop Colors")]
    [Tooltip("Shop with at least one item still available to buy.")]
    [SerializeField] private Color shopActiveColor = new Color(0.3f, 0.85f, 1f, 1f);
    [Tooltip("Shop where everything has already been purchased.")]
    [SerializeField] private Color shopSoldOutColor = new Color(0.35f, 0.35f, 0.35f, 0.6f);

    [Header("Player")]
    [SerializeField] private Color playerColor = Color.cyan;

    private Canvas minimapCanvas;
    private RectTransform minimapPanel;
    private List<MinimapIcon> chestIconPool = new();
    private List<MinimapIcon> shopIconPool  = new();
    private MinimapIcon playerIcon;
    private Transform playerTransform;
    private Transform _facingTransform;
    private float _lastMapSizePixels;

    private void Start()
    {
        CreateMinimapUI();
        FindPlayer();
        _lastMapSizePixels = mapSizePixels;
    }

    private void Update()
    {
        if (playerTransform == null || minimapPanel == null)
            return;

        if (!Mathf.Approximately(_lastMapSizePixels, mapSizePixels))
        {
            _lastMapSizePixels = mapSizePixels;
            RefreshMapSize();
        }

        UpdateMinimapDisplay();
    }

    /// <summary>
    /// Creates the minimap Canvas and panel if they don't exist.
    /// </summary>
    private void CreateMinimapUI()
    {
        // Check if minimap already exists
        var existingCanvas = FindObjectOfType<Canvas>();
        if (existingCanvas != null && existingCanvas.name == "MinimapCanvas")
        {
            minimapCanvas = existingCanvas;
            minimapPanel = minimapCanvas.GetComponentInChildren<RectTransform>()?.GetComponent<RectTransform>();
            if (minimapPanel != null)
                return;
        }

        // Create Canvas
        var canvasGO = new GameObject("MinimapCanvas");
        minimapCanvas = canvasGO.AddComponent<Canvas>();
        minimapCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Create Panel in bottom-left corner
        var panelGO = new GameObject("MinimapPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        minimapPanel = panelGO.AddComponent<RectTransform>();

        float padding = 10f;
        minimapPanel.anchorMin = Vector2.zero;
        minimapPanel.anchorMax = Vector2.zero;
        minimapPanel.anchoredPosition = new Vector2(padding + mapSizePixels * 0.5f, padding + mapSizePixels * 0.5f);
        minimapPanel.sizeDelta = new Vector2(mapSizePixels, mapSizePixels);

        var bgImage = panelGO.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.5f);

        CreatePlayerIcon();

        Debug.Log("[MinimapUI] Minimap panel created in bottom-left corner.");
    }

    /// <summary>
    /// Grows the chest-icon pool until it can show every discovered chest.
    /// </summary>
    private void EnsureChestIconPool(int needed)
    {
        while (chestIconPool.Count < needed)
            CreateChestIcon();
    }

    /// <summary>
    /// Creates a single chest icon and adds it to the pool.
    /// </summary>
    private MinimapIcon CreateChestIcon()
    {
        var iconGO = new GameObject($"ChestIcon_{chestIconPool.Count}");
        iconGO.transform.SetParent(minimapPanel, false);

        var rectTransform = iconGO.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(iconSizePixels, iconSizePixels);

        var image = iconGO.AddComponent<Image>();
        if (chestIcon != null)
            image.sprite = chestIcon;
        else
            image.color = Color.white;

        var icon = iconGO.AddComponent<MinimapIcon>();
        chestIconPool.Add(icon);
        return icon;
    }

    /// <summary>
    /// Creates the player icon in the center of the minimap.
    /// </summary>
    private void CreatePlayerIcon()
    {
        var iconGO = new GameObject("PlayerIcon");
        iconGO.transform.SetParent(minimapPanel, false);

        var rectTransform = iconGO.AddComponent<RectTransform>();
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = new Vector2(iconSizePixels, iconSizePixels);

        var image = iconGO.AddComponent<Image>();
        if (playerArrowIcon != null)
            image.sprite = playerArrowIcon;
        else
            image.color = playerColor;

        playerIcon = iconGO.AddComponent<MinimapIcon>();
    }

    /// <summary>
    /// Finds the player Transform (assumes a GameObject tagged "Player" or PlayerMovement singleton).
    /// </summary>
    private void FindPlayer()
    {
        var player = GameObject.FindWithTag("Player");
        if (player != null)
            playerTransform = player.transform;
        else if (PlayerMovement.Instance != null)
            playerTransform = PlayerMovement.Instance.transform;
        else
        {
            Debug.LogWarning("[MinimapUI] Could not find player Transform. Minimap will not display.");
            return;
        }

        // Root transform never rotates — use modelTransform for the facing direction.
        _facingTransform = PlayerMovement.Instance != null && PlayerMovement.Instance.modelTransform != null
            ? PlayerMovement.Instance.modelTransform
            : playerTransform;
    }

    /// <summary>
    /// Updates the minimap display each frame based on player position and chest positions.
    /// All chest positions are rotated to match the player's facing direction so the
    /// minimap stays player-oriented (forward = up).
    /// </summary>
    private void UpdateMinimapDisplay()
    {
        var manager = MinimapManager.Instance;
        if (manager == null)
            return;

        var chestEntries = manager.ChestEntries;
        Vector3 playerWorldPos = playerTransform.position;
        float mapRadius = manager.MapRadius;
        float playerAngle = GetPlayerFacingAngle();

        EnsureChestIconPool(chestEntries.Count);

        // Update chest icons — rotate each position so player always faces "up"
        for (int i = 0; i < chestEntries.Count && i < chestIconPool.Count; i++)
        {
            var entry = chestEntries[i];
            Vector3 relativePos = entry.worldPos - playerWorldPos;

            // Counter-rotate the world by the player's angle so forward = up on the minimap
            Vector3 rotatedPos = RotateAroundY(relativePos, -playerAngle);

            Vector2 minimapPos = CalculateMinimapPosition(rotatedPos, mapRadius);
            minimapPos = ClampToBounds(minimapPos);

            Color color = entry.isOpen ? openChestColor : closedChestColor;
            chestIconPool[i].SetState(minimapPos, color, 0f);
            chestIconPool[i].Show();
        }

        // Hide unused chest icons
        for (int i = chestEntries.Count; i < chestIconPool.Count; i++)
            chestIconPool[i].Hide();

        // Update shop icons
        var shopEntries = manager.ShopEntries;
        EnsureShopIconPool(shopEntries.Count);

        for (int i = 0; i < shopEntries.Count && i < shopIconPool.Count; i++)
        {
            var entry = shopEntries[i];
            Vector3 relativePos = entry.worldPos - playerWorldPos;
            Vector3 rotatedPos  = RotateAroundY(relativePos, -playerAngle);
            Vector2 minimapPos  = ClampToBounds(CalculateMinimapPosition(rotatedPos, mapRadius));

            bool hasItems = entry.shop == null || entry.shop.HasAnyUnsoldItems();
            shopIconPool[i].SetState(minimapPos, hasItems ? shopActiveColor : shopSoldOutColor, 0f);
            shopIconPool[i].Show();
        }
        for (int i = shopEntries.Count; i < shopIconPool.Count; i++)
            shopIconPool[i].Hide();

        // Player arrow: always centered, always pointing up (rotation = 0)
        // because the map itself rotates around the player
        if (playerIcon != null)
        {
            playerIcon.SetState(Vector2.zero, playerColor, 0f);
            playerIcon.Show();
        }
    }

    private void RefreshMapSize()
    {
        if (minimapPanel == null) return;
        const float padding = 10f;
        minimapPanel.sizeDelta = new Vector2(mapSizePixels, mapSizePixels);
        minimapPanel.anchoredPosition = new Vector2(padding + mapSizePixels * 0.5f, padding + mapSizePixels * 0.5f);
    }

    private void EnsureShopIconPool(int needed)
    {
        while (shopIconPool.Count < needed)
            CreateShopIcon();
    }

    private MinimapIcon CreateShopIcon()
    {
        var iconGO = new GameObject($"ShopIcon_{shopIconPool.Count}");
        iconGO.transform.SetParent(minimapPanel, false);

        var rt = iconGO.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(iconSizePixels, iconSizePixels);

        var image = iconGO.AddComponent<Image>();
        if (shopIcon != null)
            image.sprite = shopIcon;
        else
            image.color = shopActiveColor;

        var icon = iconGO.AddComponent<MinimapIcon>();
        shopIconPool.Add(icon);
        return icon;
    }

    /// <summary>
    /// Calculates the minimap position from a (already rotated) relative world position.
    /// Formula: minimapPos = relativePos / mapRadius * mapSizePixels * 0.5
    /// </summary>
    private Vector2 CalculateMinimapPosition(Vector3 relativePos, float mapRadius)
    {
        float minimapX = (relativePos.x / mapRadius) * (mapSizePixels * 0.5f);
        float minimapY = (relativePos.z / mapRadius) * (mapSizePixels * 0.5f);
        return new Vector2(minimapX, minimapY);
    }

    /// <summary>
    /// Clamps a minimap position to the circular minimap bounds.
    /// </summary>
    private Vector2 ClampToBounds(Vector2 pos)
    {
        float maxRadius = mapSizePixels * 0.5f;
        if (pos.magnitude > maxRadius)
            pos = pos.normalized * maxRadius;
        return pos;
    }

    /// <summary>
    /// Returns the player's facing angle in degrees.
    /// 0 = facing +Z (world forward), increases clockwise (Unity convention).
    /// </summary>
    private float GetPlayerFacingAngle()
    {
        Transform facing = _facingTransform != null ? _facingTransform : playerTransform;
        if (facing == null) return 0f;
        Vector3 forward = facing.forward;
        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Rotates a vector in the XZ plane around the Y axis by the given degrees.
    /// Positive degrees = clockwise when viewed from above (Unity convention).
    /// </summary>
    private Vector3 RotateAroundY(Vector3 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector3(
            cos * v.x + sin * v.z,
            v.y,
            -sin * v.x + cos * v.z
        );
    }
}
