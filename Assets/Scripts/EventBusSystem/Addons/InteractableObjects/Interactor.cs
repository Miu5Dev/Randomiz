using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Detects nearby Interactable objects via trigger volume and routes the interact
/// button to the closest one. Self-subscribes to OnInteractDodgeInputEvent with
/// high priority — when an interactable is in range, the press is consumed
/// (event cancelled) so dash/wallhug/unequip don't also fire.
///
/// Singleton: exposes a static Instance so HUD/UI can locate it without
/// resorting to FindObjectByType.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Interactor : MonoBehaviour
{
    public static Interactor Instance { get; private set; }

    // Closest valid interactable — refreshed every Update.
    public bool         onInteractArea     => interactableScript != null;
    public GameObject   interactObject     { get; private set; }
    public Interactable interactableScript { get; private set; }

    // All interactables currently inside the trigger volume.
    private readonly List<Interactable> _candidates = new();

    // Cached event + last-emitted state for proximity transitions.
    private readonly OnInteractableProximityChangedEvent _proxEvt = new();
    private bool _lastEmittedNearby;
    private bool _hasEmittedProximity;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        // Priority 10 — must run before PlayerMovement (wallhug/dash) and
        // QuickslotManager (unequip) so we can cancel the event when consuming it.
        EventBus.Subscribe<OnInteractDodgeInputEvent>(OnInteractInput, 10);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnInteractDodgeInputEvent>(OnInteractInput);
    }

    private void OnInteractInput(OnInteractDodgeInputEvent e)
    {
        if (!e.pressed) return;
        if (!onInteractArea) return;
        Interact();
        // Consume the event — no dash / wallhug / unequip on this press.
        EventBus.Cancel<OnInteractDodgeInputEvent>();
    }

    /// <summary>Trigger the closest interactable's Use(). Safe no-op if none.</summary>
    public void Interact()
    {
        if (!onInteractArea) return;
        interactableScript.Use(gameObject);
    }

    // ── Trigger volume tracking ──────────────────────────────────────────
    // TryGetComponent avoids the small managed allocation that GetComponent does
    // when the component is absent, and shortcuts the lookup cleanly.

    private void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent<Interactable>(out var interactable)) return;
        if (!_candidates.Contains(interactable))
            _candidates.Add(interactable);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.TryGetComponent<Interactable>(out var interactable)) return;
        _candidates.Remove(interactable);
    }

    // ── Per-frame pick ───────────────────────────────────────────────────

    private void Update()
    {
        CleanCandidates();
        PickClosest();
    }

    private void CleanCandidates()
    {
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            var c = _candidates[i];
            if (c == null || !c.gameObject.activeInHierarchy)
                _candidates.RemoveAt(i);
        }
    }

    private void PickClosest()
    {
        Interactable closest = null;
        float bestDist = float.MaxValue;
        Vector3 selfPos = transform.position;

        for (int i = 0; i < _candidates.Count; i++)
        {
            var candidate = _candidates[i];
            // SqrMagnitude avoids the sqrt of Vector3.Distance and is fine for ordering.
            float dist = (candidate.transform.position - selfPos).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                closest = candidate;
            }
        }

        interactableScript = closest;
        interactObject     = closest != null ? closest.gameObject : null;

        // Emit proximity transition only when the boolean state actually flips.
        bool nearby = closest != null;
        if (!_hasEmittedProximity || _lastEmittedNearby != nearby)
        {
            _lastEmittedNearby   = nearby;
            _hasEmittedProximity = true;
            _proxEvt.nearby      = nearby;
            EventBus.Raise(_proxEvt);
        }
    }
}
