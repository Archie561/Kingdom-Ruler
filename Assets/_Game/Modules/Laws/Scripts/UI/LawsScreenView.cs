using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using KingdomRuler.Shared.Text;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Modules.Laws.UI
{
    /// <summary>
    /// The Laws screen. Renders the presenter's state onto its components (the card, the dials)
    /// and simple widgets (labels, the refill button), and forwards player input back.
    /// </summary>
    /// <remarks>
    /// It displays the replenishment countdown but never advances it: the timer runs on the game
    /// clock whether or not this screen exists.
    /// </remarks>
    [RequireComponent(typeof(Canvas))]
    public sealed class LawsScreenView : MonoBehaviour
    {
        [Header("Card")]
        [SerializeField] private LawCardView     _card;
        [Tooltip("Held-card readout, e.g. \"6/8\".")]
        [SerializeField] private TextMeshProUGUI _cardCountLabel;
        [Tooltip("Shown when no card is held and none is on the way.")]
        [SerializeField] private GameObject      _waitingOverlay;

        [Header("Characteristics")]
        [Tooltip("One dial per characteristic. Matched by each dial's own Type, so order does not matter.")]
        [SerializeField] private CharacteristicDialView[] _dials;

        [Header("Timer & Refill")]
        [SerializeField] private TextMeshProUGUI _timerLabel;
        [SerializeField] private Button          _refillButton;
        [SerializeField] private TextMeshProUGUI _refillCostLabel;

        private LawsPresenter _presenter;

        // Last values written, so a refresh does not rewrite unchanged text and dirty the canvas.
        private int _shownTimerSeconds = -1;
        private int _shownHeldCount    = -1;
        private int _shownRefillCost   = -1;

        [Inject]
        public void Construct(LawsPresenter presenter) => _presenter = presenter;

        private void Start()
        {
            if (_presenter == null)
            {
                Debug.LogError("[LawsScreenView] No LawsPresenter injected — enter Play mode from the " +
                               "Bootstrap scene, which loads Main and injects its views.", this);
                enabled = false;
                return;
            }

            CheckDialsCoverEveryCharacteristic();

            _card.Swiped += decision => _presenter.ResolveCard(decision);
            _refillButton.onClick.AddListener(() => _presenter.RequestRefill().Forget());

            _presenter.StateChanged            += Refresh;
            _presenter.CardArrived             += _card.PlayArrival;
            _presenter.CharacteristicLeveledUp += PlayLevelUp;

            // OnEnable may have run before injection.
            _presenter.SetScreenVisible(isActiveAndEnabled);
            Refresh();
        }

        private void OnEnable()  => _presenter?.SetScreenVisible(true);
        private void OnDisable() => _presenter?.SetScreenVisible(false);

        // The presenter outlives this view, so its events must be released.
        private void OnDestroy()
        {
            if (_presenter == null) return;
            _presenter.StateChanged            -= Refresh;
            _presenter.CardArrived             -= _card.PlayArrival;
            _presenter.CharacteristicLeveledUp -= PlayLevelUp;
        }

        private void Update()
        {
            if (!_presenter.IsReplenishing) return;

            // Whole seconds only; rewriting the same text every frame would rebuild the canvas.
            int seconds = Mathf.CeilToInt(_presenter.SecondsUntilNextCard);
            if (seconds == _shownTimerSeconds) return;

            _shownTimerSeconds = seconds;
            _timerLabel.SetText(TimeFormat.MinutesSeconds(seconds));
        }

        private void Refresh()
        {
            int held = _presenter.HeldCardCount;
            if (held != _shownHeldCount)
            {
                _shownHeldCount = held;
                _cardCountLabel.SetText($"{held}/{_presenter.MaxCardCount}");
            }

            bool replenishing = _presenter.IsReplenishing;
            _timerLabel.gameObject.SetActive(replenishing);
            if (!replenishing) _shownTimerSeconds = -1;

            _waitingOverlay.SetActive(_presenter.IsWaiting);

            if (_presenter.TryGetActiveCard(out var card)) _card.Show(card);
            else                                           _card.Hide();

            _refillButton.interactable = _presenter.CanAffordRefill;
            int refillCost = _presenter.RefillCost;
            if (refillCost != _shownRefillCost)
            {
                _shownRefillCost = refillCost;
                _refillCostLabel.SetText(refillCost.ToString());
            }

            foreach (var dial in _dials)
                dial.Render(_presenter.GetDial(dial.Type));
        }

        private void PlayLevelUp(CharacteristicType type)
        {
            foreach (var dial in _dials)
                if (dial.Type == type) dial.PlayLevelUp();
        }

        private void CheckDialsCoverEveryCharacteristic()
        {
            foreach (CharacteristicType type in Enum.GetValues(typeof(CharacteristicType)))
            {
                int count = 0;
                foreach (var dial in _dials)
                    if (dial.Type == type) count++;

                if (count != 1)
                    Debug.LogError($"[LawsScreenView] {count} dials show {type}; there must be exactly one.", this);
            }
        }
    }
}
