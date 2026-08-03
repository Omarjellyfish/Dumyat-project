using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// UI Toolkit prompt for the microphone + internet consent required by the AI merchant
    /// pipeline. Listens for <see cref="ConsentManager.OnConsentUIRequested"/> (fired by
    /// <see cref="ConsentManager.RequestConsent"/>) and can also show itself on startup when
    /// the player has never been asked. The player's choice is reported back through
    /// <see cref="ConsentManager.SubmitConsentResult"/>, which persists it and completes any
    /// pending request callback.
    ///
    /// Expects the ConsentPrompt.uxml layout on the attached <see cref="UIDocument"/>:
    /// a "consent-panel" root, "mic-toggle"/"internet-toggle" toggles and
    /// "confirm-button"/"decline-button" buttons.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ConsentPromptUI : MonoBehaviour
    {
        // Element names in ConsentPrompt.uxml.
        private const string PanelName = "consent-panel";
        private const string MicToggleName = "mic-toggle";
        private const string InternetToggleName = "internet-toggle";
        private const string ConfirmButtonName = "confirm-button";
        private const string DeclineButtonName = "decline-button";

        [Tooltip("Show the prompt automatically on startup when the player has never been asked " +
                 "(either consent still NotAsked). Leave on so consent is settled before the first " +
                 "merchant approach instead of mid-negotiation.")]
        [SerializeField] private bool _promptOnStartWhenNotAsked = true;

        // Injected consent service (replaces the former _consent).
        private IConsentService _consent;

        [Inject]
        private void Inject(IConsentService consent) => _consent = consent;

        private UIDocument _document;
        private VisualElement _panel;
        private Toggle _micToggle;
        private Toggle _internetToggle;
        private Button _confirmButton;
        private Button _declineButton;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;
            _panel = root?.Q<VisualElement>(PanelName);
            if (_panel == null)
            {
                Debug.LogWarning("ConsentPromptUI: 'consent-panel' not found — is ConsentPrompt.uxml " +
                                 "assigned as the UIDocument's Source Asset?");
                return;
            }

            _micToggle = root.Q<Toggle>(MicToggleName);
            _internetToggle = root.Q<Toggle>(InternetToggleName);
            _confirmButton = root.Q<Button>(ConfirmButtonName);
            _declineButton = root.Q<Button>(DeclineButtonName);

            if (_confirmButton != null) _confirmButton.clicked += HandleConfirm;
            if (_declineButton != null) _declineButton.clicked += HandleDeclineAll;

            // Consent is all-or-nothing (voice mode needs BOTH mic and internet): Confirm is
            // only clickable when both toggles are on; the only other choice is Decline All.
            if (_micToggle != null) _micToggle.RegisterValueChangedCallback(HandleToggleChanged);
            if (_internetToggle != null) _internetToggle.RegisterValueChangedCallback(HandleToggleChanged);

            Hide();

            // Awake/OnEnable order across objects is nondeterministic, so the manager may not
            // exist yet here — Start() (guaranteed after every Awake) retries the subscription.
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (_consent != null)
                _consent.OnConsentUIRequested -= Show;

            if (_confirmButton != null) _confirmButton.clicked -= HandleConfirm;
            if (_declineButton != null) _declineButton.clicked -= HandleDeclineAll;

            if (_micToggle != null) _micToggle.UnregisterValueChangedCallback(HandleToggleChanged);
            if (_internetToggle != null) _internetToggle.UnregisterValueChangedCallback(HandleToggleChanged);
        }

        private void Start()
        {
            TrySubscribe();
            if (_consent == null)
            {
                Debug.LogWarning("ConsentPromptUI: no ConsentManager in scene; prompt will never show.");
                return;
            }

            if (!_promptOnStartWhenNotAsked || _panel == null) return;

            ConsentState state = _consent.GetConsentStatus();
            if (state.MicConsent == ConsentStatus.NotAsked || state.InternetConsent == ConsentStatus.NotAsked)
                Show();
        }

        // Idempotent subscribe (remove-then-add) so OnEnable and Start can both call it safely.
        private void TrySubscribe()
        {
            if (_consent == null) return;
            _consent.OnConsentUIRequested -= Show;
            _consent.OnConsentUIRequested += Show;
        }

        /// <summary>Shows the prompt, pre-checking the toggles from the current consent state.</summary>
        public void Show()
        {
            if (_panel == null) return;

            // Previously-declined players see their choice reflected; first-timers default to on.
            if (_consent != null)
            {
                ConsentState state = _consent.GetConsentStatus();
                if (_micToggle != null) _micToggle.value = state.MicConsent != ConsentStatus.Declined;
                if (_internetToggle != null) _internetToggle.value = state.InternetConsent != ConsentStatus.Declined;
            }

            _panel.style.display = DisplayStyle.Flex;
            RefreshConfirmEnabled();
        }

        private void Hide()
        {
            if (_panel != null) _panel.style.display = DisplayStyle.None;
        }

        private bool BothToggledOn() =>
            _micToggle != null && _micToggle.value && _internetToggle != null && _internetToggle.value;

        private void HandleToggleChanged(ChangeEvent<bool> _) => RefreshConfirmEnabled();

        private void RefreshConfirmEnabled() => _confirmButton?.SetEnabled(BothToggledOn());

        private void HandleConfirm()
        {
            if (!BothToggledOn()) return; // button is disabled in this state; belt-and-suspenders
            Submit(ConsentStatus.Granted, ConsentStatus.Granted);
        }

        private void HandleDeclineAll()
        {
            Submit(ConsentStatus.Declined, ConsentStatus.Declined);
        }

        private void Submit(ConsentStatus mic, ConsentStatus internet)
        {
            Hide();

            if (_consent == null)
            {
                Debug.LogWarning("ConsentPromptUI: consent chosen but no ConsentManager to receive it.");
                return;
            }

            _consent.SubmitConsentResult(mic, internet);
            Debug.Log($"ConsentPromptUI: player chose mic={mic}, internet={internet}.");
        }
    }
}
