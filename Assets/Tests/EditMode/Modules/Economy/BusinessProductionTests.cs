using System;
using NUnit.Framework;
using UnityEngine;
using KingdomRuler.Modules.Economy;
using KingdomRuler.Systems.Ledger;

namespace KingdomRuler.Tests.EditMode.Modules.Economy
{
    /// <summary>The rate seam: linear production, a storage of StorageHours, and saturation at the gold ceiling.</summary>
    [TestFixture]
    public sealed class BusinessProductionTests
    {
        private BusinessDefinition _bakery;
        private EconomyConfig      _config;

        [SetUp]
        public void SetUp()
        {
            _bakery = ScriptableObject.CreateInstance<BusinessDefinition>();
            _bakery.BusinessId  = "business.bakery";
            _bakery.GoldPerHour = 60;
            _bakery.UnitPrice   = 100;

            _config = ScriptableObject.CreateInstance<EconomyConfig>();   // StorageHours 24
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_bakery);
            UnityEngine.Object.DestroyImmediate(_config);
        }

        [TestCase(0, 0L)]
        [TestCase(1, 60L)]
        [TestCase(7, 420L)]
        public void GoldPerHour_IsUnitsTimesTheBaseRate(int units, long expected)
        {
            Assert.AreEqual(expected, BusinessProduction.GoldPerHour(_bakery, units));
        }

        [TestCase(1, 1_440L)]
        [TestCase(10, 14_400L)]
        public void StorageCapacity_IsStorageHoursOfProduction(int units, long expected)
        {
            Assert.AreEqual(expected, BusinessProduction.StorageCapacity(_bakery, units, _config));
        }

        [Test]
        public void StoragePeriod_IsStorageHours_AtAnyNumberOfUnits()
        {
            Assert.AreEqual(TimeSpan.FromHours(24), BusinessProduction.StoragePeriod(_config));

            _config.StorageHours = 8;
            Assert.AreEqual(TimeSpan.FromHours(8), BusinessProduction.StoragePeriod(_config));
            Assert.AreEqual(480L, BusinessProduction.StorageCapacity(_bakery, 1, _config));
        }

        [Test]
        public void NothingOwned_OrNoBusiness_ProducesNothing()
        {
            Assert.AreEqual(0, BusinessProduction.GoldPerHour(_bakery, -3));
            Assert.AreEqual(0, BusinessProduction.GoldPerHour(null, 5));
            Assert.AreEqual(0, BusinessProduction.StorageCapacity(_bakery, 0, _config));
        }

        [Test]
        public void AMistunedRate_SaturatesAtTheGoldCeiling_InsteadOfOverflowing()
        {
            _bakery.GoldPerHour = long.MaxValue / 2;

            Assert.AreEqual(KingdomLedger.MaxGold, BusinessProduction.GoldPerHour(_bakery, EconomyConfig.MaxUnitsPerType));
            Assert.AreEqual(KingdomLedger.MaxGold, BusinessProduction.StorageCapacity(_bakery, EconomyConfig.MaxUnitsPerType, _config));
        }
    }
}
