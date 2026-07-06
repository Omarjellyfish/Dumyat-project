namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the player's unlock-key store. Injected in place of
    /// <c>PlayerPersistence.Singleton</c>. Backed by <see cref="PlayerPersistenceService"/>.
    /// </summary>
    public interface IPlayerPersistenceService
    {
        bool HasKey(string key);
        void AddKey(string key);
    }
}
