using System.Threading.Tasks;
using NUnit.Framework;
using KingdomRuler.Systems.Purchasing;

namespace KingdomRuler.Tests.EditMode.Systems
{
    [TestFixture]
    public sealed class MockPurchasingServiceTests
    {
        private MockPurchasingService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new MockPurchasingService();
        }

        [Test]
        public async Task PurchaseCrystalPack_ReturnsSuccess()
        {
            var result = await _service.PurchaseCrystalPack("test_pack_100");

            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task PurchaseCrystalPack_AwardsCrystals()
        {
            var result = await _service.PurchaseCrystalPack("test_pack_100");

            Assert.Greater(result.CrystalsAwarded, 0,
                "Mock purchase should award a positive number of crystals.");
        }

        [Test]
        public async Task PurchaseCrystalPack_NoErrorMessage()
        {
            var result = await _service.PurchaseCrystalPack("test_pack_100");

            Assert.IsNull(result.ErrorMessage);
        }

        [Test]
        public void PurchaseResult_Succeeded_Factory()
        {
            var result = PurchaseResult.Succeeded(50);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(50, result.CrystalsAwarded);
            Assert.IsNull(result.ErrorMessage);
        }

        [Test]
        public void PurchaseResult_Failed_Factory()
        {
            var result = PurchaseResult.Failed("Network error");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, result.CrystalsAwarded);
            Assert.AreEqual("Network error", result.ErrorMessage);
        }
    }
}
