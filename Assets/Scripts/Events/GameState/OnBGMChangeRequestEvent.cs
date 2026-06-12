using UnityEngine;

/// <summary>
/// Raised by any code that needs to override the BGM without a physical MusicZone.
/// Common uses: cutscenes, scripted events, in-game menus.
///
/// To release a previously-requested override, raise again with the same
/// <see cref="requestId"/> and <c>clip = null</c>.
///
/// Priority convention (mirrors MusicZone priorities):
///   cutscene = 30 | boss = 50 | cinematic override = 100
/// </summary>
public class OnBGMChangeRequestEvent
{
    /// <summary>Unique identifier for this request. Use the same id to release it.</summary>
    public string requestId;

    /// <summary>Clip to play. Set to null to release a previous request with this id.</summary>
    public AudioClip clip;

    /// <summary>Wins over any active MusicZone or lower-priority request.</summary>
    public int priority = 50;

    /// <summary>Crossfade duration in seconds.</summary>
    public float fadeTime = 1.5f;
}
