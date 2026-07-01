using System.Collections;
using Randomiz.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Player death flow. On the player's <see cref="OnDieEvent"/>:
///   1. Freezes player input and lets the death animation play.
///   2. Fades the screen to black.
///   3. Shows "You Died" with two buttons — Respawn / Exit to Main Menu.
///
/// Respawn teleports the player to the last checkpoint (via
/// <see cref="CheckpointManager"/>), revives them with <see cref="respawnHearts"/>
/// hearts, resets the animator out of the death state, and fades back in.
///
/// Built entirely in code via UIFactory. The Setup Wizard adds it automatically.
/// Filters strictly to the player, so enemy deaths never trigger it.
/// </summary>
public class DeathScreenUI : MonoBehaviour
{
    [Header("Timing (seconds, unscaled)")]
    [Tooltip("How long to let the death animation play before fading out.")]
    [SerializeField] private float deathAnimTime = 1.8f;
    [SerializeField] private float fadeTime = 1.0f;

    [Header("Respawn")]
    [Tooltip("Hearts the player respawns with at the last checkpoint.")]
    [SerializeField] private int respawnHearts = 3;
    [SerializeField] private string mainMenuScene = "MainMenu";

    private Canvas        _canvas;
    private CanvasGroup   _fadeGroup;     // black overlay alpha
    private GameObject    _optionsRoot;   // title + buttons (shown after fade)
    private GameObject    _firstSelected; // focus target for gamepad/keyboard
    private bool          _active;

    private void Awake()
    {
        BuildUI();
        _canvas.gameObject.SetActive(false);
    }

    private void OnEnable()  => EventBus.Subscribe<OnDieEvent>(OnDie);
    private void OnDisable() => EventBus.Unsubscribe<OnDieEvent>(OnDie);

    // ─── Death ───────────────────────────────────────────────────────────────

    private void OnDie(OnDieEvent e)
    {
        if (_active || !IsPlayer(e?.murdered)) return;   // enemies never trigger this
        _active = true;
        StartCoroutine(DeathSequence());
    }

    private static bool IsPlayer(GameObject go)
    {
        if (go == null) return false;
        if (PlayerMovement.Instance != null && go == PlayerMovement.Instance.gameObject) return true;
        return go.CompareTag("Player");
    }

    private IEnumerator DeathSequence()
    {
        // Lock the player out while dying (death anim still plays at timeScale 1).
        // Attack is disabled too — without this, mashing the dodge/interact button
        // while dying reaches QuickslotManager.OnInteract, which unequips the sword
        // (movement being disabled zeroes MoveInput, so its "player is standing
        // still" check passes) and the player respawns empty-handed.
        EventBus.Raise(new OnSetMovementEnabledEvent { enabled = false });
        EventBus.Raise(new OnSetAttackEnabledEvent { enabled = false });

        _canvas.gameObject.SetActive(true);
        _optionsRoot.SetActive(false);
        _fadeGroup.alpha = 0f;

        yield return new WaitForSecondsRealtime(deathAnimTime);

        yield return Fade(0f, 1f, fadeTime);

        // Freeze the world behind the black screen and show the options.
        Time.timeScale = 0f;
        ShowCursor(true);
        _optionsRoot.SetActive(true);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_firstSelected);
    }

    // ─── Buttons ───────────────────────────────────────────────────────────────

    private void OnRespawn() => StartCoroutine(RespawnSequence());

    private IEnumerator RespawnSequence()
    {
        _optionsRoot.SetActive(false);
        Time.timeScale = 1f;

        var player = PlayerMovement.Instance;
        if (player != null)
        {
            // Respawn at the point: last checkpoint, else the default spawn point
            // (auto-captured player start or an explicit PlayerSpawnPoint). Only revive
            // in place as a last resort if nothing at all is registered.
            var cm = CheckpointManager.Instance;
            Vector3 spawn = (cm != null && cm.HasSpawnPosition)
                ? cm.GetSpawnPosition()
                : player.transform.position;

            var pc = player.GetComponent<PhysicsController>();
            if (pc != null) pc.SetPosition(spawn);
            else player.transform.position = spawn;

            // Revive with the configured hearts (1 heart = 4 health). SetHealth (not
            // Initialize) so any max-heart upgrades are preserved — we only refill to 3.
            var hp = player.GetComponent<HealthSystem>();
            if (hp != null) hp.SetHealth(Mathf.Max(1, respawnHearts) * 4f);

            // Reset the animator out of the (terminal) death state cleanly.
            var anim = player.GetComponentInChildren<Animator>();
            if (anim != null) { anim.Rebind(); anim.Update(0f); }
        }

        ShowCursor(false);
        EventBus.Raise(new OnSetMovementEnabledEvent { enabled = true });
        EventBus.Raise(new OnSetAttackEnabledEvent { enabled = true });

        yield return Fade(1f, 0f, fadeTime);

        _canvas.gameObject.SetActive(false);
        _active = false;
    }

    private void OnExit()
    {
        Time.timeScale = 1f;
        ShowCursor(true);
        _active = false;
        SceneManager.LoadScene(mainMenuScene);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static void ShowCursor(bool show)
    {
        Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = show;
    }

    private IEnumerator Fade(float from, float to, float dur)
    {
        float t = 0f;
        _fadeGroup.alpha = from;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            _fadeGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
            yield return null;
        }
        _fadeGroup.alpha = to;
    }

    // ─── Construction ────────────────────────────────────────────────────────

    private void BuildUI()
    {
        _canvas = UIFactory.CreateCanvas("DeathScreenCanvas");
        _canvas.sortingOrder = 300; // topmost — above pause / dialogue / shop
        _canvas.transform.SetParent(transform, false);

        // Full-screen black overlay (its CanvasGroup drives the fade).
        var black = UIFactory.CreatePanel(_canvas.transform, Color.black);
        _fadeGroup = black.gameObject.AddComponent<CanvasGroup>();
        _fadeGroup.alpha = 0f;

        // Options layer (title + buttons), shown only after the fade completes.
        _optionsRoot = new GameObject("Options", typeof(RectTransform));
        _optionsRoot.transform.SetParent(_canvas.transform, false);
        UIFactory.StretchToParent(_optionsRoot.GetComponent<RectTransform>());

        var title = UIFactory.CreateLabel(_optionsRoot.transform, "You Died", 64,
            new Color(0.85f, 0.15f, 0.15f));
        var tRt = title.rectTransform;
        tRt.anchorMin = tRt.anchorMax = new Vector2(0.5f, 0.62f);
        tRt.pivot     = new Vector2(0.5f, 0.5f);
        tRt.sizeDelta = new Vector2(820f, 110f);

        var col = UIFactory.CreateLayoutGroup(_optionsRoot.transform, vertical: true, spacing: 16f);
        var cRt = col.GetComponent<RectTransform>();
        cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0.4f);
        cRt.pivot     = new Vector2(0.5f, 0.5f);
        cRt.sizeDelta = new Vector2(340f, 150f);

        var respawnBtn = UIFactory.CreateButton(col.transform, "Respawn", 26, OnRespawn);
        respawnBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 60f);
        StylePrimary(respawnBtn);
        _firstSelected = respawnBtn.gameObject;

        var exitBtn = UIFactory.CreateButton(col.transform, "Exit to Main Menu", 22, OnExit);
        exitBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 60f);

        _optionsRoot.SetActive(false);
    }

    private static void StylePrimary(Button btn)
    {
        btn.GetComponent<Image>().color = new Color(0.45f, 0.12f, 0.12f, 0.95f);
        var cb = btn.colors;
        cb.normalColor      = new Color(0.45f, 0.12f, 0.12f, 0.95f);
        cb.highlightedColor = new Color(0.65f, 0.18f, 0.18f, 1.00f);
        cb.pressedColor     = new Color(0.30f, 0.08f, 0.08f, 1.00f);
        cb.selectedColor    = cb.highlightedColor;
        btn.colors = cb;
    }
}
