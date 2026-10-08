using System;
using NUnit.Framework;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Systems
{
    [TestFixture]
    public sealed class EventBusTests
    {
        private EventBus _bus;

        [SetUp]
        public void SetUp()
        {
            _bus = new EventBus();
        }

        [Test]
        public void Publish_SubscriberReceivesEvent()
        {
            GoldChangedEvent? received = null;
            _bus.Subscribe<GoldChangedEvent>(e => received = e);

            _bus.Publish(new GoldChangedEvent(100, 50));

            Assert.IsNotNull(received);
            Assert.AreEqual(100, received.Value.NewAmount);
            Assert.AreEqual(50, received.Value.Delta);
        }

        [Test]
        public void Publish_MultipleSubscribers_AllReceive()
        {
            int callCount = 0;
            _bus.Subscribe<GoldChangedEvent>(_ => callCount++);
            _bus.Subscribe<GoldChangedEvent>(_ => callCount++);

            _bus.Publish(new GoldChangedEvent(100, 50));

            Assert.AreEqual(2, callCount);
        }

        [Test]
        public void Unsubscribe_StopsReceivingEvents()
        {
            int callCount = 0;
            void handler(GoldChangedEvent e) => callCount++;
            _bus.Subscribe<GoldChangedEvent>(handler);
            _bus.Unsubscribe<GoldChangedEvent>(handler);

            _bus.Publish(new GoldChangedEvent(100, 50));

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Publish_WrongType_SubscriberNotCalled()
        {
            int callCount = 0;
            _bus.Subscribe<GoldChangedEvent>(_ => callCount++);

            _bus.Publish(new CrystalsChangedEvent(10, 5));

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Publish_NoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _bus.Publish(new GoldChangedEvent(100, 50)));
        }

        [Test]
        public void Subscribe_DuringPublish_DoesNotThrow()
        {
            // Re-entrancy: subscribing during a publish should not cause
            // a collection-modified exception thanks to the ToArray() copy.
            _bus.Subscribe<GoldChangedEvent>(_ =>
            {
                _bus.Subscribe<GoldChangedEvent>(_ => { });
            });

            Assert.DoesNotThrow(() => _bus.Publish(new GoldChangedEvent(100, 50)));
        }

        [Test]
        public void Unsubscribe_DuringPublish_DoesNotThrow()
        {
            Action<GoldChangedEvent> handler = null;
            handler = _ => _bus.Unsubscribe(handler);
            _bus.Subscribe(handler);

            Assert.DoesNotThrow(() => _bus.Publish(new GoldChangedEvent(100, 50)));
        }

        /// <summary>
        /// A throwing subscriber must not swallow the event for everyone behind it.
        /// The Ledger publishes immediately after mutating, so one bad Presenter would
        /// otherwise leave every other listener with a stale view of the game state.
        /// </summary>
        [Test]
        public void Publish_OneSubscriberThrows_OthersStillReceiveTheEvent()
        {
            bool beforeRan = false, afterRan = false;

            _bus.Subscribe<GoldChangedEvent>(_ => beforeRan = true);
            _bus.Subscribe<GoldChangedEvent>(_ => throw new InvalidOperationException("boom"));
            _bus.Subscribe<GoldChangedEvent>(_ => afterRan = true);

            Assert.Throws<InvalidOperationException>(() => _bus.Publish(new GoldChangedEvent(100, 50)));

            Assert.IsTrue(beforeRan, "Subscriber registered before the thrower must still run.");
            Assert.IsTrue(afterRan, "Subscriber registered after the thrower must still run.");
        }

        [Test]
        public void Publish_MultipleSubscribersThrow_AggregatesThemAll()
        {
            _bus.Subscribe<GoldChangedEvent>(_ => throw new InvalidOperationException("one"));
            _bus.Subscribe<GoldChangedEvent>(_ => throw new InvalidOperationException("two"));

            var ex = Assert.Throws<AggregateException>(() => _bus.Publish(new GoldChangedEvent(1, 1)));
            Assert.AreEqual(2, ex.InnerExceptions.Count);
        }

        [Test]
        public void Publish_HandlerUnsubscribedByAnEarlierHandler_DoesNotRun()
        {
            bool secondRan = false;
            Action<GoldChangedEvent> second = _ => secondRan = true;

            _bus.Subscribe<GoldChangedEvent>(_ => _bus.Unsubscribe(second));
            _bus.Subscribe(second);

            _bus.Publish(new GoldChangedEvent(100, 50));

            Assert.IsFalse(secondRan,
                "A handler removed during delivery must not be invoked from the snapshot.");
        }

        [Test]
        public void Publish_HandlerSubscribedByAnEarlierHandler_DoesNotRunThisDelivery()
        {
            int lateRuns = 0;

            _bus.Subscribe<GoldChangedEvent>(_ => _bus.Subscribe<GoldChangedEvent>(__ => lateRuns++));

            _bus.Publish(new GoldChangedEvent(100, 50));
            Assert.AreEqual(0, lateRuns, "A handler added mid-delivery joins from the next publish.");

            _bus.Publish(new GoldChangedEvent(100, 50));
            Assert.AreEqual(1, lateRuns);
        }

        [Test]
        public void Clear_RemovesAllSubscribers()
        {
            bool ran = false;
            _bus.Subscribe<GoldChangedEvent>(_ => ran = true);

            _bus.Clear();
            _bus.Publish(new GoldChangedEvent(100, 50));

            Assert.IsFalse(ran);
        }
    }
}
