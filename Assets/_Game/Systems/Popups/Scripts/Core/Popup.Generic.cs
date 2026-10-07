using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KingdomRuler.Systems.Popups
{
    /// <summary>
    /// The base class every popup derives from. <typeparamref name="TData"/> is what it shows;
    /// <typeparamref name="TChoice"/> is what its buttons mean, usually an enum.
    /// </summary>
    /// <remarks>
    /// <para>A popup implements <see cref="Render"/> and calls <see cref="Choose"/> from its
    /// buttons. Everything else — refreshing, waiting, closing — happens here.</para>
    ///
    /// <para>Closing without a choice (the backdrop, <c>closeWhen</c>, the popup being destroyed)
    /// gives the caller <c>null</c>: no response at all, which never equals a real choice.</para>
    ///
    /// <para>This file is <c>Popup.Generic.cs</c> because <c>Popup.cs</c> holds the non-generic
    /// <see cref="Popup"/> — the same pairing as <c>Task</c> and <c>Task&lt;T&gt;</c>.</para>
    /// </remarks>
    public abstract class Popup<TData, TChoice> : Popup where TChoice : struct
    {
        /// <summary>How often an open popup re-reads its data. One value for every popup.</summary>
        private const float RefreshIntervalSeconds = 0.25f;

        private Func<TData> _readData;
        private Func<bool>  _closeWhen;

        private bool  _hasRendered;
        private TData _rendered;
        private float _secondsUntilRefresh;

        /// <summary>The caller waiting for the next button press, or null if nobody is.</summary>
        private UniTaskCompletionSource<TChoice?> _waitingCaller;

        // ── For callers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Fill the popup with <paramref name="data"/> and put it on screen. It <b>stays open</b>
        /// until it is closed — for a panel taking several presses, read with
        /// <see cref="WaitForChoice"/>. For a single question, use <see cref="Ask(TData)"/>.
        /// </summary>
        /// <remarks>The data is fixed: it is drawn once and never re-read.</remarks>
        public Popup<TData, TChoice> Show(TData data) => Show(() => data);

        /// <summary>
        /// Fill the popup with live data and put it on screen. It <b>stays open</b> until it is
        /// closed — for a panel taking several presses, read with <see cref="WaitForChoice"/>.
        /// For a single question, use <see cref="Ask(Func{TData}, Func{bool})"/>.
        /// </summary>
        /// <param name="data">
        /// Read now, then a few times a second while the popup is open; it redraws whenever the
        /// result differs. It must only <b>read</b> — it runs over and over.
        /// </param>
        /// <param name="closeWhen">
        /// Optional, checked just as often. Once it is true the popup closes with no choice — for
        /// a question that stopped making sense while the player was reading it.
        /// </param>
        public Popup<TData, TChoice> Show(Func<TData> data, Func<bool> closeWhen = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (IsOpen || IsClosed)
                throw new InvalidOperationException("A popup is shown once. Create a new one instead.");

            _readData  = data;
            _closeWhen = closeWhen;

            Reveal();
            Refresh();
            return this;
        }

        /// <summary>
        /// Wait for the next button press. Gives <c>null</c> if the popup closes first, or has
        /// already closed.
        /// </summary>
        public UniTask<TChoice?> WaitForChoice()
        {
            if (IsClosed) return UniTask.FromResult<TChoice?>(null);
            if (!IsOpen)
                throw new InvalidOperationException("Show the popup before waiting for a choice.");
            if (_waitingCaller != null)
                throw new InvalidOperationException("Something is already waiting for this popup's choice.");

            _waitingCaller = new UniTaskCompletionSource<TChoice?>();
            return _waitingCaller.Task;
        }

        /// <summary>
        /// Fill the popup with <paramref name="data"/>, put it on screen, wait for the player's
        /// <b>one</b> choice, then close it. Gives <c>null</c> if it closed without a choice.
        /// </summary>
        /// <remarks>
        /// <c>Show</c> + <c>WaitForChoice</c> + <c>Close</c> in one call — the way to ask a
        /// question. The data is fixed: it is drawn once and never re-read.
        /// </remarks>
        public UniTask<TChoice?> Ask(TData data) => Ask(() => data);

        /// <summary>
        /// Fill the popup with live data, put it on screen, wait for the player's <b>one</b>
        /// choice, then close it. Gives <c>null</c> if it closed without a choice.
        /// </summary>
        /// <remarks>
        /// <c>Show</c> + <c>WaitForChoice</c> + <c>Close</c> in one call — the way to ask a
        /// question. Parameters as for <see cref="Show(Func{TData}, Func{bool})"/>.
        /// </remarks>
        public async UniTask<TChoice?> Ask(Func<TData> data, Func<bool> closeWhen = null)
        {
            Show(data, closeWhen);
            TChoice? choice = await WaitForChoice();
            Close();
            return choice;
        }

        // ── For popup authors ─────────────────────────────────────────────────────────

        /// <summary>Draw the data. Called when the popup opens and whenever the data changes.</summary>
        protected abstract void Render(TData data);

        /// <summary>
        /// The player pressed a button. Ignored when nobody is waiting — the popup has closed, or
        /// the caller is still handling the previous press — so a fast double tap counts once.
        /// </summary>
        protected void Choose(TChoice choice)
        {
            if (!IsOpen || _waitingCaller == null) return;

            // Cleared before the caller resumes, because the caller may wait again straight away.
            var caller = _waitingCaller;
            _waitingCaller = null;
            caller.TrySetResult(choice);
        }

        // ── Internals ─────────────────────────────────────────────────────────────────

        /// <remarks>Override only to add to it, and call the base — this is the refresh clock.</remarks>
        protected virtual void Update()
        {
            if (!IsOpen) return;

            // Unscaled, so the popup stays live while the game is paused behind it.
            _secondsUntilRefresh -= Time.unscaledDeltaTime;
            if (_secondsUntilRefresh <= 0f) Refresh();
        }

        private void Refresh()
        {
            _secondsUntilRefresh = RefreshIntervalSeconds;

            try
            {
                if (_closeWhen != null && _closeWhen())
                {
                    Close();
                    return;
                }

                TData data = _readData();
                if (_hasRendered && EqualityComparer<TData>.Default.Equals(data, _rendered)) return;

                _rendered    = data;
                _hasRendered = true;
                Render(data);
            }
            catch (Exception exception)
            {
                // A popup that cannot draw itself must not stay open to be answered: close it
                // with no choice, so nothing is ever bought from a broken popup.
                Debug.LogException(exception, this);
                Close();
            }
        }

        protected sealed override void ReleaseCaller()
        {
            // Nothing the caller handed in runs after the popup has closed.
            _readData  = null;
            _closeWhen = null;

            var caller = _waitingCaller;
            _waitingCaller = null;
            caller?.TrySetResult(null);
        }
    }
}
