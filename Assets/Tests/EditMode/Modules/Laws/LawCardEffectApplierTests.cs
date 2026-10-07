using NUnit.Framework;
using KingdomRuler.Systems.Events;
using KingdomRuler.Systems.Ledger;
using KingdomRuler.Modules.Laws.Domain;

namespace KingdomRuler.Tests.EditMode.Modules.Laws
{
    /// <summary>
    /// The sign convention: a card authors one signed number per characteristic, and the
    /// Ledger splits gains from losses into separate methods. Getting this backwards would
    /// silently invert every negative law card.
    /// </summary>
    [TestFixture]
    public sealed class LawCardEffectApplierTests
    {
        private KingdomLedger NewLedger() => new KingdomLedger(new EventBus());

        [Test]
        public void Apply_PositivePoints_AddsProgress()
        {
            var ledger = NewLedger();

            LawCardEffectApplier.Apply(ledger, new[]
            {
                new LawCardEffect(CharacteristicType.Army, 40f)
            });

            Assert.AreEqual(40f, ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void Apply_NegativePoints_RemovesProgressRatherThanAddingIt()
        {
            var ledger = NewLedger();
            ledger.AddCharacteristicPoints(CharacteristicType.Army, 60f);

            LawCardEffectApplier.Apply(ledger, new[]
            {
                new LawCardEffect(CharacteristicType.Army, -25f)
            });

            Assert.AreEqual(35f, ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }

        [Test]
        public void Apply_NegativePoints_StillCannotDropTheLevel()
        {
            var ledger = NewLedger();
            ledger.AddCharacteristicPoints(CharacteristicType.Army, 150f);   // → level 2

            LawCardEffectApplier.Apply(ledger, new[]
            {
                new LawCardEffect(CharacteristicType.Army, -9999f)
            });

            var state = ledger.GetCharacteristic(CharacteristicType.Army);
            Assert.AreEqual(2, state.Level);
            Assert.AreEqual(0f, state.PointsIntoCurrentLevel);
        }

        [Test]
        public void Apply_MultipleEffects_AppliesEachToItsOwnCharacteristic()
        {
            var ledger = NewLedger();

            LawCardEffectApplier.Apply(ledger, new[]
            {
                new LawCardEffect(CharacteristicType.Army, 10f),
                new LawCardEffect(CharacteristicType.Science, 20f),
                new LawCardEffect(CharacteristicType.Welfare, 30f)
            });

            Assert.AreEqual(10f, ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
            Assert.AreEqual(20f, ledger.GetCharacteristic(CharacteristicType.Science).PointsIntoCurrentLevel);
            Assert.AreEqual(30f, ledger.GetCharacteristic(CharacteristicType.Welfare).PointsIntoCurrentLevel);
        }

        [Test]
        public void Apply_RepeatedEffectsOnOneCharacteristic_Accumulate()
        {
            var ledger = NewLedger();

            LawCardEffectApplier.Apply(ledger, new[]
            {
                new LawCardEffect(CharacteristicType.Medicine, 15f),
                new LawCardEffect(CharacteristicType.Medicine, 25f)
            });

            Assert.AreEqual(40f, ledger.GetCharacteristic(CharacteristicType.Medicine).PointsIntoCurrentLevel);
        }

        [Test]
        public void Apply_NullOrEmpty_IsANoOp()
        {
            var ledger = NewLedger();

            Assert.DoesNotThrow(() => LawCardEffectApplier.Apply(ledger, null));
            Assert.DoesNotThrow(() => LawCardEffectApplier.Apply(ledger, new LawCardEffect[0]));
            Assert.DoesNotThrow(() => LawCardEffectApplier.Apply(null, new[]
            {
                new LawCardEffect(CharacteristicType.Army, 10f)
            }));

            Assert.AreEqual(0f, ledger.GetCharacteristic(CharacteristicType.Army).PointsIntoCurrentLevel);
        }
    }
}
