using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>
    /// The short receipt after an accept: every resource received and, on lines of their own, every
    /// unit lost to a full warehouse — so the loss is always visible (GDD §7). Shows for a moment,
    /// then fades; a new trade replaces it. It has no raycast targets, so it never swallows a tap.
    /// </summary>
    /// <remarks>
    /// A strip on the screen rather than a popup: a popup would cost a tap on every trade, and the
    /// player already saw the loss before confirming.
    /// </remarks>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class TradeReceiptView : MonoBehaviour
    {
        [Serializable]
        private struct Line
        {
            public GameObject      Root;
            public Image           Icon;
            public TextMeshProUGUI Label;
        }

        [Tooltip("Six lines: up to three received and three lost.")]
        [SerializeField] private Line[] _lines;
        [SerializeField] private Color  _gainColor = new(0.85f, 0.95f, 0.75f, 1f);
        [SerializeField] private Color  _lossColor = new(1f, 0.6f, 0.5f, 1f);

        [Header("Animation")]
        [SerializeField] private float _fadeInDuration  = 0.2f;
        [SerializeField] private float _holdDuration    = 2.5f;
        [SerializeField] private float _fadeOutDuration = 0.4f;

        private CanvasGroup _group;
        private Sequence    _tween;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
        }

        private void OnDisable()
        {
            _tween?.Kill();
            if (_group != null) _group.alpha = 0f;
        }

        private void OnDestroy() => _tween?.Kill();

        public void Show(TradeReceiptViewData data)
        {
            int count = data.Lines?.Length ?? 0;
            for (int i = 0; i < _lines.Length; i++)
            {
                bool used = i < count;
                _lines[i].Root.SetActive(used);
                if (!used) continue;

                var line = data.Lines[i];
                _lines[i].Icon.sprite  = line.Icon;
                _lines[i].Icon.enabled = line.Icon != null;
                _lines[i].Label.SetText(line.Text);
                _lines[i].Label.color  = line.IsLoss ? _lossColor : _gainColor;
            }

            _tween?.Kill();
            _tween = DOTween.Sequence()
                .Append(_group.DOFade(1f, _fadeInDuration))
                .AppendInterval(_holdDuration)
                .Append(_group.DOFade(0f, _fadeOutDuration))
                .SetUpdate(true);
        }
    }
}
