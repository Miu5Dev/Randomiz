using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Creates and manages the minimap UI panel in the bottom-left corner.
///
/// Responsibilities:
/// - Creates Canvas and minimap panel on Start (if not already present).
/// - Pools MinimapIcon components for chests and the player.
/// - Each Update: reads chest positions from MinimapManager and repositions icons.
/// - Calculates minimap positions using the formula:
///   minimapPos = (chestWorldPos - playerWorldPos) / mapRadius * mapSizePixels * 0.5
/// - Clamps positions to minimap bounds.
/// - Colors: white for closed chests, gold (1, 0.8, 0) for opened.
/// - Player dot always centered with arrow showing facing direction.
/// </summary>
public class MinimapUI : MonoBehaviour
{
    [SerializeField] private Sprite chestIcon;
    [SerializeField] private Sprite playerArrowIcon;
    [SerializeField] private float mapSizePixels = 150f;
    [SerializeField] private float iconSizePixels = 16f;
    [SerializeField] private Color closedChestColor = Color.white;
    [SerializeField] private Color openChestColor = new Color(1f, 0.8f, 0f, 1f);
    [SerializeField] private Color playerColor = Color.cyan;

    private Canvas minimapCanvas;
    private RectTransform minimapPanel;
    private List<MinimapIcon> chestIconPool = new();
    private MinimapIcon playerIcon;
    private Transform playerTransform;

    private void Start()
    {
        CreateMinimapUI();
        FindPlayer();
    }

    private void Update()
    {
        if (playerTransform == null || minimapPanel == null)
            return;

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
            minimapPanel = minimapCanvas.GetComponentInChildren<RawImage>()?.GetComponent<RectTransform>();
            if (minimapPanel != null)
                return;
        }

        // Create Canvas
        var canvasGO = new GameObject("MinimapCanvas");
        minimapCanvas = canvasGO.AddComponent<Canvas>();
        minimapCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Create Panel (RawImage in bottom-left corner)
        var panelGO = new GameObject("MinimapPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        minimapPanel = panelGO.AddComponent<RectTransform>();

        // Position: bottom-left corner with padding
        float padding = 10f;
        minimapPanel.anchorMin = Vector2.zero;
        minimapPanel.anchorMax = Vector2.zero;
        minimapPanel.anchoredPosition = new Vector2(padding + mapSizePixels * 0.5f, padding + mapSizePixels * 0.5f);
        minimapPanel.sizeDelta = new Vector2(mapSizePixels, mapSizePixels);

        // Add Image component for background
        var bgImage = panelGO.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.5f);

        // Create icon pool for chests
        InitializeChestIconPool();

        // Create player icon
        CreatePlayerIcon();

        Debug.Log("[MinimapUI] Minimap panel created in bottom-left corner.");
    }

    /// <summary>
    /// Initializes the chest icon pool based on discovered chests.
    /// </summary>
    private void InitializeChestIconPool()
    {
        var manager = MinimapManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[MinimapUI] MinimapManager not found in scene.");
            return;
        }

        int chestCount = manager.ChestEntries.Count;
        for (int i = 0; i < chestCount; i++)
        {
            CreateChestIcon();
        }
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
            image.color = Color.white; // Fallback: white circle

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
    /// Finds the player Transform (assumes a GameObject with a CharacterMovement or Player tag).
    /// </summary>
    private void FindPlayer()
    {
        // Try common player tags/names
        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
            return;
        }

        // Fallback: look for PlayerMovement singleton
        if (PlayerMovement.Instance != null)
        {
            playerTransform = PlayerMovement.Instance.transform;
            return;
        }

        Debug.LogWarning("[MinimapUI] Could not find player Transform. Minimap will not display.");
    }

    /// <summary>
    /// Updates the minimap display each frame based on player position and chest positions.
    /// </summary>
    private void UpdateMinimapDisplay()
    {
        var manager = MinimapManager.Instance;
        if (manager == null)
            return;

        var chestEntries = manager.ChestEntries;
        Vector3 playerWorldPos = playerTransform.position;
        float mapRadius = manager.MapRadius;

        // Update chest icons
        for (int i = 0; i < chestEntries.Count && i < chestIconPool.Count; i++)
        {
            var entry = chestEntries[i];
            Vector3 relativePos = entry.worldPos - playerWorldPos;

            // Calculate minimap position
            Vector2 minimapPos = CalculateMinimapPosition(relativePos, mapRadius);

            // Clamp to minimap bounds
            minimapPos = ClampToBounds(minimapPos);

            // Set color based on open state
            Color color = entry.isOpen ? openChestColor : closedChestColor;

            // Update icon
            chestIconPool[i].SetState(minimapPos, color, 0f);
            chestIconPool[i].Show();
        }

        // Hide unused icons
        for (int i = chestEntries.Count; i < chestIconPool.Count; i++)
        {
            chestIconPool[i].Hide();
        }

        // Update player icon (always centered)
        if (playerIcon != null)
        {
            float playerRotation = GetPlayerFacingDirection();
            playerIcon.SetState(Vector2.zero, playerColor, playerRotation);
            playerIcon.Show();
        }
    }

    /// <summary>
    /// Calculates the position on the minimap given a relative world position.
    /// Formula: minimapPos = relativePos / mapRadius * mapSizePixels * 0.5
    /// </summary>
    private Vector2 CalculateMinimapPosition(Vector3 relativePos, float mapRadius)
    {
        // Use only X and Z (horizontal plane)
        float relX = relativePos.x;
        float relZ = relativePos.z;

        // Convert to minimap coordinates
        float minimapX = (relX / mapRadius) * (mapSizePixels * 0.5f);
        float minimapY = (relZ / mapRadius) * (mapSizePixels * 0.5f);

        return new Vector2(minimapX, minimapY);
    }

    /// <summary>
    /// Clamps a minimap position to the minimap's circular bounds.
    /// </summary>
    private Vector2 ClampToBounds(Vector2 pos)
    {
        float maxRadius = mapSizePixels * 0.5f;
        if (pos.magnitude > maxRadius)
        {
            pos = pos.normalized * maxRadius;
        }
        return pos;
    }

    /// <summary>
    /// Gets the player's facing direction in degrees (0 = up/north).
    /// </summary>
    private float GetPlayerFacingDirection()
    {
        if (playerTransform == null)
            return 0f;

        // Use forward direction of player
        Vector3 forward = playerTransform.forward;
        float angle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        return angle;
    }
}
