using UnityEngine;
using UnityEngine.UIElements;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// World-space UI panel at a bus station that shows available destinations.
    /// The player selects a destination card and the station dispatches the car.
    /// Built with UI Toolkit, following the same pattern as <see cref="CraftingStationMenuUI"/>.
    /// </summary>
    public class DestinationBoardUI : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private BusStation _station;

        private ScrollView _destinationContainer;
        private Label _headerLabel;
        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void OnEnable()
        {
            if (_uiDocument == null)
            {
                _uiDocument = GetComponent<UIDocument>();
            }

            if (_uiDocument != null && _uiDocument.rootVisualElement != null)
            {
                var root = _uiDocument.rootVisualElement;
                _destinationContainer = root.Q<ScrollView>("DestinationContainer");
                _headerLabel = root.Q<Label>("HeaderLabel");
            }

            if (_eventService != null)
                _eventService.Subscribe<SceneLoadBeginEvent>(OnSceneLoadBegin);
        }

        private void OnDisable()
        {
            if (_eventService != null)
                _eventService.Unsubscribe<SceneLoadBeginEvent>(OnSceneLoadBegin);
        }

        private void Start()
        {
            PopulateDestinations();
        }

        private void PopulateDestinations()
        {
            if (_destinationContainer == null || _station == null) return;

            _destinationContainer.Clear();

            var destinations = _station.Destinations;

            if (destinations == null || destinations.Count == 0)
            {
                var emptyLabel = new Label("No destinations available.");
                emptyLabel.AddToClassList("destination-empty");
                _destinationContainer.Add(emptyLabel);
                return;
            }

            foreach (var destination in destinations)
            {
                if (destination == null) continue;
                _destinationContainer.Add(CreateDestinationCard(destination));
            }
        }

        private VisualElement CreateDestinationCard(TravelDestination destination)
        {
            var card = new VisualElement();
            card.AddToClassList("destination-card");

            // Destination name
            var nameLabel = new Label(destination.DisplayName);
            nameLabel.AddToClassList("destination-name");
            card.Add(nameLabel);

            // Description (if provided)
            if (!string.IsNullOrEmpty(destination.Description))
            {
                var descLabel = new Label(destination.Description);
                descLabel.AddToClassList("destination-description");
                card.Add(descLabel);
            }

            // Travel button
            var travelButton = new Button(() => OnTravelClicked(destination));
            travelButton.text = "Travel";
            travelButton.AddToClassList("destination-travel-btn");
            card.Add(travelButton);

            return card;
        }

        private void OnTravelClicked(TravelDestination destination)
        {
            _station.TravelTo(destination);
        }

        private void OnSceneLoadBegin(SceneLoadBeginEvent evt)
        {
            // Disable interaction while a scene is loading
            if (_uiDocument != null && _uiDocument.rootVisualElement != null)
            {
                _uiDocument.rootVisualElement.SetEnabled(false);
            }
        }
    }
}
