using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>
    /// One characteristic dial: a progress ring, the icon inside it and the level below.
    /// </summary>
    /// <remarks>
    /// Not a tap target of its own: the whole characteristics panel is one button that opens the
    /// characteristics popup, which is easier to hit on a small screen than one dial.
    /// </remarks>
    public sealed class CharacteristicDialView : MonoBehaviour
    {
        [Tooltip("The characteristic this dial shows. Set per instance.")]
        [SerializeField] private CharacteristicType _type;

        [Header("Display")]
        [SerializeField] private Image           _icon;
        [SerializeField] private TextMeshProUGUI _levelLabel;
        [Tooltip("The progress ring: Image type Filled, fillMethod Radial360.")]
        [SerializeField] private Image           _fillBar;

        [Header("Animation")]
        [Tooltip("Seconds to slide the ring to a new value within a level.")]
        [SerializeField] private float _barFillDuration = 1.0f;
        [Tooltip("Seconds to top the ring off when a level completes.")]
        [SerializeField] private float _levelUpTopOffDuration = 0.5f;
        [SerializeField] private float _levelUpPunchScale     = 0.5f;
        [SerializeField] private float _levelUpPunchDuration  = 0.5f;

        private LevelFill _fill;
        private Tween     _punchTween;

        // Last rendered values, so an unrelated refresh does not rewrite unchanged labels.
        private bool   _hasRendered;
        private Sprite _renderedIcon;
        private int    _renderedLevel;

        public CharacteristicType Type => _type;

        private void Awake() => _fill = new LevelFill(_fillBar, _barFillDuration, _levelUpTopOffDuration);

        private void OnDestroy()
        {
            _fill?.Kill();
            _punchTween?.Kill();
        }

        public void Render(CharacteristicDialViewData data)
        {
            bool firstRender = !_hasRendered;

            if (firstRender || data.Icon != _renderedIcon)
            {
                // An Image without a sprite draws a white box, so hide it instead.
                _icon.sprite  = data.Icon;
                _icon.enabled = data.Icon != null;
            }

            if (firstRender || data.Level != _renderedLevel)
                _levelLabel.SetText(data.Level.ToString());

            _fill.Render(data.Level, data.Progress);

            _hasRendered   = true;
            _renderedIcon  = data.Icon;
            _renderedLevel = data.Level;
        }

        public void PlayLevelUp()
        {
            _punchTween?.Kill(complete: true);
            _punchTween = _levelLabel.transform
                .DOPunchScale(Vector3.one * _levelUpPunchScale, _levelUpPunchDuration, 5, 0.1f)
                .SetUpdate(true);
        }
    }
}
