namespace DumyatRuntime
{
    /// <summary>
    /// O(1) lookup for active NPC actors in the scene.
    /// NPCs self-register/unregister via <see cref="NarrativeCharacterView"/>.
    /// Same pattern as <see cref="IEventService"/> — plain C# singleton in GameInstaller.
    /// </summary>
    public interface INpcRegistryService
    {
        void Register(string characterId, INpcActor actor);
        void Unregister(string characterId);

        /// <summary>Returns the actor, or null if the character is not in the scene.</summary>
        INpcActor GetActor(string characterId);
    }
}
