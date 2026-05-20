using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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
    
    [Header("Filler items — fallback cuando no hay tier mayor disponible")]
    public List<SOItem> fillerItems = new();

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
        
            // ← AGREGA ESTO: también agregar fillerItems al lookup
            foreach (var filler in fillerItems)
                if (filler != null && !_lookup.ContainsKey(filler.itemName))
                    _lookup[filler.itemName] = filler;
        }
    
        _lookup.TryGetValue(itemName ?? "", out var item);
        return item;
    }
    
    // En SOItemPool.cs, agrega este método:
    private void OnValidate()
    {
        _lookup = null;
    
        // Auto-popular fillerItems con items marcados como filler (opcional)
    #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var autoFiller = items.Where(e => e.item is SOItem si && si.isFiller)
                    .Select(e => e.item)
                    .ToList();
            
                foreach (var f in autoFiller)
                    if (!fillerItems.Contains(f))
                        fillerItems.Add(f);
            }
    #endif
        }
}
