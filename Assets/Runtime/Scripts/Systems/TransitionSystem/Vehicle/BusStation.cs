using System;
using System.Collections.Generic;
using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Placed at each bus station in every scene. Holds a list of available
    /// <see cref="TravelDestination"/> options. When the player selects one,
    /// it triggers a fade transition to the destination scene.
    ///
    /// The list is authored ONCE, on the BusStop prefab, and carries every town on the line —
    /// including the one this stop stands in. The stop's own scene is dropped at runtime by
    /// <see cref="AvailableDestinations"/>, so every stop offers every other town and none of them
    /// sells a ticket to where the player already is. Do not re-cut the list per scene: a hand-edited
    /// copy on each stop is how three of them ended up missing routes and one offered itself.
    /// </summary>
    public class BusStation : MonoBehaviour
    {
        [Header("Station Config")]
        [Tooltip("Every destination on the line, including this stop's own scene — it is filtered " +
                 "out at runtime. Leave this as the prefab authored it rather than overriding it " +
                 "per scene.")]
        [SerializeField] private List<TravelDestination> _destinations;
        [SerializeField] private List<DialogueSequenceSO> _transitionVoiceLines;

        /// <summary><see cref="_destinations"/> minus this stop's own scene. Built once in Awake.</summary>
        private readonly List<TravelDestination> _available = new List<TravelDestination>();

        private IFadeTransitionService _fadeTransition;
        private INarrativeService _narrativeService;

        [Inject]
        private void Inject(IFadeTransitionService fadeTransition, INarrativeService narrativeService)
        {
            _fadeTransition = fadeTransition;
            _narrativeService = narrativeService;
        }

        private void Awake()
        {
            RebuildAvailableDestinations();
        }

        /// <summary>
        /// The full authored list, self-scene included. Prefer <see cref="AvailableDestinations"/> for
        /// anything the player sees.
        /// </summary>
        public IReadOnlyList<TravelDestination> Destinations => _destinations;

        /// <summary>
        /// The destinations this station will actually sell — every authored route except the one
        /// leading back into the scene the station itself lives in. This is what a UI should list.
        /// </summary>
        public IReadOnlyList<TravelDestination> AvailableDestinations => _available;

        /// <summary>
        /// Whether <paramref name="destination"/> points at the scene this station stands in.
        ///
        /// Compared against the station's OWN scene rather than the active scene, so an additively
        /// loaded stop still answers for the town it belongs to. Case-insensitive because the scene
        /// name is typed by hand into the <see cref="TravelDestination"/> asset.
        /// </summary>
        private bool IsCurrentScene(TravelDestination destination)
        {
            return destination != null &&
                   !string.IsNullOrEmpty(destination.SceneName) &&
                   string.Equals(destination.SceneName, gameObject.scene.name,
                                 StringComparison.OrdinalIgnoreCase);
        }

        private void RebuildAvailableDestinations()
        {
            _available.Clear();

            if (_destinations == null) return;

            foreach (var destination in _destinations)
            {
                if (destination == null || IsCurrentScene(destination)) continue;

                _available.Add(destination);
            }
        }

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

            // Backstop for callers that did not go through AvailableDestinations (TravelToIndex, an
            // Inspector-wired UnityEvent). Reloading the scene the player is standing in costs them a
            // full blackout and drops them back where they started, which reads as the bus breaking.
            if (IsCurrentScene(destination))
            {
                Debug.LogWarning($"BusStation: '{destination.SceneName}' is the scene this stop is in — " +
                                 "refusing to travel to it.");
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
                    // Qualified: `using System` above puts System.Random in scope alongside this one.
                    int randomIndex = UnityEngine.Random.Range(0, _transitionVoiceLines.Count);
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
