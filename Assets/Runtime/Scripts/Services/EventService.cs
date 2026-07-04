using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Instance-based, thread-safe publish/subscribe hub keyed on <see cref="IGameEvent"/>.
    /// Registered as <see cref="IEventService"/> singleton in the DI container by <c>GameInstaller</c>.
    ///
    /// Handlers for each event type live in a strongly-typed generic channel
    /// (<c>Channel&lt;T&gt;</c>). Because the CLR creates one channel per event type,
    /// locating the handler list for a <see cref="Publish{T}"/> is a direct static-field
    /// read -- no dictionary lookup, no boxing, and no per-handler cast. Publishing also
    /// walks the live handler list with zero per-call allocation: subscription changes
    /// made while an event is being dispatched are buffered and applied once dispatch
    /// unwinds, so the list is never mutated mid-iteration.
    /// </summary>
    public sealed class EventService : IEventService
    {
        private static readonly object _lock = new();
        private static int _publishDepth;

        // Channels with buffered subscribe/unsubscribe ops, flushed when _publishDepth returns to 0.
        private static readonly List<IChannel> _dirtyChannels = new();
        // Every channel that has had at least one subscriber -- used by Clear() and diagnostics.
        private static readonly List<IChannel> _allChannels = new();

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        public void Subscribe<T>(Action<T> handler) where T : IGameEvent
        {
            if (handler == null)
            {
                Debug.LogWarning("[EventService] Tried to subscribe a null handler.");
                return;
            }

            lock (_lock)
            {
                Register<T>();

                if (_publishDepth > 0)
                {
                    // A publish is in flight -- buffer the change so the live list is not mutated.
                    Channel<T>.PendingRemove.Remove(handler); // cancel a queued removal, if any
                    if (!Channel<T>.Handlers.Contains(handler) && !Channel<T>.PendingAdd.Contains(handler))
                    {
                        Channel<T>.PendingAdd.Add(handler);
                        MarkDirty<T>();
                    }
                    return;
                }

                if (Channel<T>.Handlers.Contains(handler))
                {
                    Debug.LogWarning($"[EventService] Handler already subscribed to {typeof(T).Name}. " +
                                     "Duplicate subscription ignored. If using lambdas, cache them to avoid this.");
                    return;
                }

                Channel<T>.Handlers.Add(handler);
            }
        }

        public void Unsubscribe<T>(Action<T> handler) where T : IGameEvent
        {
            if (handler == null)
            {
                Debug.LogWarning("[EventService] Tried to unsubscribe a null handler.");
                return;
            }

            lock (_lock)
            {
                if (_publishDepth > 0)
                {
                    // A publish is in flight -- buffer the change.
                    Channel<T>.PendingAdd.Remove(handler); // cancel a queued add, if any
                    if (Channel<T>.Handlers.Contains(handler) && !Channel<T>.PendingRemove.Contains(handler))
                    {
                        Channel<T>.PendingRemove.Add(handler);
                        MarkDirty<T>();
                    }
                    return;
                }

                if (!Channel<T>.Handlers.Remove(handler))
                {
                    Debug.LogWarning($"[EventService] Unsubscribe called for {typeof(T).Name} but handler was not found. " +
                                     "Check for mismatched Subscribe/Unsubscribe calls.");
                }
            }
        }

        public void Publish<T>(T eventData) where T : IGameEvent
        {
            List<Action<T>> handlers = Channel<T>.Handlers;
            int count;

            lock (_lock)
            {
                count = handlers.Count;
                if (count == 0) return;
                _publishDepth++;
            }

            try
            {
                // While _publishDepth > 0 every subscribe/unsubscribe is buffered (see above),
                // so this list is frozen for the duration and can be walked directly -- no snapshot.
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        handlers[i].Invoke(eventData);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
            finally
            {
                lock (_lock)
                {
                    _publishDepth--;
                    if (_publishDepth == 0 && _dirtyChannels.Count > 0)
                        FlushPending();
                }
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                for (int i = 0; i < _allChannels.Count; i++)
                    _allChannels[i].Clear();

                _dirtyChannels.Clear();
                _publishDepth = 0;
            }
        }

        // -------------------------------------------------------------------------
        // Internals (callers below already hold _lock)
        // -------------------------------------------------------------------------

        private static void Register<T>() where T : IGameEvent
        {
            if (Channel<T>.Registered) return;
            Channel<T>.Registered = true;
            _allChannels.Add(Channel<T>.Proxy);
        }

        private static void MarkDirty<T>() where T : IGameEvent
        {
            if (Channel<T>.Dirty) return;
            Channel<T>.Dirty = true;
            _dirtyChannels.Add(Channel<T>.Proxy);
        }

        private static void FlushPending()
        {
            for (int i = 0; i < _dirtyChannels.Count; i++)
                _dirtyChannels[i].Flush();

            _dirtyChannels.Clear();
        }

        // Non-generic handle onto a Channel<T> so global operations (flush, clear, diagnostics)
        // can iterate channels without knowing their concrete event type.
        private interface IChannel
        {
            string EventName { get; }
            int HandlerCount { get; }
            void Flush();
            void Clear();
        }

        // One closed generic type per event T (guaranteed distinct by the CLR), giving each
        // event its own statically-resolved, allocation-free handler storage.
        private static class Channel<T> where T : IGameEvent
        {
            public static readonly List<Action<T>> Handlers = new();
            public static readonly List<Action<T>> PendingAdd = new();
            public static readonly List<Action<T>> PendingRemove = new();
            public static readonly IChannel Proxy = new ChannelProxy();
            public static bool Registered;
            public static bool Dirty;

            private sealed class ChannelProxy : IChannel
            {
                public string EventName => typeof(T).Name;
                public int HandlerCount => Handlers.Count;

                public void Flush()
                {
                    for (int i = 0; i < PendingAdd.Count; i++)
                    {
                        Action<T> handler = PendingAdd[i];
                        if (!Handlers.Contains(handler))
                            Handlers.Add(handler);
                    }
                    PendingAdd.Clear();

                    for (int i = 0; i < PendingRemove.Count; i++)
                        Handlers.Remove(PendingRemove[i]);
                    PendingRemove.Clear();

                    Dirty = false;
                }

                public void Clear()
                {
                    Handlers.Clear();
                    PendingAdd.Clear();
                    PendingRemove.Clear();
                    Dirty = false;
                }
            }
        }

#if UNITY_EDITOR
        public static Dictionary<string, int> GetSubscriberCounts()
        {
            lock (_lock)
            {
                var counts = new Dictionary<string, int>();
                for (int i = 0; i < _allChannels.Count; i++)
                {
                    IChannel channel = _allChannels[i];
                    if (channel.HandlerCount > 0)
                        counts[channel.EventName] = channel.HandlerCount;
                }
                return counts;
            }
        }

        public static void LogSubscriberCounts()
        {
            lock (_lock)
            {
                var sb = new System.Text.StringBuilder("[EventService] Active subscribers:\n");
                bool any = false;
                for (int i = 0; i < _allChannels.Count; i++)
                {
                    IChannel channel = _allChannels[i];
                    if (channel.HandlerCount == 0) continue;
                    any = true;
                    sb.AppendLine($"  {channel.EventName}: {channel.HandlerCount} handler(s)");
                }

                if (!any)
                {
                    Debug.Log("[EventService] No active subscribers.");
                    return;
                }

                Debug.Log(sb.ToString());
            }
        }
#endif
    }
}
