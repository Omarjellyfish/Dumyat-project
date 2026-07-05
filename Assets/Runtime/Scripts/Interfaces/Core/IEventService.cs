using System;
namespace DumyatRuntime{
    public interface IEventService
    {
        void Subscribe<T>(Action<T> handler) where T : IGameEvent;
        void Unsubscribe<T>(Action<T> handler) where T : IGameEvent;
        void Publish<T>(T eventData) where T : IGameEvent;
    }

}
