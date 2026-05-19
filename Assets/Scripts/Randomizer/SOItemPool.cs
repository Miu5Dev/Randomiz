using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ItemPool", menuName = "Randomizer/ItemPool")]
public class SOItemPool : ScriptableObject
{
    [System.Serializable]
    public class ItemEntry
    {
        public SOItem item;
        [Range(1, 10)] public int priority = 5;
        [Tooltip("Desbloquea nuevas zonas (armas de progresión, llaves...)")]
        public bool isProgression;
        [Tooltip("Sin este ítem el juego no se puede terminar")]
        public bool isRequired;
    }

    [Header("Items de la run")]
    public List<ItemEntry> items = new();

    [Header("Estado global de la run")]
    public RandomizerState state;

    // ─── Lookup por nombre para resolver desde el state ───
    private Dictionary<string, SOItem> _lookup;

    public SOItem FindItem(string itemName)
    {
        if (_lookup == null)
        {
            _lookup = new();
            foreach (var e in items)
                if (e.item != null && !_lookup.ContainsKey(e.item.itemName))
                    _lookup[e.item.itemName] = e.item;
        }
        _lookup.TryGetValue(itemName ?? "", out var item);
        return item;
    }

    // Limpia el lookup al cambiar items en el inspector
    private void OnValidate() => _lookup = null;
}
