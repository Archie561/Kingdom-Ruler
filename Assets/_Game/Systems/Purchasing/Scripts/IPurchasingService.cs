using Cysharp.Threading.Tasks;

namespace KingdomRuler.Systems.Purchasing
{
    /// <summary>
    /// Abstraction over IAP purchasing. Current impl is a mock;
    /// real store billing is a second implementation behind this interface.
    /// </summary>
    public interface IPurchasingService
    {
        UniTask<PurchaseResult> PurchaseCrystalPack(string packId);
    }
}
