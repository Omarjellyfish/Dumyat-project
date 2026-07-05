namespace DumyatRuntime
{
    /// <summary>
    /// Marker interface for all game events. Use structs for zero-allocation dispatch.
    /// Example: public struct PlayerDiedEvent : IGameEvent { public int Score; }
    /// </summary>
    public interface IGameEvent { }
}
