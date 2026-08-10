using System;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Drives the shared SettingsPage template: audio levels, interface language, merchant
    /// subtitles, and a read-only controls image. Lives on the same GameObject as the main menu or
    /// pause menu UIDocument and binds into whichever one instantiated the template.
    ///
    /// It never knows who owns it — closing raises <see cref="BackRequested"/> and the owning menu
    /// decides where "back" goes. That is what lets one component serve both menus.
    /// </summary>
    public class SettingsToolkitUI : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Document containing the SettingsPage template. Auto-resolved from this GameObject if empty.")]
        [SerializeField] private UIDocument _uiDocument;

        [Header("Back Button")]
        [Tooltip("Shows the page's own BACK button. Turn it off where the menu already offers another " +
                 "way out — the carpentry bench has a Back NAIL, so the button would be a second, " +
                 "redundant exit sitting on the same surface.")]
        [SerializeField] private bool _showBackButton = true;

        [Header("Subtitle Preview")]
        [Tooltip("Sample line shown on the Language page when the language is English.")]
        [SerializeField] private string _previewEnglish = "Fresh from the lake this morning — give me forty and it's yours.";

        [Tooltip("Sample line shown in Arabic mode. Also the quickest in-headset check that the " +
                 "Arabic font and its shaping are working.")]
        [SerializeField] private string _previewArabic = "طازة من البحيرة الصبح — هاتلي أربعين وخدها.";

        [Tooltip("Shown instead of a sample line when subtitles are off.")]
        [SerializeField] private string _previewOff = "Subtitles are off. Merchants still speak.";

        private ISettingsService _settings;
        private IConsentService _consent;
        private IEventService _events;

        [Inject]
        private void Inject(ISettingsService settings, IConsentService consent, IEventService events)
        {
            _settings = settings;
            _consent = consent;
            _events = events;
        }

        /// <summary>Raised by the Back button. The owning menu decides what to show next.</summary>
        public event Action BackRequested;

        private VisualElement _root;

        // What gets shown/hidden. When the page is used as a <ui:Instance>, UI Toolkit wraps it in a
        // TemplateContainer, and toggling only the inner SettingsRoot leaves that wrapper hidden —
        // the page then "opens" into a blank panel. Toggling the wrapper is what actually works.
        private VisualElement _visibilityTarget;

        private Button _backButton;
        private Button[] _tabs;
        private VisualElement[] _pages;

        private Slider[] _sliders;
        private Label[] _sliderValues;
        private Button[] _languageButtons;
        private Button[] _subtitleButtons;

        private Label _previewLabel;

        // --- Voice page ---------------------------------------------------------
        private Button[] _micModeButtons;
        private Button[] _sensitivityButtons;
        private Button[] _talkButtons;
        private Slider _thresholdSlider;
        private Label _thresholdValue;
        private VisualElement _sensitivityGroup;
        private VisualElement _talkButtonGroup;
        private VisualElement _gateRow;
        private VisualElement _deviceNav;
        private Button _devicePrevButton;
        private Button _deviceNextButton;
        private VisualElement _meterFill;
        private VisualElement _meterGate;
        private VisualElement _meterDot;
        private Label _meterDevice;
        private Label _meterCaption;

        // --- Permissions page ---------------------------------------------------
        private Button[] _micConsentButtons;
        private Button[] _aiConsentButtons;
        private Label _consentStatus;
        private VisualElement _permissionsGroup;

        /// <summary>
        /// The microphone behind the level meter.
        ///
        /// Its own capture, deliberately NOT the negotiation's: reading the live level off a
        /// <see cref="VoiceCaptureController"/> would mean opening an STT session — a network
        /// connection, and on a metered provider a bill — just to draw a bar in a menu. This reuses
        /// <see cref="MicrophoneStreamCapture"/>, the same class the STT clients drive, so the
        /// envelope follower and the adaptive gate the meter draws are the exact ones the merchant
        /// will hear through.
        /// </summary>
        private MicrophoneStreamCapture _micPreview;
        private bool _meterRunning;

        /// <summary>
        /// Connected microphones, re-read every time the Voice page opens rather than cached for the
        /// session — a headset can gain a mic when a USB dongle or a Link cable is plugged in, and a
        /// list captured at boot would never show it.
        /// </summary>
        private string[] _devices = System.Array.Empty<string>();

        /// <summary>Index into <see cref="_devices"/> of the player's choice, or -1 when there is none.</summary>
        private int _deviceIndex = -1;

        // Index of each slider matches the AudioChannel enum, which is contiguous from 0.
        private static readonly AudioChannel[] Channels =
        {
            AudioChannel.Master, AudioChannel.Music, AudioChannel.SoundFX,
            AudioChannel.Ambience, AudioChannel.Dialogue
        };

        private static readonly string[] SliderNames =
        {
            "MasterSlider", "MusicSlider", "SfxSlider", "AmbienceSlider", "DialogueSlider"
        };

        private static readonly string[] SliderValueNames =
        {
            "MasterValue", "MusicValue", "SfxValue", "AmbienceValue", "DialogueValue"
        };

        // Index matches the GameLanguage enum, which is contiguous from 0.
        private static readonly string[] LanguageButtonNames =
        {
            "LanguageEnglishButton", "LanguageArabicButton"
        };

        // Index 0 = on, 1 = off. Deliberately not a bool-indexed array — reading
        // `_subtitleButtons[0]` as "the ON button" is clearer at the call site.
        private static readonly string[] SubtitleButtonNames =
        {
            "SubtitlesOnButton", "SubtitlesOffButton"
        };

        // Index matches the MicCaptureMode enum, which is contiguous from 0.
        private static readonly string[] MicModeButtonNames =
        {
            "MicVoiceActivityButton", "MicPushToTalkButton"
        };

        // Index 0 = auto, 1 = manual.
        private static readonly string[] SensitivityButtonNames =
        {
            "MicAutoButton", "MicManualButton"
        };

        // Index matches the PushToTalkButton enum, which is contiguous from 0.
        private static readonly string[] TalkButtonNames =
        {
            "TalkPrimaryButton", "TalkSecondaryButton"
        };

        // Index 0 = allow, 1 = block. NOT indexed by ConsentStatus, whose 0 is NotAsked — a state
        // the player cannot click their way back into, so it has no button.
        private static readonly string[] MicConsentButtonNames =
        {
            "MicConsentAllowButton", "MicConsentBlockButton"
        };

        private static readonly string[] AiConsentButtonNames =
        {
            "AiConsentAllowButton", "AiConsentBlockButton"
        };

        private const string ActiveTabClass = "settings-tab--active";
        private const string ActivePageClass = "settings-page--active";
        private const string SelectedSegmentClass = "segment-btn--selected";
        private const string MutedPreviewClass = "subtitle-preview-line--muted";
        private const string HiddenGroupClass = "voice-group--hidden";
        private const string MeterFillOpenClass = "input-meter-fill--open";
        private const string MeterDotOpenClass = "input-meter-dot--open";
        private const string MeterGateAutoClass = "input-meter-gate--auto";
        private const string ConsentOkClass = "consent-status--ok";
        private const string ConsentBlockedClass = "consent-status--blocked";
        private const string ConsentOfflineClass = "consent-status--offline";

        /// <summary>Page index of the Voice tab, which is the only page that owns a live microphone.</summary>
        private const int VoicePageIndex = 2;

        private void OnEnable()
        {
            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null)
            {
                Debug.LogWarning($"{nameof(SettingsToolkitUI)} on '{name}' has no UIDocument — settings will not appear.", this);
                return;
            }

            VisualElement documentRoot = _uiDocument.rootVisualElement;
            if (documentRoot == null) return;

            _root = documentRoot.Q<VisualElement>("SettingsRoot");
            if (_root == null)
            {
                Debug.LogWarning($"{nameof(SettingsToolkitUI)} on '{name}' found no 'SettingsRoot' in the " +
                                 "document — is the SettingsPage template instanced in this UXML?", this);
                return;
            }

            // A TemplateContainer has no flex-grow of its own, so the page inside it would collapse
            // to its content height instead of filling the panel.
            _visibilityTarget = _root.parent is TemplateContainer ? _root.parent : _root;
            if (_visibilityTarget != _root) _visibilityTarget.style.flexGrow = 1f;

            if (_settings == null)
                Debug.LogWarning($"{nameof(SettingsToolkitUI)} on '{name}' has no {nameof(ISettingsService)} " +
                                 "injected — controls will show but change nothing. Does this scene have a " +
                                 "ContainerScope?", this);

            BindElements();
            Close();
        }

        private void OnDisable()
        {
            // Before the references go: the microphone is an OS resource, and dropping the element
            // it draws into would otherwise leave it open for the rest of the session.
            StopMeter();

            // UI Toolkit callbacks live on the elements, which are torn down with the document, so
            // there is nothing to unregister — but the cached references must go or a re-enable
            // would rebind on top of stale elements.
            _root = null;
            _visibilityTarget = null;
            _tabs = null;
            _pages = null;
            _sliders = null;
            _sliderValues = null;
            _languageButtons = null;
            _subtitleButtons = null;
            _events?.Unsubscribe<ConsentChangedEvent>(HandleConsentChanged);

            _micModeButtons = null;
            _sensitivityButtons = null;
            _talkButtons = null;
            _micConsentButtons = null;
            _aiConsentButtons = null;
            _consentStatus = null;
            _permissionsGroup = null;
            _thresholdSlider = null;
            _thresholdValue = null;
            _sensitivityGroup = null;
            _talkButtonGroup = null;
            _gateRow = null;
            _deviceNav = null;
            _devicePrevButton = null;
            _deviceNextButton = null;
            _meterFill = null;
            _meterGate = null;
            _meterDot = null;
            _meterDevice = null;
            _meterCaption = null;
        }

        /// <summary>
        /// Shows the page, always on Audio, with every control re-seeded from
        /// <see cref="ISettingsService"/>. Seeding PULLS rather than waiting for an event because
        /// SettingsService publishes its boot state during construction, long before this exists.
        /// </summary>
        public void Open()
        {
            if (_root == null) return;

            _root.style.display = DisplayStyle.Flex;
            _visibilityTarget.style.display = DisplayStyle.Flex;
            SeedFromSettings();
            ShowPage(0);
        }

        public void Close()
        {
            // Unconditionally, before the _root guard: Close() is called from OnEnable to hide the
            // page at boot, and an early return there would be fine — but it is also the path taken
            // when the player backs out of the menu, and the mic must not survive that.
            StopMeter();

            if (_root == null) return;
            _visibilityTarget.style.display = DisplayStyle.None;
        }

        public bool IsOpen => _visibilityTarget != null && _visibilityTarget.style.display == DisplayStyle.Flex;

        private void BindElements()
        {
            _backButton = _root.Q<Button>("SettingsBackButton");
            if (_backButton != null)
            {
                // Hidden, not SetEnabled(false): a greyed-out button still reads as a control the
                // player is meant to be able to hit, and in VR they will keep trying. display:None
                // takes it out of the header's layout entirely, leaving the title alone up there.
                _backButton.style.display = _showBackButton ? DisplayStyle.Flex : DisplayStyle.None;

                // Nothing is subscribed while it is off, so a host that hides the button cannot be
                // closed by one — BackRequested stays a promise the page only makes when it shows.
                if (_showBackButton) _backButton.clicked += () => BackRequested?.Invoke();
            }

            // The two arrays are index-paired, and VoicePageIndex names position 2 in both. Insert
            // anywhere else and the meter would start on the wrong tab.
            _tabs = new[]
            {
                _root.Q<Button>("TabAudioButton"),
                _root.Q<Button>("TabLanguageButton"),
                _root.Q<Button>("TabVoiceButton"),
                _root.Q<Button>("TabPermissionsButton"),
                _root.Q<Button>("TabControlsButton")
            };

            _pages = new[]
            {
                _root.Q<VisualElement>("AudioPage"),
                _root.Q<VisualElement>("LanguagePage"),
                _root.Q<VisualElement>("VoicePage"),
                _root.Q<VisualElement>("PermissionsPage"),
                _root.Q<VisualElement>("ControlsPage")
            };

            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                int index = i;   // captured per iteration, not shared
                _tabs[i].clicked += () => ShowPage(index);
            }

            BindSliders();
            BindLanguageSegment();
            BindSubtitleSegment();
            BindVoicePage();

            _previewLabel = _root.Q<Label>("SubtitlePreviewLabel");

            // The Controls page is static markup — rows of action and binding, authored in
            // SettingsPage.uxml. Nothing here to bind until the player can actually rebind.
        }

        private void BindSliders()
        {
            _sliders = new Slider[Channels.Length];
            _sliderValues = new Label[Channels.Length];

            for (int i = 0; i < Channels.Length; i++)
            {
                _sliders[i] = _root.Q<Slider>(SliderNames[i]);
                _sliderValues[i] = _root.Q<Label>(SliderValueNames[i]);

                if (_sliders[i] == null) continue;

                AudioChannel channel = Channels[i];
                int index = i;
                Slider slider = _sliders[i];

                // While dragging: apply to the mixer so it is audible, but do NOT hit the disk.
                // PlayerPrefs.Save is a synchronous write and would hitch a headset at 90 Hz.
                slider.RegisterValueChangedCallback(evt =>
                {
                    _settings?.SetVolume(channel, evt.newValue, persistNow: false);
                    SetSliderValueLabel(index, evt.newValue);
                });

                // On release the slider gives up pointer capture — that is the moment to persist.
                slider.RegisterCallback<PointerCaptureOutEvent>(_ =>
                    _settings?.SetVolume(channel, slider.value, persistNow: true));
            }
        }

        private void BindLanguageSegment()
        {
            _languageButtons = new Button[LanguageButtonNames.Length];

            for (int i = 0; i < LanguageButtonNames.Length; i++)
            {
                _languageButtons[i] = _root.Q<Button>(LanguageButtonNames[i]);
                if (_languageButtons[i] == null) continue;

                GameLanguage language = (GameLanguage)i;   // English, Arabic — same order as the UXML
                _languageButtons[i].clicked += () =>
                {
                    _settings?.SetLanguage(language);
                    SeedFromSettings();
                };
            }
        }

        private void BindSubtitleSegment()
        {
            _subtitleButtons = new Button[SubtitleButtonNames.Length];

            for (int i = 0; i < SubtitleButtonNames.Length; i++)
            {
                _subtitleButtons[i] = _root.Q<Button>(SubtitleButtonNames[i]);
                if (_subtitleButtons[i] == null) continue;

                bool enabled = i == 0;   // 0 = ON, 1 = OFF
                _subtitleButtons[i].clicked += () =>
                {
                    _settings?.SetSubtitlesEnabled(enabled);
                    SeedFromSettings();
                };
            }
        }

        private void BindVoicePage()
        {
            _sensitivityGroup = _root.Q<VisualElement>("SensitivityGroup");
            _talkButtonGroup = _root.Q<VisualElement>("TalkButtonGroup");
            _gateRow = _root.Q<VisualElement>("GateRow");

            _deviceNav = _root.Q<VisualElement>("MicDeviceNav");
            _devicePrevButton = _root.Q<Button>("MicDevicePrevButton");
            _deviceNextButton = _root.Q<Button>("MicDeviceNextButton");

            if (_devicePrevButton != null) _devicePrevButton.clicked += () => CycleDevice(-1);
            if (_deviceNextButton != null) _deviceNextButton.clicked += () => CycleDevice(1);

            _meterFill = _root.Q<VisualElement>("InputMeterFill");
            _meterGate = _root.Q<VisualElement>("InputMeterGate");
            _meterDot = _root.Q<VisualElement>("InputMeterDot");
            _meterDevice = _root.Q<Label>("InputMeterDevice");
            _meterCaption = _root.Q<Label>("InputMeterCaption");

            _micModeButtons = BindSegment(MicModeButtonNames, i =>
                _settings?.SetMicCaptureMode((MicCaptureMode)i));

            _sensitivityButtons = BindSegment(SensitivityButtonNames, i =>
                _settings?.SetMicAutoSensitivity(i == 0));

            _talkButtons = BindSegment(TalkButtonNames, i =>
                _settings?.SetPushToTalkButton((PushToTalkButton)i));

            BindConsent();
            BindThresholdSlider();
        }

        /// <summary>
        /// Wires the two permission rows. Each writes BOTH flags because
        /// <see cref="IConsentService.SetConsent"/> takes both — the untouched one is passed through
        /// unchanged rather than re-derived, so clicking one row can never quietly reset the other.
        /// </summary>
        private void BindConsent()
        {
            _permissionsGroup = _root.Q<VisualElement>("PermissionsPage");
            _consentStatus = _root.Q<Label>("ConsentStatusLabel");

            _micConsentButtons = BindSegment(MicConsentButtonNames, i =>
                SetConsent(mic: Answer(i), ai: null));

            _aiConsentButtons = BindSegment(AiConsentButtonNames, i =>
                SetConsent(mic: null, ai: Answer(i)));

            // Granting mic consent can put an OS permission dialog on screen, and the answer lands
            // asynchronously — so the re-seed BindSegment does on click runs while the decision is
            // still pending. ConsentChangedEvent fires once it has actually settled.
            _events?.Subscribe<ConsentChangedEvent>(HandleConsentChanged);
        }

        private static ConsentStatus Answer(int segmentIndex) =>
            segmentIndex == 0 ? ConsentStatus.Granted : ConsentStatus.Declined;

        private void SetConsent(ConsentStatus? mic, ConsentStatus? ai)
        {
            if (_consent == null) return;

            ConsentState current = _consent.GetConsentStatus();
            _consent.SetConsent(mic ?? current.MicConsent, ai ?? current.InternetConsent);
        }

        private void HandleConsentChanged(ConsentChangedEvent evt) => ApplyConsent();

        /// <summary>
        /// Draws both rows and the status line from the service.
        ///
        /// The status line is deliberately NOT derived from the two segments: both can read ALLOW
        /// and voice still be unavailable, because <see cref="IConsentService.IsVoiceModeAvailable"/>
        /// also wants a reachable network and the OS microphone grant. Those are three different
        /// sentences, and telling a player their setting is wrong when it is right sends them to
        /// change the one thing they had already got right.
        /// </summary>
        private void ApplyConsent()
        {
            if (_consent == null)
            {
                // No service should be impossible now that it is a root binding, but a panel that
                // silently offers dead switches is worse than one that hides them.
                _permissionsGroup?.SetEnabled(false);
                return;
            }

            ConsentState state = _consent.GetConsentStatus();

            PaintConsentSegment(_micConsentButtons, state.MicConsent);
            PaintConsentSegment(_aiConsentButtons, state.InternetConsent);

            bool refused = state.MicConsent == ConsentStatus.Declined
                           || state.InternetConsent == ConsentStatus.Declined;
            bool undecided = !refused
                             && (state.MicConsent == ConsentStatus.NotAsked
                                 || state.InternetConsent == ConsentStatus.NotAsked);

            string message;
            string tone = null;

            if (undecided)
            {
                message = "Not set up yet — choose for each.";
            }
            else if (refused)
            {
                message = "Merchants will use the buttons instead. You can still sell everything.";
                tone = ConsentBlockedClass;
            }
            else if (!MicrophonePermission.IsGranted)
            {
                message = "Your device is still blocking the microphone. Allow it in the system settings.";
                tone = ConsentBlockedClass;
            }
            else if (!_consent.IsVoiceModeAvailable())
            {
                // Both allowed and the OS is happy, so the only thing left is the network.
                message = "No connection — the buttons will stand in until it is back.";
                tone = ConsentOfflineClass;
            }
            else
            {
                message = "Voice haggling is on.";
                tone = ConsentOkClass;
            }

            if (_consentStatus != null)
            {
                _consentStatus.text = message;
                _consentStatus.EnableInClassList(ConsentOkClass, tone == ConsentOkClass);
                _consentStatus.EnableInClassList(ConsentBlockedClass, tone == ConsentBlockedClass);
                _consentStatus.EnableInClassList(ConsentOfflineClass, tone == ConsentOfflineClass);
            }
        }

        private static void PaintConsentSegment(Button[] buttons, ConsentStatus status)
        {
            if (buttons == null) return;

            bool allowed = status == ConsentStatus.Granted;
            bool blocked = status == ConsentStatus.Declined;

            // Both buttons use the page's one selection class, exactly as the subtitle, mic-mode and
            // talk-button segments do. A chosen BLOCK is still a chosen option, and giving it its own
            // colour made it the only control here whose "selected" look came from a class added
            // alongside the feature — so when that rule was not in the loaded stylesheet, ALLOW lit
            // up and BLOCK appeared to do nothing.
            //
            // NotAsked lights neither, which is the honest picture: the player has not chosen, and
            // pre-selecting one would put words in their mouth on a privacy control.
            buttons[0]?.EnableInClassList(SelectedSegmentClass, allowed);
            buttons[1]?.EnableInClassList(SelectedSegmentClass, blocked);
        }

        /// <summary>
        /// Wires one segment: each button reports its own index, then the whole page re-seeds so the
        /// lit pill, the meter and the two mode-specific groups all agree with the service rather
        /// than with whatever the click assumed.
        /// </summary>
        private Button[] BindSegment(string[] names, Action<int> onPicked)
        {
            var buttons = new Button[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                buttons[i] = _root.Q<Button>(names[i]);
                if (buttons[i] == null) continue;

                int index = i;   // captured per iteration, not shared
                buttons[i].clicked += () =>
                {
                    onPicked(index);
                    SeedFromSettings();
                };
            }

            return buttons;
        }

        private void BindThresholdSlider()
        {
            _thresholdSlider = _root.Q<Slider>("MicThresholdSlider");
            _thresholdValue = _root.Q<Label>("MicThresholdValue");

            if (_thresholdSlider == null) return;

            // The slider runs 0..1 like the volume sliders; the service stores the gate in the
            // capture source's peak-amplitude domain. Same disk-write contract as the volumes: apply
            // on every change, persist only when the drag ends.
            _thresholdSlider.RegisterValueChangedCallback(evt =>
            {
                float threshold = evt.newValue * SettingsKeys.MaxMicThreshold;

                _settings?.SetMicThreshold(threshold, persistNow: false);
                SetThresholdLabel(evt.newValue);

                // Straight onto the preview mic as well, so the gate marker moves WITH the handle.
                // Waiting for the next SeedFromSettings would leave the marker a drag behind — and
                // watching the marker cross the level is the entire point of setting it by hand.
                if (_micPreview != null) _micPreview.ManualThreshold = threshold;
            });

            _thresholdSlider.RegisterCallback<PointerCaptureOutEvent>(_ =>
                _settings?.SetMicThreshold(_thresholdSlider.value * SettingsKeys.MaxMicThreshold,
                                           persistNow: true));
        }

        private void SetThresholdLabel(float value01)
        {
            if (_thresholdValue == null) return;
            _thresholdValue.text = Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f).ToString();
        }

        private void SeedFromSettings()
        {
            // Before the ISettingsService guard: consent comes from a different service, and a page
            // opened without settings should still show the permissions truthfully.
            ApplyConsent();

            if (_settings == null) return;

            for (int i = 0; i < Channels.Length; i++)
            {
                float value = _settings.GetVolume(Channels[i]);

                // SetValueWithoutNotify so seeding does not fire the change callback and write the
                // value straight back through the service.
                _sliders?[i]?.SetValueWithoutNotify(value);
                SetSliderValueLabel(i, value);
            }

            ApplyLanguageAndSubtitles();
            ApplyVoiceSettings();
        }

        /// <summary>
        /// Lights the selected pill in each Voice segment and swaps the two mode-specific groups.
        ///
        /// Only one group is ever on screen: push-to-talk sets <c>AutoStopOnSilence = false</c>, so a
        /// sensitivity gate has nothing to act on, and voice activity has no button to hold. Showing
        /// both would put two dead controls on a page with no room for them.
        /// </summary>
        private void ApplyVoiceSettings()
        {
            if (_settings == null) return;

            MicCaptureMode mode = _settings.MicCaptureMode;
            bool auto = _settings.MicAutoSensitivity;
            bool pushToTalk = mode == MicCaptureMode.PushToTalk;

            if (_micModeButtons != null)
            {
                for (int i = 0; i < _micModeButtons.Length; i++)
                    _micModeButtons[i]?.EnableInClassList(SelectedSegmentClass, i == (int)mode);
            }

            if (_sensitivityButtons != null)
            {
                _sensitivityButtons[0]?.EnableInClassList(SelectedSegmentClass, auto);
                if (_sensitivityButtons.Length > 1)
                    _sensitivityButtons[1]?.EnableInClassList(SelectedSegmentClass, !auto);
            }

            if (_talkButtons != null)
            {
                PushToTalkButton talk = _settings.PushToTalkButton;
                for (int i = 0; i < _talkButtons.Length; i++)
                    _talkButtons[i]?.EnableInClassList(SelectedSegmentClass, i == (int)talk);
            }

            _sensitivityGroup?.EnableInClassList(HiddenGroupClass, pushToTalk);
            _talkButtonGroup?.EnableInClassList(HiddenGroupClass, !pushToTalk);

            // The gate handle under the bar is only meaningful while the gate is manual, and only in
            // voice activity — push-to-talk has no gate for it to move.
            _gateRow?.EnableInClassList(HiddenGroupClass, auto || pushToTalk);

            float normalized = Mathf.Clamp01(_settings.MicThreshold / SettingsKeys.MaxMicThreshold);
            _thresholdSlider?.SetValueWithoutNotify(normalized);
            SetThresholdLabel(normalized);

            // The preview mic mirrors the settings so the gate it reports — and therefore the marker
            // the meter draws — is the one the merchant will actually be heard through.
            if (_micPreview != null)
            {
                _micPreview.AutoAdjustSensitivity = auto;
                _micPreview.ManualThreshold = _settings.MicThreshold;
            }

            // No gate exists in push-to-talk, so there is nothing to mark.
            if (_meterGate != null)
            {
                _meterGate.style.display = pushToTalk ? DisplayStyle.None : DisplayStyle.Flex;
                _meterGate.EnableInClassList(MeterGateAutoClass, auto);
            }

            UpdateMeterCaption();
        }

        private void SetSliderValueLabel(int index, float value01)
        {
            Label label = _sliderValues != null && index < _sliderValues.Length ? _sliderValues[index] : null;
            if (label == null) return;

            label.text = Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f).ToString();
        }

        /// <summary>
        /// Lights the selected pill in each segment and redraws the caption sample.
        ///
        /// The two segments are read independently, so the preview can show an Arabic line that is
        /// currently switched off — which is exactly the state the old single control could not
        /// represent.
        /// </summary>
        private void ApplyLanguageAndSubtitles()
        {
            if (_settings == null) return;

            GameLanguage language = _settings.Language;
            bool subtitlesOn = _settings.SubtitlesEnabled;

            if (_languageButtons != null)
            {
                for (int i = 0; i < _languageButtons.Length; i++)
                    _languageButtons[i]?.EnableInClassList(SelectedSegmentClass, i == (int)language);
            }

            if (_subtitleButtons != null)
            {
                _subtitleButtons[0]?.EnableInClassList(SelectedSegmentClass, subtitlesOn);
                if (_subtitleButtons.Length > 1)
                    _subtitleButtons[1]?.EnableInClassList(SelectedSegmentClass, !subtitlesOn);
            }

            if (_previewLabel == null) return;

            if (!subtitlesOn)
            {
                _previewLabel.text = _previewOff;
                _previewLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                _previewLabel.EnableInClassList(MutedPreviewClass, true);
                return;
            }

            bool arabic = language == GameLanguage.Arabic;
            _previewLabel.text = arabic ? _previewArabic : _previewEnglish;

            // UI Toolkit has no direction property, so right-alignment is the closest we can get to
            // an RTL layout for the preview.
            _previewLabel.style.unityTextAlign = arabic ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            _previewLabel.EnableInClassList(MutedPreviewClass, false);
        }

        private void ShowPage(int index)
        {
            if (_tabs == null || _pages == null) return;

            for (int i = 0; i < _pages.Length; i++)
            {
                bool active = i == index;
                _tabs[i]?.EnableInClassList(ActiveTabClass, active);
                _pages[i]?.EnableInClassList(ActivePageClass, active);
            }

            // The mic is open only while its page is, never for the whole time the menu is up.
            if (index == VoicePageIndex) StartMeter();
            else StopMeter();
        }

        // ----------------------------------------------------------------- Device --

        /// <summary>
        /// Re-reads the connected microphones and points the picker at the player's choice.
        ///
        /// The stored setting is a device NAME, so a machine whose device order changed — or that
        /// has since lost the chosen mic — resolves to the first available one here, matching what
        /// <c>MicrophoneStreamCapture.ResolveDevice</c> will do when capture actually starts. The two
        /// must agree, or the menu would name one microphone while the merchant listened to another.
        /// </summary>
        private void RefreshDeviceList()
        {
            _devices = Microphone.devices ?? System.Array.Empty<string>();
            _deviceIndex = -1;

            string chosen = _settings?.MicDevice;
            if (!string.IsNullOrEmpty(chosen))
                _deviceIndex = System.Array.IndexOf(_devices, chosen);

            // No stored choice, or one that is no longer plugged in.
            if (_deviceIndex < 0 && _devices.Length > 0) _deviceIndex = 0;

            // The arrows only exist where there is something to switch between. On a standalone
            // headset that is one device, so the head stays a single line and the page keeps the
            // 64px it would otherwise spend on a control that cannot do anything.
            bool switchable = _devices.Length > 1;
            _deviceNav?.EnableInClassList(HiddenGroupClass, !switchable);

            UpdateDeviceLabel();
        }

        private void UpdateDeviceLabel()
        {
            if (_meterDevice == null) return;

            _meterDevice.text = _deviceIndex >= 0 && _deviceIndex < _devices.Length
                ? _devices[_deviceIndex]
                : "No microphone found";
        }

        /// <summary>
        /// Steps to the next or previous microphone and reopens the meter on it, so the bar is
        /// answering for the device now named above it. Wraps, because with two devices a wrapping
        /// pair of arrows is simply a toggle and never dead-ends.
        /// </summary>
        private void CycleDevice(int step)
        {
            if (_devices.Length <= 1 || _settings == null) return;

            _deviceIndex = ((_deviceIndex + step) % _devices.Length + _devices.Length) % _devices.Length;
            _settings.SetMicDevice(_devices[_deviceIndex]);

            UpdateDeviceLabel();

            // The preview is holding the OLD device open; it has to be released before the new one
            // can be started, or the restart lands on a mic the OS still considers busy.
            if (_meterRunning)
            {
                StopMeter();
                StartMeter();
            }
        }

        // ------------------------------------------------------------------ Meter --

        /// <summary>
        /// Opens the preview microphone, unless something has a better claim on it.
        ///
        /// Two refusals, both deliberate. Without OS permission there is nothing to show and asking
        /// for it from a settings menu would be a prompt out of nowhere — that consent belongs to
        /// <see cref="ConsentManager"/>, at the point the game first wants to listen. And if any
        /// device is already recording, a negotiation is very likely mid-utterance: a second
        /// <c>Microphone.Start</c> on the same device ends the first, so opening the pause menu
        /// during a haggle would silently cut the player off. Better a meter that explains itself.
        /// </summary>
        private void StartMeter()
        {
            if (_meterRunning || _meterFill == null) return;

            // Before the permission check: the picker has to show what is connected even when the
            // meter itself cannot run, or a player who has denied the mic sees an empty control and
            // no way to tell whether the game found their hardware.
            RefreshDeviceList();

            if (!MicrophonePermission.IsGranted)
            {
                SetMeterUnavailable("Microphone access is off, so there is nothing to show here.");
                return;
            }

            if (IsAnyMicrophoneRecording())
            {
                SetMeterUnavailable("The microphone is busy. Close this to get it back.");
                return;
            }

            _micPreview = new MicrophoneStreamCapture { LogPrefix = "[Settings mic]" };

            if (_settings != null)
            {
                _micPreview.AutoAdjustSensitivity = _settings.MicAutoSensitivity;
                _micPreview.ManualThreshold = _settings.MicThreshold;
                _micPreview.Device = _settings.MicDevice;   // empty = system default
            }

            if (!_micPreview.Start(out string error))
            {
                _micPreview = null;
                SetMeterUnavailable(error ?? "No microphone found.");
                return;
            }

            _meterRunning = true;

            // What the capture actually opened, which can differ from the stored name when that
            // device has been unplugged. Naming the real one keeps the label honest.
            if (_meterDevice != null && !string.IsNullOrEmpty(_micPreview.ResolvedDevice))
                _meterDevice.text = _micPreview.ResolvedDevice;

            UpdateMeterCaption();
        }

        private void StopMeter()
        {
            if (_micPreview != null)
            {
                _micPreview.Stop();
                _micPreview = null;
            }

            _meterRunning = false;
            DrawMeter(0f, 0f, open: false);
        }

        private static bool IsAnyMicrophoneRecording()
        {
            string[] devices = Microphone.devices;
            if (devices == null) return false;

            for (int i = 0; i < devices.Length; i++)
            {
                if (Microphone.IsRecording(devices[i])) return true;
            }

            return false;
        }

        /// <summary>
        /// Explains why the bar is flat. The device LABEL is deliberately left alone — it was filled
        /// by <see cref="RefreshDeviceList"/> and still answers "did the game find my microphone",
        /// which is the first thing a player checks when the meter is not moving.
        /// </summary>
        private void SetMeterUnavailable(string reason)
        {
            if (_meterCaption != null) _meterCaption.text = reason;
            DrawMeter(0f, 0f, open: false);
        }

        /// <summary>
        /// Polls the preview mic and redraws the bar.
        ///
        /// Runs on <see cref="Update"/> rather than a coroutine because the pause menu opens with
        /// <c>Time.timeScale</c> at 0, which stops every <c>WaitForSeconds</c> in the game. Nothing
        /// here reads scaled time.
        /// </summary>
        private void Update()
        {
            if (!_meterRunning || _micPreview == null) return;

            _micPreview.Poll(out _, out _);

            float level = _micPreview.CurrentInputLevel;
            float gate = _micPreview.CurrentGateThreshold;

            DrawMeter(level, gate, level > gate && gate > 0f);
        }

        /// <summary>
        /// Draws the bar. Both values are peak amplitudes scaled against
        /// <see cref="SettingsKeys.MaxMicThreshold"/>, not 1.0 — see the note on
        /// <c>.input-meter</c> in MenuStyles.uss for why full scale is 0.5.
        /// </summary>
        private void DrawMeter(float level, float gate, bool open)
        {
            if (_meterFill == null) return;

            float levelPercent = Mathf.Clamp01(level / SettingsKeys.MaxMicThreshold) * 100f;
            _meterFill.style.width = Length.Percent(levelPercent);
            _meterFill.EnableInClassList(MeterFillOpenClass, open);

            if (_meterGate != null)
            {
                float gatePercent = Mathf.Clamp01(gate / SettingsKeys.MaxMicThreshold) * 100f;
                _meterGate.style.left = Length.Percent(gatePercent);
            }

            _meterDot?.EnableInClassList(MeterDotOpenClass, open);
        }

        private void UpdateMeterCaption()
        {
            if (_meterCaption == null || !_meterRunning || _settings == null) return;

            // Kept to one line each. The caption is the last element on the page in its tightest
            // state, so a string long enough to wrap is a string that pushes itself off the panel.
            if (_settings.MicCaptureMode == MicCaptureMode.PushToTalk)
            {
                _meterCaption.text = "Hold the talk button to test it.";
                return;
            }

            _meterCaption.text = _settings.MicAutoSensitivity
                ? "Speak to test. The gate follows your room."
                : "Set the gate just above the bar when quiet.";
        }
    }
}
