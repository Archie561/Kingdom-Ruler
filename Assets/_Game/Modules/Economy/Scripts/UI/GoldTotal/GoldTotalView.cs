using DG.Tweening;
using TMPro;
using UnityEngine;

namespace KingdomRuler.Modules.Economy.UI
{
    /// <summary>
    /// The player's total gold, on the Economy screen (GDD §8). <b>Temporary</b>: it moves to the
    /// always-visible currency header, a <c>Systems/</c> entry, when that exists — remove this folder,
    /// the <c>GoldTotal</c> object in the screen prefab and its field on <see cref="EconomyScreenView"/>.
    /// </summary>
    /// <remarks>On its own Canvas in the prefab: it changes on every collect and purchase.</remarks>
    public sealed class GoldTotalView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _amountLabel;
        [SerializeField] private float _punchScale    = 0.18f;
        [SerializeField] private float _punchDuration = 0.35f;

        private Tween _punchTween;

        /// <summary>The displayed value on the label; the screen view refreshes the text when it differs.</summary>
        public long ShownDisplay { get; private set; } = long.MinValue;

        private void OnDisable() => _punchTween?.Kill(complete: true);

        /// <summary>Show a new amount; a rise — a collect — punches the number.</summary>
        public void Show(long displayedValue, string text)
        {
            bool rose = ShownDisplay != long.MinValue && displayedValue > ShownDisplay;
            ShownDisplay = displayedValue;
            _amountLabel.SetText(text);

            if (!rose) return;
            _punchTween?.Kill(complete: true);
            _punchTween = _amountLabel.transform
                .DOPunchScale(Vector3.one * _punchScale, _punchDuration, 6, 0.4f)
                .SetUpdate(true);
        }

        /// <summary>Forget the text on the label, so the next frame re-resolves it (a language change).</summary>
        public void InvalidateText() => ShownDisplay = long.MinValue;
    }
}
