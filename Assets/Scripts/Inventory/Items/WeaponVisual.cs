using UnityEngine;

/// Permanent child of ItemsPivotPoint that shows/hides its renderers
/// when the matching SOItem is equipped or unequipped.
///
/// Edit-mode behaviour: renderers are ALWAYS enabled so you can select
/// the "Model" child, then move/rotate/scale it freely in the Scene view.
/// Colliders and Rigidbodies anywhere in the hierarchy are destroyed at
/// runtime so the visual never interferes with player movement.
public class WeaponVisual : MonoBehaviour
{
    [Tooltip("The SOItem this visual represents.")]
    public SOItem itemAsset;

    private Renderer[] _renderers;

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);

        // Weapon visuals must never push or block the player.
        foreach (var col in GetComponentsInChildren<Collider>(true))
            Destroy(col);
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
            Destroy(rb);
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnItemEquipEvent>(OnEquip);
        EventBus.Subscribe<OnItemUnequipEvent>(OnUnequip);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnItemEquipEvent>(OnEquip);
        EventBus.Unsubscribe<OnItemUnequipEvent>(OnUnequip);
    }

    private void Start()
    {
        bool isEquipped = EquipHandler.Instance != null && EquipHandler.Instance.EquipedItem == itemAsset;
        SetVisible(isEquipped);
    }

    private void OnEquip(OnItemEquipEvent e) => SetVisible(e.item == itemAsset);

    private void OnUnequip(OnItemUnequipEvent e)
    {
        if (e.item == itemAsset) SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers)
            if (r != null) r.enabled = visible;
    }

#if UNITY_EDITOR
    // OnValidate fires in Edit mode whenever the Inspector changes (or the scene loads).
    // This keeps all renderers visible outside of Play mode so the weapon can be positioned.
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers)
            if (r != null) r.enabled = true;
    }
#endif
}
