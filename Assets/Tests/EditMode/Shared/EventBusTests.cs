using System;
using NUnit.Framework;
using KingdomRuler.Core;
using KingdomRuler.Shared.Ledger;

namespace KingdomRuler.Tests.EditMode.Shared
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
            GoldChanged? received = null;
            _bus.Subscribe<GoldChanged>(e => received = e);

            _bus.Publish(new GoldChanged(100, 50));

            Assert.IsNotNull(received);
            Assert.AreEqual(100, received.Value.NewAmount);
            Assert.AreEqual(50, received.Value.Delta);
        }

        [Test]
        public void Publish_MultipleSubscribers_AllReceive()
        {
            int callCount = 0;
            _bus.Subscribe<GoldChanged>(_ => callCount++);
            _bus.Subscribe<GoldChanged>(_ => callCount++);

            _bus.Publish(new GoldChanged(100, 50));

            Assert.AreEqual(2, callCount);
        }

        [Test]
        public void Unsubscribe_StopsReceivingEvents()
        {
            int callCount = 0;
            void handler(GoldChanged e) => callCount++;
            _bus.Subscribe<GoldChanged>(handler);
            _bus.Unsubscribe<GoldChanged>(handler);

            _bus.Publish(new GoldChanged(100, 50));

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Publish_WrongType_SubscriberNotCalled()
        {
            int callCount = 0;
            _bus.Subscribe<GoldChanged>(_ => callCount++);

            _bus.Publish(new CrystalsChanged(10, 5));

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Publish_NoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _bus.Publish(new GoldChanged(100, 50)));
        }

        [Test]
        public void Subscribe_DuringPublish_DoesNotThrow()
        {
            // Re-entrancy: subscribing during a publish should not cause
            // a collection-modified exception thanks to the ToArray() copy.
            _bus.Subscribe<GoldChanged>(_ =>
            {
                _bus.Subscribe<GoldChanged>(_ => { });
            });

            Assert.DoesNotThrow(() => _bus.Publish(new GoldChanged(100, 50)));
        }

        [Test]
        public void Unsubscribe_DuringPublish_DoesNotThrow()
        {
            Action<GoldChanged> handler = null;
            handler = _ => _bus.Unsubscribe(handler);
            _bus.Subscribe(handler);

            Assert.DoesNotThrow(() => _bus.Publish(new GoldChanged(100, 50)));
        }
    }
}
