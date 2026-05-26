using System.Collections.Generic;
using UnityEngine;

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
        // Ajusta el número de corazones al máximo actual
        while (_hearts.Count < e.maxHearts)
            _hearts.Add(Instantiate(heartPrefab, container));

        while (_hearts.Count > e.maxHearts)
        {
            Destroy(_hearts[^1].gameObject);
            _hearts.RemoveAt(_hearts.Count - 1);
        }

        // Actualiza el fill de cada corazón
        for (int i = 0; i < _hearts.Count; i++)
        {
            float hpForHeart = e.currentHealth - i * QuartersPerHeart;
            _hearts[i].SetFill(hpForHeart / QuartersPerHeart);
        }
    }
}
