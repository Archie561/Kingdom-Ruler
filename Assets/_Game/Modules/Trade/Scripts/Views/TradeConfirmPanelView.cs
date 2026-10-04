using System;
using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KingdomRuler.Modules.Trade.Presenters;

namespace KingdomRuler.Modules.Trade.Views
{
    /// <summary>
    /// The confirmation panel: the terms of a trade, why it cannot proceed, or what it will cost
    /// the player to proceed anyway.
    /// </summary>
    /// <remarks>
    /// <para><b>Blockers and warnings are rendered differently and mean different things.</b> A
    /// blocker disables Confirm; a warning does not. A full warehouse produces a warning — the
    /// player is told exactly what will be lost and may go ahead. Wiring
    /// <c>Confirm.interactable</c> to "no messages at all" would quietly turn every overflow into
    /// a refusal and undo the rule.</para>
    /// </remarks>
    public sealed class TradeConfirmPanelView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TextMeshProUGUI _giveLabel;
        [SerializeField] private TextMeshProUGUI _receiveLabel;
        [SerializeField] private TextMeshProUGUI _blockerLabel;
        [SerializeField] private TextMeshProUGUI _warningLabel;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;

        [Header("Feel")]
        [SerializeField] private float _openDuration = 0.18f;

        private readonly StringBuilder _builder = new();
        private CanvasGroup _canvasGroup;
        private Tween _fadeTween;
        private string _offerId;

        public event Action<string> OnConfirmPressed;
        public event Action OnCancelPressed;

        /// <summary>Whether the panel is currently up.</summary>
        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>
        /// The offer the panel is showing, so the View can re-resolve it after a state change —
        /// the 20-minute refresh can land while the panel is open.
        /// </summary>
        public string OfferId => _offerId;

        /// <remarks>
        /// <b>Deliberately does not call <see cref="Close"/>.</b> The panel's root is authored
        /// inactive in the prefab, so it already starts closed — and because it is inactive,
        /// Awake does not run until <see cref="Open"/> activates it. Closing here therefore fired
        /// *during* the first Open and undid it: the very first tap on an offer flashed the panel
        /// and dismissed it, while every later tap worked. Found in Play mode; no EditMode test
        /// would have caught it.
        /// </remarks>
        private void Awake()
        {
            _canvasGroup = _root != null ? _root.GetComponent<CanvasGroup>() : null;

            if (_confirmButton != null) _confirmButton.onClick.AddListener(HandleConfirm);
            if (_cancelButton  != null) _cancelButton.onClick.AddListener(HandleCancel);
        }

        private void OnDestroy()
        {
            if (_confirmButton != null) _confirmButton.onClick.RemoveListener(HandleConfirm);
            if (_cancelButton  != null) _cancelButton.onClick.RemoveListener(HandleCancel);
            _fadeTween?.Kill();
        }

        /// <summary>Show the panel for an offer.</summary>
        public void Open(TradeConfirmDisplayData data)
        {
            // Activate FIRST. This is what runs Awake on the very first open, and anything
            // assigned before that point would be clobbered by it.
            if (_root != null) _root.SetActive(true);

            _offerId = data.OfferId;

            if (_giveLabel    != null) _giveLabel.SetText(Join(data.Give));
            if (_receiveLabel != null) _receiveLabel.SetText(Join(data.Receive));

            SetMessages(_blockerLabel, data.BlockerMessages);
            SetMessages(_warningLabel, data.WarningMessages);

            // Only blockers disable Confirm. An overflow warning leaves it enabled by design.
            if (_confirmButton != null) _confirmButton.interactable = data.CanAccept;

            if (_canvasGroup != null)
            {
                _fadeTween?.Kill();
                _canvasGroup.alpha = 0f;
                _fadeTween = _canvasGroup.DOFade(1f, _openDuration).SetUpdate(true);
            }
        }

        /// <summary>Hide the panel.</summary>
        public void Close()
        {
            _offerId = null;
            _fadeTween?.Kill();
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;
            if (_root != null) _root.SetActive(false);
        }

        private void SetMessages(TextMeshProUGUI label, IReadOnlyList<string> messages)
        {
            if (label == null) return;

            bool any = messages != null && messages.Count > 0;
            label.gameObject.SetActive(any);
            if (!any) return;

            _builder.Clear();
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) _builder.Append('\n');
                _builder.Append(messages[i]);
            }
            label.SetText(_builder.ToString());
        }

        private string Join(IReadOnlyList<TradeResourceLineData> lines)
        {
            _builder.Clear();
            if (lines == null) return string.Empty;

            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) _builder.Append('\n');
                _builder.Append(lines[i].AmountText).Append(' ').Append(lines[i].Name);
            }
            return _builder.ToString();
        }

        private void HandleConfirm()
        {
            if (!string.IsNullOrEmpty(_offerId)) OnConfirmPressed?.Invoke(_offerId);
        }

        private void HandleCancel() => OnCancelPressed?.Invoke();
    }
}
