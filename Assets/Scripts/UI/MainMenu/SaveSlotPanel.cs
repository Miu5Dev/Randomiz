using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Randomiz.UI;

// SaveManager.Instance.GetAllSlots() returns SaveSlotInfo[]
// SaveSlotInfo fields used: exists, seed, Timestamp (DateTime), coins

namespace Randomiz.UI
{
    /// <summary>
    /// Panel that lists all save slots and lets the player Continue, start a
    /// New Game (with overwrite confirmation) or Delete a slot.
    /// Built entirely in code — no prefabs needed.
    /// </summary>
    public class SaveSlotPanel : MonoBehaviour
    {
        // ── Scene constant ────────────────────────────────────────────────────
        private const string GAME_SCENE = "Game";

        // ── Runtime references ────────────────────────────────────────────────
        private Canvas     _canvas;
        private GameObject _rootPanel;
        private GameObject _confirmDialog;

        // Slot row references for refreshing labels
        private readonly List<SlotRowRefs> _rows = new List<SlotRowRefs>(3);

        // Pending action waiting for confirmation
        private Action _pendingAction;

        // ── Slot row helper ───────────────────────────────────────────────────
        private struct SlotRowRefs
        {
            public TMP_Text InfoLabel;
            public Button   PrimaryButton;   // "Continue" or "New Game"
            public TMP_Text PrimaryBtnLabel;
            public Button   DeleteButton;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            BuildUI();
        }

        private void OnEnable()
        {
            RefreshSlots();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            // Canvas — sorted above main menu (sortingOrder 1)
            _canvas = UIFactory.CreateCanvas("SaveSlotCanvas");
            _canvas.sortingOrder = 1;
            _canvas.gameObject.transform.SetParent(transform, false);

            // Dark overlay
            var overlay = UIFactory.CreatePanel(_canvas.transform, new Color(0f, 0f, 0f, 0.85f));
            UIFactory.StretchToParent(overlay.GetComponent<RectTransform>());

            // Centred card
            var card = UIFactory.CreatePanel(overlay.transform, new Color(0.10f, 0.10f, 0.12f, 1f));
            UIFactory.CenterWithSize(card.GetComponent<RectTransform>(), new Vector2(700f, 580f));
            _rootPanel = card.gameObject;

            // Title
            var title = UIFactory.CreateLabel(card.transform, "Select Save Slot", 32, Color.white);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot     = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(0f, -70f);
            titleRt.offsetMax = new Vector2(0f, 0f);

            // Slot rows container
            var listGo = UIFactory.CreateLayoutGroup(card.transform, vertical: true, spacing: 14f,
                padding: new RectOffset(24, 24, 80, 80));
            var listRt = listGo.GetComponent<RectTransform>();
            UIFactory.StretchToParent(listRt);

            for (int i = 0; i < 3; i++)
            {
                var row = BuildSlotRow(listGo.transform, i);
                _rows.Add(row);
            }

            // Back button
            var backBtn = UIFactory.CreateButton(card.transform, "Back", 20, OnBackClicked);
            var backRt  = backBtn.GetComponent<RectTransform>();
            backRt.anchorMin        = new Vector2(0f, 0f);
            backRt.anchorMax        = new Vector2(0f, 0f);
            backRt.pivot            = new Vector2(0f, 0f);
            backRt.anchoredPosition = new Vector2(20f, 16f);
            backRt.sizeDelta        = new Vector2(120f, 45f);

            // Confirmation dialog (hidden by default)
            BuildConfirmDialog(overlay.transform);

            // Panel starts hidden — MainMenuUI activates it
            gameObject.SetActive(false);
        }

        private SlotRowRefs BuildSlotRow(Transform parent, int slotIndex)
        {
            // Row background
            var rowBg = UIFactory.CreatePanel(parent, new Color(0.18f, 0.18f, 0.22f, 1f));
            rowBg.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 110f);
            var rowGo = rowBg.gameObject;
            rowGo.name = $"SlotRow_{slotIndex}";

            // Info label (left side)
            var info = UIFactory.CreateLabel(rowBg.transform, "", 18, Color.white, TextAlignmentOptions.Left);
            var infoRt = info.GetComponent<RectTransform>();
            infoRt.anchorMin        = new Vector2(0f, 0f);
            infoRt.anchorMax        = new Vector2(0.58f, 1f);
            infoRt.offsetMin        = new Vector2(14f, 0f);
            infoRt.offsetMax        = new Vector2(0f, 0f);
            info.overflowMode       = TextOverflowModes.Ellipsis;

            // Right-side button container
            var btnContainer = UIFactory.CreateLayoutGroup(rowBg.transform, vertical: false, spacing: 8f,
                padding: new RectOffset(4, 8, 12, 12));
            var btnRt = btnContainer.GetComponent<RectTransform>();
            btnRt.anchorMin        = new Vector2(0.58f, 0f);
            btnRt.anchorMax        = new Vector2(1f, 1f);
            btnRt.offsetMin        = Vector2.zero;
            btnRt.offsetMax        = Vector2.zero;

            // Primary action button (Continue / New Game)
            int captured = slotIndex;
            var primaryBtn = UIFactory.CreateButton(btnContainer.transform, "Continue", 18,
                () => OnPrimaryClicked(captured));
            primaryBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(150f, 55f);
            var primaryLabel = primaryBtn.GetComponentInChildren<TMP_Text>();

            // Delete button
            var deleteBtn = UIFactory.CreateButton(btnContainer.transform, "Delete", 18,
                () => OnDeleteClicked(captured));
            var deleteBtnImg = deleteBtn.GetComponent<Image>();
            deleteBtnImg.color = new Color(0.55f, 0.12f, 0.12f, 0.92f);
            deleteBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(90f, 55f);

            return new SlotRowRefs
            {
                InfoLabel      = info,
                PrimaryButton  = primaryBtn,
                PrimaryBtnLabel = primaryLabel,
                DeleteButton   = deleteBtn
            };
        }

        private void BuildConfirmDialog(Transform parent)
        {
            // Semi-transparent overlay
            var dialogOverlay = UIFactory.CreatePanel(parent, new Color(0f, 0f, 0f, 0.6f));
            UIFactory.StretchToParent(dialogOverlay.GetComponent<RectTransform>());
            _confirmDialog = dialogOverlay.gameObject;

            // Dialog card
            var card = UIFactory.CreatePanel(dialogOverlay.transform, new Color(0.14f, 0.14f, 0.18f, 1f));
            UIFactory.CenterWithSize(card.GetComponent<RectTransform>(), new Vector2(420f, 200f));

            // Message label
            var msg = UIFactory.CreateLabel(card.transform, "Are you sure?", 24, Color.white);
            var msgRt = msg.GetComponent<RectTransform>();
            msgRt.anchorMin        = new Vector2(0f, 0.5f);
            msgRt.anchorMax        = new Vector2(1f, 1f);
            msgRt.offsetMin        = new Vector2(0f, 0f);
            msgRt.offsetMax        = new Vector2(0f, 0f);
            msg.name               = "ConfirmMessage";

            // Button row
            var btnRow = UIFactory.CreateLayoutGroup(card.transform, vertical: false, spacing: 20f);
            var btnRt  = btnRow.GetComponent<RectTransform>();
            btnRt.anchorMin        = new Vector2(0f, 0f);
            btnRt.anchorMax        = new Vector2(1f, 0.5f);
            btnRt.offsetMin        = new Vector2(0f, 0f);
            btnRt.offsetMax        = new Vector2(0f, 0f);

            UIFactory.CreateButton(btnRow.transform, "Confirm", 20, OnConfirmDialogAccepted)
                .GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 50f);

            UIFactory.CreateButton(btnRow.transform, "Cancel", 20, OnConfirmDialogCancelled)
                .GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 50f);

            _confirmDialog.SetActive(false);
        }

        // ── Slot data refresh ─────────────────────────────────────────────────

        private void RefreshSlots()
        {
            // Guard: SaveManager may not exist in the main menu scene during early dev
            if (SaveManager.Instance == null)
            {
                for (int i = 0; i < 3; i++)
                    SetRowEmpty(i);
                return;
            }

            var slots = SaveManager.Instance.GetAllSlots();
            for (int i = 0; i < 3; i++)
            {
                var data = (slots != null && i < slots.Length) ? slots[i] : null;
                if (data == null || !data.exists)
                    SetRowEmpty(i);
                else
                    SetRowOccupied(i, data);
            }
        }

        private void SetRowEmpty(int i)
        {
            _rows[i].InfoLabel.text      = $"Slot {i + 1}\n<color=#888>Empty</color>";
            _rows[i].PrimaryBtnLabel.text = "New Game";
            _rows[i].DeleteButton.gameObject.SetActive(false);
        }

        private void SetRowOccupied(int i, SaveSlotInfo data)
        {
            string dateStr = data.timestampTicks > 0
                ? new System.DateTime(data.timestampTicks).ToString("yyyy-MM-dd HH:mm")
                : "Unknown date";
            _rows[i].InfoLabel.text =
                $"Slot {i + 1}\n" +
                $"<size=15><color=#aaa>{dateStr}</color></size>\n" +
                $"<size=15>Seed: {data.seed}  |  {data.coins} coins</size>";
            _rows[i].PrimaryBtnLabel.text = "Continue";
            _rows[i].DeleteButton.gameObject.SetActive(true);
        }

        // ── Button handlers ───────────────────────────────────────────────────

        private void OnPrimaryClicked(int slot)
        {
            var slots = SaveManager.Instance?.GetAllSlots();
            bool occupied = slots != null && slot < slots.Length && slots[slot] != null && slots[slot].exists;

            if (occupied)
            {
                // Continue — load directly
                SaveManager.Instance.LoadGame(slot);
                SceneManager.LoadScene(GAME_SCENE);
            }
            else
            {
                // New Game on empty slot — no confirmation needed
                StartNewGame(slot);
            }
        }

        private void OnDeleteClicked(int slot)
        {
            ShowConfirm($"Delete slot {slot + 1}?", () =>
            {
                SaveManager.Instance?.DeleteSlot(slot);
                RefreshSlots();
            });
        }

        private void OnBackClicked()
        {
            gameObject.SetActive(false);
        }

        private void StartNewGame(int slot)
        {
            SaveManager.Instance?.NewGame(slot);
            SceneManager.LoadScene(GAME_SCENE);
        }

        // ── Confirmation dialog ───────────────────────────────────────────────

        private void ShowConfirm(string message, Action onConfirm)
        {
            _pendingAction = onConfirm;

            // Update the message text inside the dialog
            var msgLabel = _confirmDialog.GetComponentInChildren<TMP_Text>();
            if (msgLabel != null) msgLabel.text = message;

            _confirmDialog.SetActive(true);
        }

        private void OnConfirmDialogAccepted()
        {
            _confirmDialog.SetActive(false);
            _pendingAction?.Invoke();
            _pendingAction = null;
        }

        private void OnConfirmDialogCancelled()
        {
            _confirmDialog.SetActive(false);
            _pendingAction = null;
        }
    }
}
