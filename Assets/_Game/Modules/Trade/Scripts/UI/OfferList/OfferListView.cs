using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomRuler.Modules.Trade.UI
{
    /// <summary>
    /// The scrolling list of offers. Keeps one row per offer id, so an accepted offer's own row fades
    /// out while the others stay put, and a new batch can be animated in.
    /// </summary>
    /// <remarks>
    /// Rows are created once and pooled: the list is at most <c>OfferCount</c> long and is replaced
    /// every 20 minutes, so a fixed pool is the whole cost (<c>ARCHITECTURE.md</c> §9).
    /// </remarks>
    public sealed class OfferListView : MonoBehaviour
    {
        [SerializeField] private RectTransform _content;
        [SerializeField] private OfferRowView  _rowPrefab;
        [Tooltip("Shown when there are no offers.")]
        [SerializeField] private GameObject    _emptyLabel;
        [Tooltip("Seconds between rows when a new batch animates in.")]
        [SerializeField] private float         _arrivalStagger = 0.04f;

        private readonly List<OfferRowView> _rows = new();
        private readonly HashSet<string>    _ids  = new();

        public event Action<string> OfferPressed;

        /// <summary>
        /// Show <paramref name="offers"/> in order. A row whose offer is gone fades out — unless new
        /// offers arrived too, which is a refresh: then the old rows simply make way.
        /// </summary>
        public void Show(OfferRowViewData[] offers)
        {
            _ids.Clear();
            bool anyNew = false;
            foreach (var offer in offers)
            {
                _ids.Add(offer.OfferId);
                if (FindRow(offer.OfferId) == null) anyNew = true;
            }

            foreach (var row in _rows)
                if (row.OfferId != null && !_ids.Contains(row.OfferId))
                {
                    if (anyNew) row.Release();
                    else        row.PlayRemoval();
                }

            for (int i = 0; i < offers.Length; i++)
            {
                var row = FindRow(offers[i].OfferId) ?? TakeFreeRow();
                row.Show(offers[i]);
                row.transform.SetSiblingIndex(i);
            }

            _emptyLabel.SetActive(offers.Length == 0);
        }

        /// <summary>Animate every shown row in, top to bottom.</summary>
        public void PlayArrival()
        {
            int index = 0;
            foreach (Transform child in _content)
            {
                var row = child.GetComponent<OfferRowView>();
                if (row != null && row.OfferId != null) row.PlayArrival(index++ * _arrivalStagger);
            }
        }

        private OfferRowView FindRow(string offerId)
        {
            foreach (var row in _rows)
                if (row.OfferId == offerId) return row;
            return null;
        }

        private OfferRowView TakeFreeRow()
        {
            foreach (var row in _rows)
                if (row.OfferId == null && !row.gameObject.activeSelf) return row;

            var created = Instantiate(_rowPrefab, _content);
            created.Pressed += id => OfferPressed?.Invoke(id);
            _rows.Add(created);
            return created;
        }
    }
}
