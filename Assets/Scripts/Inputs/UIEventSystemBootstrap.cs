using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Guarantees the scene has exactly one EventSystem driven by the NEW Input System
/// (<see cref="InputSystemUIInputModule"/>), so code-built UI (pause menu, shop,
/// main menu) is clickable and navigable.
///
/// Runs automatically after every scene load — no setup required. Handles two gotchas:
///   • A legacy StandaloneInputModule (throws every frame under the new Input System)
///     is swapped for the correct module.
///   • A module added at runtime has NO UI actions assigned, so clicks never register.
///     <see cref="InputSystemUIInputModule.AssignDefaultActions"/> wires up
///     Point / Click / Navigate / Submit / Cancel.
/// </summary>
public static class UIEventSystemBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Ensure()
    {
        var existing = Object.FindFirstObjectByType<EventSystem>();
        InputSystemUIInputModule module;

        if (existing != null)
        {
            // Remove a legacy module if one slipped in.
            var legacy = existing.GetComponent<StandaloneInputModule>();
            if (legacy != null) Object.Destroy(legacy);

            module = existing.GetComponent<InputSystemUIInputModule>();
            if (module == null)
                module = existing.gameObject.AddComponent<InputSystemUIInputModule>();
        }
        else
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            module = go.AddComponent<InputSystemUIInputModule>();
            Debug.Log("[UIEventSystemBootstrap] Created EventSystem (InputSystemUIInputModule).");
        }

        // A runtime-created module has no actions → clicks/navigation do nothing.
        // Only assign defaults when missing so an editor-configured module is left alone.
        if (module != null && module.point == null)
            module.AssignDefaultActions();
    }
}
