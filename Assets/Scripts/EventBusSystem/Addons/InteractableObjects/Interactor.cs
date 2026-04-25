using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Interactor : MonoBehaviour
{
    // The closest valid interactable — always up to date
    public bool        onInteractArea   => interactableScript != null;
    public GameObject  interactObject   { get; private set; }
    public Interactable interactableScript { get; private set; }

    // All interactables currently inside the trigger volume
    private readonly List<Interactable> _candidates = new();

    // ── Interact ─────────────────────────────────────────────────────────────
    public void Interact()
    {
        if (!onInteractArea) return;
        interactableScript.Use();
    }

    // ── Trigger events ────────────────────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        var interactable = other.GetComponent<Interactable>();
        if (interactable != null && !_candidates.Contains(interactable))
            _candidates.Add(interactable);
    }

    private void OnTriggerExit(Collider other)
    {
        var interactable = other.GetComponent<Interactable>();
        if (interactable != null)
            _candidates.Remove(interactable);
    }

    // ── Update: pick the closest valid candidate ──────────────────────────────
    private void Update()
    {
        CleanCandidates();
        PickClosest();
    }

    // Remove destroyed or disabled interactables from the list
    private void CleanCandidates()
    {
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            var c = _candidates[i];
            if (c == null || !c.gameObject.activeInHierarchy)
                _candidates.RemoveAt(i);
        }
    }

    // Select the interactable whose transform is closest to this transform
    private void PickClosest()
    {
        Interactable closest  = null;
        float        bestDist = float.MaxValue;

        foreach (var candidate in _candidates)
        {
            float dist = Vector3.SqrMagnitude(candidate.transform.position - transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                closest  = candidate;
            }
        }

        interactableScript = closest;
        interactObject     = closest != null ? closest.gameObject : null;
    }
}
