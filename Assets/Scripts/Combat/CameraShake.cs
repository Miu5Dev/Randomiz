using UnityEngine;

/// <summary>
/// Trauma-based camera shake. Add to the Camera GameObject (child of
/// CameraTargetController). Applies a random positional + rotational offset
/// in LateUpdate, layered on top of the parent's follow movement.
///
/// Usage:
///   CameraShake.Instance.AddTrauma(amount);   // 0..1 range
///
/// Trauma decays over time; shake intensity = trauma^2 (so small hits barely
/// move the camera but big hits feel impactful). Two named presets:
///   • HIT_GIVEN  — mini shake (player hit an enemy)
///   • HIT_TAKEN  — strong shake (player was hit)
/// </summary>
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Trauma Decay")]
    [Tooltip("How fast trauma falls off per second (higher = shorter shake).")]
    [SerializeField] private float decaySpeed = 1.8f;

    [Header("Positional Shake")]
    [SerializeField] private float maxPositionOffset = 0.30f;

    [Header("Rotational Shake")]
    [SerializeField] private float maxRollDegrees = 3.5f;

    [Header("Noise Speed")]
    [SerializeField] private float noiseSpeed = 22f;

    [Header("Shake Curve")]
    [Tooltip("Trauma is raised to this power. 2 = punchy (small hits soft), 1.3 = mini hits stay visible.")]
    [SerializeField] private float traumaExponent = 1.4f;

    [Header("Presets")]
    [Tooltip("Trauma added when the player lands a hit on an enemy (mini shake).")]
    [SerializeField] private float traumaOnHitGiven = 0.40f;
    [Tooltip("Trauma added when the player receives a hit (strong shake).")]
    [SerializeField] private float traumaOnHitTaken = 0.80f;

    private float   _trauma;
    private float   _seed;

    // The offset applied last frame — undone at the start of each LateUpdate so
    // whatever follow/orbit system moves the camera isn't interfered with.
    private Vector3 _appliedOffset;
    private float   _appliedRoll;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _seed = Random.Range(0f, 100f);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()
    {
        EventBus.Subscribe<OnDamagedEvent>(OnDamaged);
        EventBus.Subscribe<OnDamageDealtEvent>(OnDamageDealt);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnDamagedEvent>(OnDamaged);
        EventBus.Unsubscribe<OnDamageDealtEvent>(OnDamageDealt);
    }

    // Player was hit → strong shake.
    private void OnDamaged(OnDamagedEvent e)
    {
        if (IsPlayer(e.victim)) AddTrauma(traumaOnHitTaken);
    }

    // Player hit an enemy → mini shake.
    private void OnDamageDealt(OnDamageDealtEvent e)
    {
        // Only when the PLAYER is the attacker and the target is NOT the player.
        if (IsPlayer(e.Attacker) && !IsPlayer(e.Target)) AddTrauma(traumaOnHitGiven);
    }

    // Compares by root so it works even if the weapon / EquipHandler / HealthSystem
    // live on different GameObjects than PlayerMovement within the player hierarchy.
    private static bool IsPlayer(GameObject go)
    {
        return go != null
            && PlayerMovement.Instance != null
            && go.transform.root == PlayerMovement.Instance.transform.root;
    }

    public void AddTrauma(float amount) =>
        _trauma = Mathf.Clamp01(_trauma + amount);

    private void LateUpdate()
    {
        // 1. Undo last frame's shake so the follow system's position is clean.
        transform.localPosition -= _appliedOffset;
        Vector3 euler = transform.localEulerAngles;
        euler.z -= _appliedRoll;
        transform.localEulerAngles = euler;

        if (_trauma <= 0f)
        {
            _appliedOffset = Vector3.zero;
            _appliedRoll   = 0f;
            return;
        }

        // 2. Decay and compute new shake.
        _trauma = Mathf.Max(0f, _trauma - decaySpeed * Time.deltaTime);
        float shake = Mathf.Pow(_trauma, traumaExponent);   // <2 keeps mini-hits visible
        float t     = Time.time * noiseSpeed;

        _appliedOffset.x = (Mathf.PerlinNoise(_seed + t,        0f) * 2f - 1f) * maxPositionOffset * shake;
        _appliedOffset.y = (Mathf.PerlinNoise(_seed + t + 100f, 0f) * 2f - 1f) * maxPositionOffset * shake;
        _appliedOffset.z = 0f;
        _appliedRoll     = (Mathf.PerlinNoise(_seed + t + 200f, 0f) * 2f - 1f) * maxRollDegrees    * shake;

        // 3. Apply on top of wherever the follow system placed the camera.
        transform.localPosition += _appliedOffset;
        euler    = transform.localEulerAngles;
        euler.z += _appliedRoll;
        transform.localEulerAngles = euler;
    }
}
