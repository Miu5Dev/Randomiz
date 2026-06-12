using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that maps boss IDs to BGM clips.
/// Assign one to <see cref="MusicManager"/> so boss encounters
/// automatically trigger the right music without extra wiring.
/// Create via Assets > Create > Randomiz > BGM Profile.
/// </summary>
[CreateAssetMenu(menuName = "Randomiz/BGM Profile", fileName = "BGMProfile")]
public class SOBGMProfile : ScriptableObject
{
    [System.Serializable]
    public class BossEntry
    {
        [Tooltip("Must match the bossId field in OnBossEncounterStartedEvent.")]
        public string bossId;

        public AudioClip clip;

        [Tooltip("Crossfade in when the encounter starts.")]
        public float fadeTimeIn  = 1.5f;

        [Tooltip("Crossfade out when the encounter ends.")]
        public float fadeTimeOut = 3f;
    }

    [SerializeField] private List<BossEntry> bossEntries = new();

    /// <summary>
    /// Returns the entry whose bossId matches, or null if not found.
    /// </summary>
    public BossEntry GetBossEntry(string bossId)
    {
        for (int i = 0; i < bossEntries.Count; i++)
            if (bossEntries[i].bossId == bossId)
                return bossEntries[i];
        return null;
    }
}
