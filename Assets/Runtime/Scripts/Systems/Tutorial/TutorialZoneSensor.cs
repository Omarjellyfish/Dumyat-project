using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Lightweight trigger helper placed on a zone collider by <see cref="TutorialRunner"/>.
    /// Fires a callback when the player enters the trigger. Only active when explicitly
    /// activated by the runner, preventing false positives.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TutorialZoneSensor : MonoBehaviour
    {
        private Action _onEntered;
        private bool _isActive;

        /// <summary>Arm the sensor with a callback. Only triggers once per activation.</summary>
        public void Activate(Action onEntered)
        {
            _onEntered = onEntered;
            _isActive = true;
        }

        /// <summary>Disarm the sensor.</summary>
        public void Deactivate()
        {
            _isActive = false;
            _onEntered = null;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_isActive) return;

            // Only trigger for the player (assumes the player root has the "Player" tag)
            if (!other.CompareTag("Player")) return;

            _isActive = false;
            _onEntered?.Invoke();
        }
    }
}
