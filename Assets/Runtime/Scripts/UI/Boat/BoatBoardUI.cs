using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>Which way this board sends the player.</summary>
    public enum BoatBoardMode
    {
        /// <summary>The jetty board: takes the player out to the boat.</summary>
        Board,

        /// <summary>The gunwale board: brings the player back ashore.</summary>
        GoAshore
    }

    /// <summary>
    /// A world-space board the player uses to get on or off the fishing boat.
    ///
    /// One component and one UXML serve both boards, differing only in <see cref="_mode"/> and the
    /// strings beside it — two panels would mean two places to fix the day the treatment changes.
    /// Boarding used to be done by grabbing the father, which nothing on screen ever mentioned.
    ///
    /// It does not teleport anything itself: <see cref="BoatTransit"/> owns the move, the fade, the
    /// movement lock and the narrative together, and this only asks.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BoatBoardUI : MonoBehaviour
    {
        // Element names in BoatBoard.uxml.
        private const string RootName = "boat-board";
        private const string EyebrowName = "board-eyebrow";
        private const string TitleName = "board-title";
        private const string BodyName = "board-body";
        private const string ReadoutsName = "board-readouts";
        private const string CatchLabelName = "board-catch-label";
        private const string CatchValueName = "board-catch-value";
        private const string WeightLabelName = "board-weight-label";
        private const string WeightValueName = "board-weight-value";
        private const string ActionName = "board-action";

        [Header("Wiring")]
        [Tooltip("The transit that actually moves the player. Auto-found in the scene if left empty.")]
        [SerializeField] private BoatTransit _transit;

        [Tooltip("Which way this board sends the player.")]
        [SerializeField] private BoatBoardMode _mode = BoatBoardMode.Board;

        [Tooltip("Tally of what has been caught this trip, for the readouts. Auto-found in the scene " +
                 "if left empty.")]
        [SerializeField] private BoatTripCatchLog _catchLog;

        [Header("Copy — English")]
        [SerializeField] private string _eyebrow = "THE JETTY";
        [SerializeField] private string _title = "Out to the water";
        [SerializeField, TextArea(2, 4)] private string _body =
            "The boat is moored and your father is waiting. Take the rod with you — once you are " +
            "aboard you fish from where you stand.";
        [SerializeField] private string _actionText = "BOARD THE BOAT";

        [Header("Copy — Arabic")]
        [SerializeField] private string _eyebrowArabic = "الشط";
        [SerializeField] private string _titleArabic = "اطلع المركب";
        [SerializeField, TextArea(2, 4)] private string _bodyArabic =
            "المركب راسية وأبوك مستنيك. خد السنارة معاك — أول ما تطلع هتصطاد من مكانك.";
        [SerializeField] private string _actionTextArabic = "اطلع المركب";

        // A second line showing the OTHER language used to sit under the title. It is gone, and the
        // panel now follows the language setting like every other panel in the project. Arabic needs
        // UI Toolkit's advanced text generator to shape and join, and UiLanguage.ApplyScript switches
        // that on for the whole panel according to the CHOSEN language — so a lone Arabic label inside
        // an English panel is precisely the case it turns shaping off for, and it rendered as
        // disconnected left-to-right letterforms.

        private UIDocument _document;
        private VisualElement _root;
        private Label _eyebrowLabel;
        private Label _titleLabel;
        private Label _bodyLabel;
        private VisualElement _readouts;
        private Label _catchLabel;
        private Label _catchValue;
        private Label _weightLabel;
        private Label _weightValue;
        private Button _action;

        private IEventService _eventService;
        private ISettingsService _settings;

        [Inject]
        private void Inject(IEventService eventService, ISettingsService settings)
        {
            _eventService = eventService;
            _settings = settings;
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            if (_transit == null) _transit = FindAnyObjectByType<BoatTransit>();
            if (_catchLog == null) _catchLog = FindAnyObjectByType<BoatTripCatchLog>();

            if (_transit == null)
                Debug.LogWarning($"{nameof(BoatBoardUI)} on '{name}' found no {nameof(BoatTransit)} — " +
                                 "the board will draw but do nothing.", this);
        }

        private void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;
            _root = root?.Q<VisualElement>(RootName);

            if (_root == null)
            {
                Debug.LogWarning($"{nameof(BoatBoardUI)} on '{name}': '{RootName}' not found — is " +
                                 "BoatBoard.uxml assigned as the UIDocument's Source Asset?", this);
                return;
            }

            _eyebrowLabel = root.Q<Label>(EyebrowName);
            _titleLabel = root.Q<Label>(TitleName);
            _bodyLabel = root.Q<Label>(BodyName);
            _readouts = root.Q<VisualElement>(ReadoutsName);
            _catchLabel = root.Q<Label>(CatchLabelName);
            _catchValue = root.Q<Label>(CatchValueName);
            _weightLabel = root.Q<Label>(WeightLabelName);
            _weightValue = root.Q<Label>(WeightValueName);
            _action = root.Q<Button>(ActionName);

            if (_action != null) _action.clicked += OnActionClicked;

            Paint();
            Subscribe();
        }

        private void OnDisable()
        {
            if (_action != null) _action.clicked -= OnActionClicked;
            Unsubscribe();
        }

        // Reflex injects scene objects from ContainerScope.Awake, which can run after another object's
        // OnEnable — so the subscription is retried here, idempotently.
        private void Start() => Subscribe();

        private void Subscribe()
        {
            if (_eventService == null) return;

            Unsubscribe();

            _eventService.Subscribe<BoatBoardedEvent>(OnBoarded);
            _eventService.Subscribe<BoatLeftEvent>(OnLeft);
            _eventService.Subscribe<FishCaughtEvent>(OnFishCaught);
        }

        private void Unsubscribe()
        {
            if (_eventService == null) return;

            _eventService.Unsubscribe<BoatBoardedEvent>(OnBoarded);
            _eventService.Unsubscribe<BoatLeftEvent>(OnLeft);
            _eventService.Unsubscribe<FishCaughtEvent>(OnFishCaught);
        }

        private void OnBoarded(BoatBoardedEvent evt) => RefreshReadouts();

        private void OnLeft(BoatLeftEvent evt) => RefreshReadouts();

        private void OnFishCaught(FishCaughtEvent evt) => RefreshReadouts();

        private void OnActionClicked()
        {
            if (_transit == null) return;

            if (_mode == BoatBoardMode.Board) _transit.Board();
            else _transit.LeaveToShore();
        }

        // NOTHING here hides the board, and nothing touches its collider.
        //
        // Both were mistakes. Hiding the jetty board until the father finished his walk stranded anyone
        // who skipped the tutorial — no way onto the boat at all. And gating the collider meant this
        // component was toggling `enabled` on a collider that UIDocument's Auto collider mode owns and
        // rebuilds itself, every LateUpdate — fighting the very thing that makes a world-space panel
        // pointable. TravelUI, the bus board the player clicks in this same scene, does neither: it is
        // simply always on. This now matches it exactly.
        //
        // Both boards being permanently visible is safe: they stand 34 m apart, and a press on the wrong
        // one is already a no-op — BoatTransit.Board() returns early when aboard, LeaveToShore() when
        // ashore.

        private void Paint()
        {
            bool arabic = UiLanguage.IsArabic(_settings);

            SetText(_eyebrowLabel, UiLanguage.Pick(arabic, _eyebrow, _eyebrowArabic));
            SetText(_titleLabel, UiLanguage.Pick(arabic, _title, _titleArabic));
            SetText(_bodyLabel, UiLanguage.Pick(arabic, _body, _bodyArabic));
            SetText(_action, UiLanguage.Pick(arabic, _actionText, _actionTextArabic));

            // Only the gunwale board carries readouts. On the jetty nothing has been caught yet, and a
            // row of zeroes is decoration.
            if (_readouts != null)
                _readouts.style.display = _mode == BoatBoardMode.GoAshore ? DisplayStyle.Flex : DisplayStyle.None;

            SetText(_catchLabel, arabic ? "صيد النهاردة" : "TODAY'S CATCH");
            SetText(_weightLabel, arabic ? "الوزن" : "WEIGHT");

            RefreshReadouts();

            // After every string is written: elements keep the class only if they had it when applied.
            UiLanguage.ApplyScript(_root, arabic);
        }

        private void RefreshReadouts()
        {
            if (_mode != BoatBoardMode.GoAshore) return;
            if (_catchValue == null && _weightValue == null) return;

            int count = _catchLog != null ? _catchLog.Count : 0;
            float weight = _catchLog != null ? _catchLog.TotalWeightKg : 0f;

            SetText(_catchValue, count.ToString());
            SetText(_weightValue, $"{weight:0.00} kg");
        }

        private static void SetText(TextElement element, string text)
        {
            if (element != null) element.text = text;
        }
    }
}
