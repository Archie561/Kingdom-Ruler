using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KingdomRuler.Systems.Purchasing
{
    /// <summary>
    /// Mock purchasing service that instantly succeeds.
    /// The caller credits the crystals through the Ledger.
    /// </summary>
    public sealed class MockPurchasingService : IPurchasingService
    {
        // Placeholder crystal amounts per pack — will be driven by
        // CrystalPackDefinition data assets once the Shop module exists.
        private const int DefaultCrystalsPerPack = 100;

        public UniTask<PurchaseResult> PurchaseCrystalPack(string packId)
        {
            Debug.Log($"[MockPurchasingService] Mock purchase for pack '{packId}' — instant success.");
            return UniTask.FromResult(PurchaseResult.Succeeded(DefaultCrystalsPerPack));
        }
    }
}
