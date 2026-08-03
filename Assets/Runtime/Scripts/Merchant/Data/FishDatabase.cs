using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Every fish species the El Borg market trades, authored as <see cref="FishSpeciesSO"/> assets.
    /// This is the single source of truth for the market catalog: <see cref="CaughtFishInventory"/>
    /// hands <see cref="BuildCatalog"/> to the negotiation so the merchant only acknowledges species
    /// that actually exist here and can reject an out-of-band weight.
    ///
    /// Mirrors the <see cref="ItemDatabase"/> / <see cref="RecipeDatabase"/> pattern.
    /// </summary>
    [CreateAssetMenu(fileName = "FishDatabase", menuName = "Dumyat/Fish/Database")]
    public class FishDatabase : ScriptableObject
    {
        [Tooltip("Species this market trades. Assign the FishSpeciesSO assets under Assets/Runtime/Content/Fish.")]
        [SerializeField] private List<FishSpeciesSO> _allSpecies = new List<FishSpeciesSO>();

        // Built once on first request and reused: the catalog is asked for on every negotiation and
        // its contents never change at runtime, so there is no reason to reallocate it per get.
        private List<FishCatalogEntry> _catalog;

        public IReadOnlyList<FishSpeciesSO> AllSpecies => _allSpecies;

        /// <summary>
        /// Looks up a species by its transliterated name (e.g. "Bouri"). Case-insensitive.
        /// Returns null (with a warning) when the species isn't in this database.
        /// </summary>
        public FishSpeciesSO GetSpeciesByType(string fishType)
        {
            if (string.IsNullOrWhiteSpace(fishType)) return null;

            foreach (FishSpeciesSO species in _allSpecies)
            {
                if (species != null && string.Equals(species.FishType, fishType,
                        System.StringComparison.OrdinalIgnoreCase))
                    return species;
            }

            Debug.LogWarning($"Fish species '{fishType}' not found in the database!");
            return null;
        }

        /// <summary>
        /// The market catalog: one entry per valid species (name + weight band). Cached — callers
        /// must treat the result as read-only. Null/unnamed species entries are skipped.
        /// </summary>
        public IReadOnlyList<FishCatalogEntry> BuildCatalog()
        {
            if (_catalog != null) return _catalog;

            _catalog = new List<FishCatalogEntry>(_allSpecies.Count);
            foreach (FishSpeciesSO species in _allSpecies)
            {
                if (species == null || string.IsNullOrWhiteSpace(species.FishType)) continue;
                _catalog.Add(species.ToCatalogEntry());
            }
            return _catalog;
        }

        // A domain reload keeps the SO alive but the designer may have edited the species list, so
        // drop the cache when the asset changes in the Editor.
#if UNITY_EDITOR
        private void OnValidate() => _catalog = null;
#endif
    }
}
