using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that manages background music with crossfade.
/// Three sources of track requests, resolved by priority (highest wins):
///
///   1. <see cref="MusicZone"/> physical triggers  (priority set per zone, e.g. 0–20)
///   2. Boss encounters via <see cref="OnBossEncounterStartedEvent"/>  (priority 50)
///   3. Programmatic requests via <see cref="OnBGMChangeRequestEvent"/> (caller-defined)
///
/// When the highest-priority request is released, the manager automatically
/// falls back to the next active one, or to the default track.
/// </summary>
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────────
    [Header("Default track")]
    [Tooltip("Plays when no MusicZone or programmatic request is active.")]
    [SerializeField] private AudioClip defaultTrack;
    [SerializeField] private float defaultFadeTime = 2f;

    [Header("Boss music")]
    [Tooltip("Optional — maps bossId strings to BGM clips. " +
             "If assigned, boss encounters are handled automatically.")]
    [SerializeField] private SOBGMProfile bossProfile;

    [Header("Volume")]
    [Range(0f, 1f)]
    [SerializeField] private float masterVolume = 0.8f;

    // ── Private state ──────────────────────────────────────────────────────────
    private AudioSource _sourceA;
    private AudioSource _sourceB;
    private AudioSource _current;   // fades up to masterVolume
    private AudioSource _previous;  // fades out

    private Coroutine _crossfadeCoroutine;
    private AudioClip _playingClip;

    // Physical zone stack.
    private readonly List<MusicZone> _activeZones = new();

    // Programmatic request stack (boss events + OnBGMChangeRequestEvent).
    private struct ProgrammaticRequest
    {
        public string    requestId;
        public AudioClip clip;
        public int       priority;
        public float     fadeTime;
    }
    private readonly List<ProgrammaticRequest> _requests = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Scene loaded with a new MusicManager — absorb its config so the
            // persisted singleton picks up the new scene's default track / boss profile.
            Instance.AbsorbConfig(this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _sourceA  = CreateSource();
        _sourceB  = CreateSource();
        _current  = _sourceA;
        _previous = _sourceB;
    }

    private void Start()
    {
        if (defaultTrack != null)
            CrossfadeTo(defaultTrack, defaultFadeTime);
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnBossEncounterStartedEvent>(OnBossStarted);
        EventBus.Subscribe<OnBossEncounterEndedEvent>(OnBossEnded);
        EventBus.Subscribe<OnBGMChangeRequestEvent>(OnBGMChangeRequest);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnBossEncounterStartedEvent>(OnBossStarted);
        EventBus.Unsubscribe<OnBossEncounterEndedEvent>(OnBossEnded);
        EventBus.Unsubscribe<OnBGMChangeRequestEvent>(OnBGMChangeRequest);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Zone registration (called by MusicZone) ────────────────────────────────

    public void RegisterZone(MusicZone zone)
    {
        if (!_activeZones.Contains(zone))
            _activeZones.Add(zone);
        RefreshTrack();
    }

    public void UnregisterZone(MusicZone zone)
    {
        _activeZones.Remove(zone);
        RefreshTrack();
    }

    // ── EventBus handlers ──────────────────────────────────────────────────────

    private void OnBossStarted(OnBossEncounterStartedEvent e)
    {
        if (bossProfile == null) return;

        SOBGMProfile.BossEntry entry = bossProfile.GetBossEntry(e.bossId);
        if (entry == null || entry.clip == null) return;

        PushRequest(e.bossId, entry.clip, priority: 50, entry.fadeTimeIn);
    }

    private void OnBossEnded(OnBossEncounterEndedEvent e)
    {
        // Find the boss fade-out time from the profile if available.
        float fadeOut = defaultFadeTime;
        if (bossProfile != null)
        {
            SOBGMProfile.BossEntry entry = bossProfile.GetBossEntry(e.bossId);
            if (entry != null) fadeOut = entry.fadeTimeOut;
        }

        ReleaseRequest(e.bossId, fadeOut);
    }

    private void OnBGMChangeRequest(OnBGMChangeRequestEvent e)
    {
        if (e.clip == null)
            ReleaseRequest(e.requestId, e.fadeTime);
        else
            PushRequest(e.requestId, e.clip, e.priority, e.fadeTime);
    }

    // ── Volume ─────────────────────────────────────────────────────────────────

    /// <summary>Adjusts master volume and applies it immediately to the playing source.</summary>
    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        if (_current != null && _current.isPlaying)
            _current.volume = masterVolume;
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    private void PushRequest(string requestId, AudioClip clip, int priority, float fadeTime)
    {
        // Replace existing entry with the same id rather than duplicating.
        for (int i = 0; i < _requests.Count; i++)
        {
            if (_requests[i].requestId == requestId)
            {
                _requests[i] = new ProgrammaticRequest
                    { requestId = requestId, clip = clip, priority = priority, fadeTime = fadeTime };
                RefreshTrack();
                return;
            }
        }

        _requests.Add(new ProgrammaticRequest
            { requestId = requestId, clip = clip, priority = priority, fadeTime = fadeTime });
        RefreshTrack();
    }

    private void ReleaseRequest(string requestId, float fadeTime)
    {
        for (int i = _requests.Count - 1; i >= 0; i--)
        {
            if (_requests[i].requestId == requestId)
            {
                _requests.RemoveAt(i);
                break;
            }
        }

        // RefreshTrack picks the new top; pass the fade from the released request
        // so the transition out feels intentional.
        RefreshTrackWithFade(fadeTime);
    }

    private void RefreshTrack() => RefreshTrackWithFade(-1f);

    private void RefreshTrackWithFade(float overrideFade)
    {
        (AudioClip topClip, float topFade) = GetTopTrack();

        float fade = overrideFade >= 0f ? overrideFade : topFade;

        if (topClip == _playingClip) return;
        CrossfadeTo(topClip, fade);
    }

    /// <summary>
    /// Returns the clip and fade time of the highest-priority active source.
    /// Priority: programmatic requests first, then zones, then default.
    /// </summary>
    private (AudioClip clip, float fade) GetTopTrack()
    {
        AudioClip topClip  = null;
        float     topFade  = defaultFadeTime;
        int       topPrio  = int.MinValue;

        // Programmatic requests.
        for (int i = 0; i < _requests.Count; i++)
        {
            if (_requests[i].priority > topPrio)
            {
                topPrio = _requests[i].priority;
                topClip = _requests[i].clip;
                topFade = _requests[i].fadeTime;
            }
        }

        // Physical zones.
        for (int i = 0; i < _activeZones.Count; i++)
        {
            if (_activeZones[i].Priority > topPrio)
            {
                topPrio = _activeZones[i].Priority;
                topClip = _activeZones[i].Clip;
                topFade = _activeZones[i].FadeTime;
            }
        }

        // Fall back to default.
        if (topClip == null)
        {
            topClip = defaultTrack;
            topFade = defaultFadeTime;
        }

        return (topClip, topFade);
    }

    private void CrossfadeTo(AudioClip clip, float fadeTime)
    {
        _playingClip = clip;

        if (_crossfadeCoroutine != null)
            StopCoroutine(_crossfadeCoroutine);

        _crossfadeCoroutine = StartCoroutine(CrossfadeRoutine(clip, fadeTime));
    }

    private IEnumerator CrossfadeRoutine(AudioClip clip, float fadeTime)
    {
        (_current, _previous) = (_previous, _current);

        if (clip != null)
        {
            _current.clip   = clip;
            _current.loop   = true;
            _current.volume = 0f;
            _current.Play();
        }

        float elapsed     = 0f;
        float duration    = Mathf.Max(fadeTime, 0.01f);
        float startVolume = _previous.volume;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            _previous.volume = Mathf.Lerp(startVolume, 0f, t);
            if (clip != null)
                _current.volume = Mathf.Lerp(0f, masterVolume, t);

            yield return null;
        }

        _previous.volume = 0f;
        _previous.Stop();
        _previous.clip = null;

        if (clip != null)
            _current.volume = masterVolume;

        _crossfadeCoroutine = null;
    }

    private AudioSource CreateSource()
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake  = false;
        src.spatialBlend = 0f;
        src.volume       = 0f;
        return src;
    }

    // Called when a scene loads a second MusicManager — takes the new scene's
    // config so the persisted singleton plays the correct default track.
    private void AbsorbConfig(MusicManager incoming)
    {
        if (incoming.defaultTrack != null)
        {
            defaultTrack    = incoming.defaultTrack;
            defaultFadeTime = incoming.defaultFadeTime;
        }
        if (incoming.bossProfile != null)
            bossProfile = incoming.bossProfile;

        masterVolume = incoming.masterVolume;
        if (_current != null && _current.isPlaying)
            _current.volume = masterVolume;

        // Drop any leftover zone/request state from the previous scene and
        // let the new default track take over.
        _activeZones.Clear();
        _requests.Clear();
        RefreshTrack();
    }
}
