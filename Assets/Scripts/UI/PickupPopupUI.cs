using System.Collections;
using System.Collections.Generic;
using Randomiz.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Zelda-style "You got X!" popup. Subscribes to <see cref="OnItemPickedUpEvent"/>
/// (raised by chests / pickups) and shows a small card — item icon + name — that
/// pops in (ease-out-back scale + fade), holds, then fades and drifts up.
///
/// Multiple pickups queue so they never overlap. Built entirely in code via
/// UIFactory; never blocks gameplay raycasts. Place one in the scene (the Setup
/// Wizard adds it automatically).
/// </summary>
public class PickupPopupUI : MonoBehaviour
{
    [Header("Timing (seconds, unscaled)")]
    [SerializeField] private float introTime = 0.3f;
    [SerializeField] private float holdTime  = 1.6f;
    [SerializeField] private float outroTime = 0.4f;

    private Canvas        _canvas;
    private CanvasGroup   _group;
    private RectTransform _card;
    private Image         _icon;
    private TMP_Text      _label;

    private readonly Queue<SOItem> _queue = new();
    private Coroutine _running;

    private void Awake()
    {
        BuildUI();
        _group.alpha = 0f;
        _canvas.gameObject.SetActive(false);
    }

    private void OnEnable()  => EventBus.Subscribe<OnItemPickedUpEvent>(OnPickup);
    private void OnDisable() => EventBus.Unsubscribe<OnItemPickedUpEvent>(OnPickup);

    private void OnPickup(OnItemPickedUpEvent e)
    {
        if (e?.item == null) return;
        _queue.Enqueue(e.item);
        if (_running == null) _running = StartCoroutine(ProcessQueue());
    }

    // ─── Animation ───────────────────────────────────────────────────────────

    private IEnumerator ProcessQueue()
    {
        _canvas.gameObject.SetActive(true);
        while (_queue.Count > 0)
            yield return ShowOne(_queue.Dequeue());
        _canvas.gameObject.SetActive(false);
        _running = null;
    }

    private IEnumerator ShowOne(SOItem item)
    {
        _label.text = $"You got {item.itemName}!";
        if (item.itemSprite != null)
        {
            _icon.sprite  = item.itemSprite;
            _icon.enabled = true;
        }
        else _icon.enabled = false;

        // Intro — scale pop + fade + slight rise.
        float t = 0f;
        while (t < introTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / introTime);
            _group.alpha = k;
            _card.localScale = Vector3.one * EaseOutBack(k);
            _card.anchoredPosition = new Vector2(0f, Mathf.Lerp(-20f, 0f, k));
            yield return null;
        }
        _group.alpha = 1f;
        _card.localScale = Vector3.one;
        _card.anchoredPosition = Vector2.zero;

        yield return new WaitForSecondsRealtime(holdTime);

        // Outro — fade + drift up.
        t = 0f;
        while (t < outroTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / outroTime);
            _group.alpha = 1f - k;
            _card.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, 30f, k));
            yield return null;
        }
        _group.alpha = 0f;
    }

    // Overshoot easing for a lively "pop".
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float xm = x - 1f;
        return 1f + c3 * xm * xm * xm + c1 * xm * xm;
    }

    // ─── Construction ────────────────────────────────────────────────────────

    private void BuildUI()
    {
        _canvas = UIFactory.CreateCanvas("PickupPopupCanvas");
        _canvas.sortingOrder = 95; // above HUD, below menus (pause 100 / dialogue 200)
        _canvas.transform.SetParent(transform, false);

        var cardGo = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup));
        cardGo.transform.SetParent(_canvas.transform, false);
        _card = cardGo.GetComponent<RectTransform>();
        _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.62f);
        _card.pivot     = new Vector2(0.5f, 0.5f);
        _card.sizeDelta = new Vector2(360f, 200f);

        _group = cardGo.GetComponent<CanvasGroup>();
        _group.blocksRaycasts = false; // never eat gameplay/menu clicks
        _group.interactable   = false;

        // Background.
        var bg = UIFactory.CreatePanel(_card, new Color(0.06f, 0.06f, 0.09f, 0.92f));
        UIFactory.StretchToParent(bg.rectTransform);

        // Icon (top-center).
        _icon = UIFactory.CreateImage(_card, null);
        var iRt = _icon.rectTransform;
        iRt.anchorMin = iRt.anchorMax = new Vector2(0.5f, 1f);
        iRt.pivot     = new Vector2(0.5f, 1f);
        iRt.anchoredPosition = new Vector2(0f, -24f);
        iRt.sizeDelta = new Vector2(96f, 96f);
        _icon.preserveAspect = true;

        // Label (bottom).
        _label = UIFactory.CreateLabel(_card, "", 26, new Color(1f, 0.92f, 0.6f));
        var lRt = _label.rectTransform;
        lRt.anchorMin = new Vector2(0.05f, 0.02f);
        lRt.anchorMax = new Vector2(0.95f, 0.38f);
        lRt.offsetMin = Vector2.zero;
        lRt.offsetMax = Vector2.zero;
        _label.alignment = TextAlignmentOptions.Center;
        _label.enableWordWrapping = true;
    }
}
