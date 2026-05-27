using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HUD heart row. Spawns / destroys heart widgets to match maxHearts and updates
/// each one's fill from the player's current health. Each heart has 4 quarters.
/// Fully event-driven via OnHealthChangedEvent.
/// </summary>
public class HeartsDisplay : MonoBehaviour
{
    [SerializeField] private HeartWidget heartPrefab;
    [SerializeField] private Transform container;

    private const int QuartersPerHeart = 4;
    private readonly List<HeartWidget> _hearts = new();

    private void OnEnable()  => EventBus.Subscribe<OnHealthChangedEvent>(OnHealthChanged);
    private void OnDisable() => EventBus.Unsubscribe<OnHealthChangedEvent>(OnHealthChanged);

    private void OnHealthChanged(OnHealthChangedEvent e)
    {
        // Match the heart count to the current max.
        while (_hearts.Count < e.maxHearts)
            _hearts.Add(Instantiate(heartPrefab, container));

        while (_hearts.Count > e.maxHearts)
        {
            Destroy(_hearts[^1].gameObject);
            _hearts.RemoveAt(_hearts.Count - 1);
        }

        // Update each heart's fill amount (0..1, quartered).
        for (int i = 0; i < _hearts.Count; i++)
        {
            float hpForHeart = e.currentHealth - i * QuartersPerHeart;
            _hearts[i].SetFill(hpForHeart / QuartersPerHeart);
        }
    }
}
