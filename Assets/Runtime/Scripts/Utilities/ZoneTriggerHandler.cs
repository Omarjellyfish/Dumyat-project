using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public class ZoneTriggerHandler : MonoBehaviour
    {
        [Header("Target Objects")]
        [SerializeField] private List<GameObject> objectsToToggle = new List<GameObject>();

        [Header("Collision Settings")]
        [Tooltip("Specify the tag of your VR player object (e.g., 'Player')")]
        [SerializeField] private string playerTag = "Player";

        private void OnEnable()
        {
            // Ensure all objects are inactive at the start
            SetObjectsState(false);
        }

        // Triggered when the player enters the zone
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(playerTag))
            {
                SetObjectsState(true);
            }
        }

        // Triggered when the player leaves the zone
        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag(playerTag))
            {
                SetObjectsState(false);
            }
        }

        // Helper method to loop through the list and change active state
        private void SetObjectsState(bool state)
        {
            foreach (GameObject obj in objectsToToggle)
            {
                if (obj != null)
                {
                    obj.SetActive(state);
                }
            }
        }
    }
}
