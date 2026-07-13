using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Placed at each bus station in every scene. Holds a list of available
    /// <see cref="TravelDestination"/> options. When the player selects one,
    /// this component finds the persistent car and tells it to pick them up.
    /// </summary>
    public class BusStation : MonoBehaviour
    {
        [Header("Station Config")]
        [SerializeField] private Transform _pickupPoint;
        [SerializeField] private List<TravelDestination> _destinations;

        [Header("Tags")]
        [SerializeField] private string _playerTag = "Player";
        [SerializeField] private string _carTag = "PlayerCar";

        /// <summary>
        /// Returns the list of available destinations from this station.
        /// Use this to populate a UI selection panel.
        /// </summary>
        public IReadOnlyList<TravelDestination> Destinations => _destinations;

        /// <summary>
        /// Called when the player picks a destination from the list.
        /// Hook this up from your UI after the player makes a selection.
        /// </summary>
        public void TravelTo(TravelDestination destination)
        {
            if (destination == null)
            {
                Debug.LogWarning("BusStation: No destination selected.");
                return;
            }

            GameObject playerObj = GameObject.FindWithTag(_playerTag);
            GameObject carObj = GameObject.FindWithTag(_carTag);

            if (playerObj == null)
            {
                Debug.LogWarning("BusStation: No GameObject with tag 'Player' found.");
                return;
            }

            if (carObj == null)
            {
                Debug.LogWarning("BusStation: No GameObject with tag 'PlayerCar' found.");
                return;
            }

            var carHandler = carObj.GetComponent<CarTransitionHandler>();
            if (carHandler == null)
            {
                Debug.LogWarning("BusStation: Car is missing CarTransitionHandler component.");
                return;
            }

            carHandler.RequestPickup(_pickupPoint, playerObj.transform, destination.SceneName);
        }

        /// <summary>
        /// Convenience method for quick testing with a destination index.
        /// </summary>
        public void TravelToIndex(int index)
        {
            if (index < 0 || index >= _destinations.Count)
            {
                Debug.LogWarning($"BusStation: Invalid destination index {index}.");
                return;
            }

            TravelTo(_destinations[index]);
        }
    }
}
