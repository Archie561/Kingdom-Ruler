using System.Threading.Tasks;
using UnityEngine;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Mock purchasing service that instantly succeeds.
    /// Crystal crediting to the Ledger is handled by the caller (ShopManager).
    /// </summary>
    public sealed class MockPurchasingService : IPurchasingService
    {
        // Placeholder crystal amounts per pack — will be driven by
        // CrystalPackDefinition data assets once the Shop module exists.
        private const int DefaultCrystalsPerPack = 100;

        public Task<PurchaseResult> PurchaseCrystalPack(string packId)
        {
            Debug.Log($"[MockPurchasingService] Mock purchase for pack '{packId}' — instant success.");
            return Task.FromResult(PurchaseResult.Succeeded(DefaultCrystalsPerPack));
        }
    }
}
