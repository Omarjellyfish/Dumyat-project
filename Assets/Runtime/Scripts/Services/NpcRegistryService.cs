using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Dictionary-backed registry for active NPC actors. Plain C# service constructed by Reflex.
    /// Same lifetime pattern as <see cref="EventService"/>.
    /// NPCs register on spawn (OnEnable) and unregister on disable (OnDisable),
    /// so stale entries are automatically cleaned up across scene transitions.
    /// </summary>
    public sealed class NpcRegistryService : INpcRegistryService
    {
        private readonly Dictionary<string, INpcActor> _actors = new();

        public void Register(string characterId, INpcActor actor)
        {
            if (string.IsNullOrEmpty(characterId) || actor == null) return;

            if (_actors.ContainsKey(characterId))
            {
                Debug.LogWarning($"[NpcRegistry] Overwriting existing actor for '{characterId}'.");
            }
            _actors[characterId] = actor;
        }

        public void Unregister(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return;
            _actors.Remove(characterId);
        }

        public INpcActor GetActor(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            _actors.TryGetValue(characterId, out var actor);
            return actor;
        }
    }
}
