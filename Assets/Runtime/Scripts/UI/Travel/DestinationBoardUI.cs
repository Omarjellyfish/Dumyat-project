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

        /// <summary>Dots down the stub's perforated edge. Fixed count — the edge is decorative.</summary>
        private const int PerforationDots = 6;

        // Board chrome in both languages. Destination names and descriptions are content and come
        // from the TravelDestination assets, not from here.
        private const string EyebrowEn = "DAMIETTA · LOCAL SERVICE";
        private const string EyebrowAr = "دمياط · خط محلي";
        private const string HeaderEn = "DEPARTURES";
        private const string HeaderAr = "الرحلات";
        private const string RoutesEn = "ROUTES";
        private const string RoutesAr = "الخطوط";
        private const string TravelEn = "TRAVEL";
        private const string TravelAr = "سافر";
        private const string EmptyEn = "No destinations available.";
        private const string EmptyAr = "مفيش وجهات متاحة دلوقتي.";

        private VisualElement _root;
        private ScrollView _destinationContainer;
        private Label _headerLabel;
        private Label _eyebrowLabel;
        private Label _routesCaption;
        private Label _routeCountLabel;
        private IEventService _eventService;
        private IPlayerPersistenceService _persistenceService;
        private ISettingsService _settings;

        /// <summary>Whether the board is currently drawn in Arabic.</summary>
        private bool Arabic => UiLanguage.IsArabic(_settings);

        [Inject]
        private void Inject(IEventService eventService, IPlayerPersistenceService persistenceService,
                            ISettingsService settings)
        {
            _eventService = eventService;
            _persistenceService = persistenceService;
            _settings = settings;
        }

        private void OnEnable()
        {
            if (_uiDocument == null)
            {
                _uiDocument = GetComponent<UIDocument>();
            }

            if (_uiDocument != null && _uiDocument.rootVisualElement != null)
            {
                _root = _uiDocument.rootVisualElement;
                _destinationContainer = _root.Q<ScrollView>("DestinationContainer");
                _headerLabel = _root.Q<Label>("HeaderLabel");
                _eyebrowLabel = _root.Q<Label>("BoardEyebrow");
                _routesCaption = _root.Q<Label>("RouteCountLabel");
                _routeCountLabel = _root.Q<Label>("RouteCount");
            }

            if (_eventService != null)
            {
                _eventService.Subscribe<SceneLoadBeginEvent>(OnSceneLoadBegin);
                _eventService.Subscribe<KeyUnlockedEvent>(OnKeyUnlocked);
                _eventService.Subscribe<LanguageChangedEvent>(OnLanguageChanged);
            }
        }

        private void OnDisable()
        {
            if (_eventService != null)
            {
                _eventService.Unsubscribe<SceneLoadBeginEvent>(OnSceneLoadBegin);
                _eventService.Unsubscribe<KeyUnlockedEvent>(OnKeyUnlocked);
                _eventService.Unsubscribe<LanguageChangedEvent>(OnLanguageChanged);
            }
        }

        /// <summary>
        /// Redraws the board in the newly chosen language. A full repopulate rather than a string
        /// patch: every stub is built in C#, so rebuilding is the only way to be certain no card
        /// keeps the previous language.
        /// </summary>
        private void OnLanguageChanged(LanguageChangedEvent evt)
        {
            PopulateDestinations();
        }

        /// <summary>Fills the strings authored in the UXML from the current language.</summary>
        private void ApplyStaticLanguage()
        {
            bool ar = Arabic;

            if (_eyebrowLabel != null) _eyebrowLabel.text = ar ? EyebrowAr : EyebrowEn;
            if (_headerLabel != null) _headerLabel.text = ar ? HeaderAr : HeaderEn;
            if (_routesCaption != null) _routesCaption.text = ar ? RoutesAr : RoutesEn;
        }

        private void OnKeyUnlocked(KeyUnlockedEvent evt)
        {
            PopulateDestinations();
        }

        private void Start()
        {
            PopulateDestinations();
        }

        private void PopulateDestinations()
        {
            if (_destinationContainer == null || _station == null) return;

            _destinationContainer.Clear();
            ApplyStaticLanguage();

            // AvailableDestinations, not Destinations: the station drops the route back into its own
            // scene, so the board never offers the town the player is standing in.
            var destinations = _station.AvailableDestinations;

            if (destinations == null || destinations.Count == 0)
            {
                var emptyLabel = new Label(Arabic ? EmptyAr : EmptyEn);
                emptyLabel.AddToClassList("destination-empty");
                _destinationContainer.Add(emptyLabel);
                UpdateRouteCount(0);
                UiLanguage.ApplyScript(_root, Arabic);
                return;
            }

            foreach (var destination in destinations)
            {
                if (destination == null) continue;

                if (!string.IsNullOrEmpty(destination.RequiredPersistenceKey) && 
                    (_persistenceService == null || !_persistenceService.HasKey(destination.RequiredPersistenceKey)))
                {
                    continue; // Skip this destination since the player doesn't have the required key
                }

                _destinationContainer.Add(CreateDestinationCard(destination));
            }

            // Counted before the empty-state label goes in, or the placeholder would read as a route.
            int routeCount = _destinationContainer.childCount;

            if (routeCount == 0)
            {
                var emptyLabel = new Label(Arabic ? EmptyAr : EmptyEn);
                emptyLabel.AddToClassList("destination-empty");
                _destinationContainer.Add(emptyLabel);
            }

            UpdateRouteCount(routeCount);

            // Last, so every stub built above is covered.
            UiLanguage.ApplyScript(_root, Arabic);
        }

        /// <summary>
        /// Fills the header readout with however many routes survived the key filter. Display only —
        /// null-guarded so the board still works against a UXML without the RouteCount label.
        /// </summary>
        private void UpdateRouteCount(int routeCount)
        {
            if (_routeCountLabel == null) return;

            _routeCountLabel.text = routeCount.ToString();
        }

        private VisualElement CreateDestinationCard(TravelDestination destination)
        {
            var card = new VisualElement();
            card.AddToClassList("destination-card");

            // Perforated edge — the stub reads as a torn-off ticket rather than a list row.
            card.Add(CreatePerforation());

            // Route code cell. Gives each route a short handle the player can learn, so after a few
            // trips they point at the code instead of re-reading four descriptions. Skipped entirely
            // when a destination has no code, and the stub still works without it.
            if (!string.IsNullOrEmpty(destination.RouteCode))
            {
                var codeCell = new VisualElement();
                codeCell.AddToClassList("destination-code");

                var codeLabel = new Label(destination.RouteCode);
                codeLabel.AddToClassList("destination-code-label");
                codeCell.Add(codeLabel);

                card.Add(codeCell);
            }

            // Name and description share a column so the Travel button can hold a fixed slot on the
            // right — every route's action then lands in the same place, which matters when you are
            // pointing a controller ray at it.
            var textColumn = new VisualElement();
            textColumn.AddToClassList("destination-text");

            bool ar = Arabic;

            // Destination name. Falls back to the English field when a stop has no Arabic name yet,
            // so a part-translated database still lists every route.
            var nameLabel = new Label(UiLanguage.Pick(ar, destination.DisplayName, destination.DisplayNameArabic));
            nameLabel.AddToClassList("destination-name");
            textColumn.Add(nameLabel);

            // Description (if provided)
            string description = UiLanguage.Pick(ar, destination.Description, destination.DescriptionArabic);
            if (!string.IsNullOrEmpty(description))
            {
                var descLabel = new Label(description);
                descLabel.AddToClassList("destination-description");
                textColumn.Add(descLabel);
            }

            card.Add(textColumn);

            // Travel button. Authored uppercase — UI Toolkit has no text-transform.
            var travelButton = new Button(() => OnTravelClicked(destination));
            travelButton.text = ar ? TravelAr : TravelEn;
            travelButton.AddToClassList("destination-travel-btn");
            card.Add(travelButton);

            return card;
        }

        /// <summary>
        /// The column of dots down the stub's left edge. Purely decorative, so it is ray-transparent
        /// and never competes with the Travel button for a controller ray.
        /// </summary>
        private VisualElement CreatePerforation()
        {
            var perforation = new VisualElement();
            perforation.AddToClassList("destination-perforation");
            perforation.pickingMode = PickingMode.Ignore;

            for (int i = 0; i < PerforationDots; i++)
            {
                var dot = new VisualElement();
                dot.AddToClassList("destination-perforation-dot");
                perforation.Add(dot);
            }

            return perforation;
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
