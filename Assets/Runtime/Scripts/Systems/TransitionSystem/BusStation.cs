using System.Collections.Generic;
using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Placed at each bus station in every scene. Holds a list of available
    /// <see cref="TravelDestination"/> options. When the player selects one,
    /// it triggers a fade transition to the destination scene.
    /// </summary>
    public class BusStation : MonoBehaviour
    {
        [Header("Station Config")]
        [SerializeField] private List<TravelDestination> _destinations;
        [SerializeField] private List<DialogueSequenceSO> _transitionVoiceLines;

        private IFadeTransitionService _fadeTransition;
        private INarrativeService _narrativeService;

        [Inject]
        private void Inject(IFadeTransitionService fadeTransition, INarrativeService narrativeService)
        {
            _fadeTransition = fadeTransition;
            _narrativeService = narrativeService;
        }

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

            if (_fadeTransition == null)
            {
                Debug.LogError("BusStation: IFadeTransitionService not injected.");
                return;
            }

            if (_narrativeService != null)
            {
                _narrativeService.CancelCurrent();

                if (_transitionVoiceLines != null && _transitionVoiceLines.Count > 0)
                {
                    int randomIndex = Random.Range(0, _transitionVoiceLines.Count);
                    var selectedLine = _transitionVoiceLines[randomIndex];
                    if (selectedLine != null)
                    {
                        _narrativeService.PlayDialogue(selectedLine, DialoguePriority.High);
                    }
                }
            }

            _fadeTransition.StartTransition(destination.SceneName);
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
