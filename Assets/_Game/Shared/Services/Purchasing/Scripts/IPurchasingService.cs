using System.Threading.Tasks;

namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Abstraction over IAP purchasing. Current impl is a mock;
    /// real store billing is a second implementation behind this interface.
    /// </summary>
    public interface IPurchasingService
    {
        Task<PurchaseResult> PurchaseCrystalPack(string packId);
    }
}
