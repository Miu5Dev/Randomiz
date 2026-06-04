using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates a multi-part boss. Lives on the boss root GameObject; each child
/// <see cref="EnemyController"/> registers itself by part name on Start. Phase
/// conditions (e.g. <see cref="SOCondition_PartDead"/>) query this to react to
/// other parts dying — letting a hidden body "wake up" when the head is killed.
/// </summary>
public class BossGroup : MonoBehaviour
{
    private readonly Dictionary<string, EnemyController> _parts = new();

    public void Register(string partName, EnemyController controller)
    {
        if (string.IsNullOrEmpty(partName) || controller == null) return;
        _parts[partName] = controller;
    }

    public void Unregister(string partName)
    {
        if (!string.IsNullOrEmpty(partName)) _parts.Remove(partName);
    }

    /// <summary>True if the named part exists and is still alive.</summary>
    public bool IsPartAlive(string partName)
    {
        return _parts.TryGetValue(partName, out var c) && c != null && c.IsAlive;
    }

    /// <summary>True if the named part is registered (alive or dead).</summary>
    public bool HasPart(string partName) => _parts.ContainsKey(partName);

    public EnemyController GetPart(string partName) =>
        _parts.TryGetValue(partName, out var c) ? c : null;

    /// <summary>True when every registered part is dead — fire boss-defeat logic here.</summary>
    public bool AllPartsDead()
    {
        foreach (var c in _parts.Values)
            if (c != null && c.IsAlive) return false;
        return _parts.Count > 0;
    }
}
