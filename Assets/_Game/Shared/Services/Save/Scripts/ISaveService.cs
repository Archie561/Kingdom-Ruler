namespace KingdomRuler.Shared.Services
{
    /// <summary>
    /// Persistence abstraction. Current impl is local JSON file;
    /// cloud save would be a second implementation.
    /// </summary>
    public interface ISaveService
    {
        /// <summary>
        /// Persist the given state. Returns false if the write failed; the previous save
        /// is left intact in that case. Implementations must not throw — callers save at
        /// shutdown and focus-loss, where an exception has nowhere useful to go.
        /// </summary>
        bool Save(GameStateDto state);

        /// <summary>
        /// Read the saved state, or null if there is none. An unreadable save is discarded
        /// and null returned, so the caller starts a fresh game (ARCHITECTURE.md §5).
        /// </summary>
        GameStateDto Load();

        void Delete();
    }
}
