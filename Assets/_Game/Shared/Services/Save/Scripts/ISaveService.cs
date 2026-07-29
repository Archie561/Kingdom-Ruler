namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Persistence abstraction. Current impl is local JSON file;
    /// cloud save would be a second implementation.
    /// </summary>
    public interface ISaveService
    {
        void Save(GameStateDto state);
        GameStateDto Load();
        void Delete();
    }
}
