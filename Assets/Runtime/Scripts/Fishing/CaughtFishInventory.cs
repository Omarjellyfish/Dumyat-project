using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The player's catch: the real <see cref="IFishInventory"/>. Listens for
    /// <see cref="FishCaughtEvent"/> and holds every fish the player has hooked, so the merchant
    /// negotiates over an actual catch — species, weight and fair value all decided by the
    /// <see cref="Fish"/> itself — instead of the randomized
    /// <see cref="RandomFishInventoryTestDriver"/> stand-in.
    ///
    /// Drop one into any scene where fishing and selling meet, and point
    /// <c>NegotiationFlowController</c> and <c>MerchantApproachCoordinator</c> at it.
    /// </summary>
    public class CaughtFishInventory : MonoBehaviour, IFishInventory
    {
        [Header("Catalog")]
        [Tooltip("Every species this market trades. Handed to the merchant so it only acknowledges " +
                 "real fish and can reject an out-of-band weight — independent of what's been caught.")]
        [SerializeField] private FishDatabase _database;

        // The catch, oldest first. Each entry pairs the world fish with the FishData handed to the
        // negotiation; the Fish is kept so a sold fish can be removed from the world.
        private readonly List<Entry> _caught = new List<Entry>();

        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void OnEnable()
        {
            if (_eventService == null)
            {
                Debug.LogWarning($"{nameof(CaughtFishInventory)} on '{name}' has no IEventService injected — " +
                                 "caught fish will not be collected. Does this scene have a ContainerScope?", this);
                return;
            }
            _eventService.Subscribe<FishCaughtEvent>(HandleFishCaught);
        }

        private void OnDisable()
        {
            _eventService?.Unsubscribe<FishCaughtEvent>(HandleFishCaught);
        }

        /// <inheritdoc/>
        public bool HasFish
        {
            get
            {
                PruneDestroyed();
                return _caught.Count > 0;
            }
        }

        /// <inheritdoc/>
        // From the database, NOT from what's been caught: the merchant must recognise every species
        // the market trades so it can ask what the player brought.
        public IReadOnlyList<FishCatalogEntry> Catalog
        {
            get
            {
                if (_database != null) return _database.BuildCatalog();

                Debug.LogWarning($"{nameof(CaughtFishInventory)} on '{name}' has no FishDatabase assigned — " +
                                 "the merchant will negotiate without a species catalog.", this);
                return null;
            }
        }

        /// <inheritdoc/>
        // Peeks the oldest fish — the contract allows peek or consume, and consuming here would lose
        // the fish on a walked-away negotiation. MarkFishSold is what actually removes it.
        public bool TryGetFishToSell(out FishData fish)
        {
            fish = null;
            PruneDestroyed();
            if (_caught.Count == 0) return false;

            fish = _caught[0].Data;
            return fish != null;
        }

        /// <inheritdoc/>
        // The deal closed: the fish changed hands, so drop it from the catch and remove it from the
        // world. Matched by reference — Fish.ToFishData() caches its FishData, so the instance handed
        // out by TryGetFishToSell is the same one that comes back here.
        public void MarkFishSold(FishData fish)
        {
            if (fish == null) return;

            for (int i = 0; i < _caught.Count; i++)
            {
                if (!ReferenceEquals(_caught[i].Data, fish)) continue;

                Fish sold = _caught[i].Fish;
                _caught.RemoveAt(i);

                Debug.Log($"[FishInventory] Fish SOLD: {fish.fishType} {fish.weight:0.##}kg. " +
                          $"{_caught.Count} fish left in the catch.");

                if (sold != null) Destroy(sold.gameObject);
                return;
            }

            Debug.LogWarning($"[FishInventory] MarkFishSold called for {fish.fishType} " +
                             $"{fish.weight:0.##}kg but it is not in the catch.", this);
        }

        private void HandleFishCaught(FishCaughtEvent evt)
        {
            Fish fish = evt.Fish;
            if (fish == null) return;

            FishData data = fish.ToFishData();
            if (data == null) return;   // no species — Fish already warned

            _caught.Add(new Entry(fish, data));
            Debug.Log($"[FishInventory] Added {data.fishType} {data.weight:0.##}kg to the catch " +
                      $"({_caught.Count} total).");
        }

        // A fish can leave the world without being sold — it slips the hook and is destroyed, or the
        // player drops it into the sea. Drop those before reporting the catch.
        private void PruneDestroyed()
        {
            for (int i = _caught.Count - 1; i >= 0; i--)
            {
                if (_caught[i].Fish == null) _caught.RemoveAt(i);
            }
        }

        private readonly struct Entry
        {
            public readonly Fish Fish;
            public readonly FishData Data;

            public Entry(Fish fish, FishData data)
            {
                Fish = fish;
                Data = data;
            }
        }
    }
}
