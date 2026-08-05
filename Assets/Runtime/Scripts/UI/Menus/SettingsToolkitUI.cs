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

        [Header("Subtitle Preview")]
        [Tooltip("Sample line shown on the Language page when the language is English.")]
        [SerializeField] private string _previewEnglish = "Fresh from the lake this morning — give me forty and it's yours.";

        [Tooltip("Sample line shown in Arabic mode. Also the quickest in-headset check that the " +
                 "Arabic font and its shaping are working.")]
        [SerializeField] private string _previewArabic = "طازة من البحيرة الصبح — هاتلي أربعين وخدها.";

        [Tooltip("Shown instead of a sample line when subtitles are off.")]
        [SerializeField] private string _previewOff = "Subtitles are off. Merchants still speak.";

        private ISettingsService _settings;
        private MenuCatalogSO _catalog;

        [Inject]
        private void Inject(ISettingsService settings, MenuCatalogSO catalog)
        {
            _settings = settings;
            _catalog = catalog;
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
        private VisualElement _controlsImage;

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

        private const string ActiveTabClass = "settings-tab--active";
        private const string ActivePageClass = "settings-page--active";
        private const string SelectedSegmentClass = "segment-btn--selected";
        private const string MutedPreviewClass = "subtitle-preview-line--muted";

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
            if (_root == null) return;
            _visibilityTarget.style.display = DisplayStyle.None;
        }

        public bool IsOpen => _visibilityTarget != null && _visibilityTarget.style.display == DisplayStyle.Flex;

        private void BindElements()
        {
            _backButton = _root.Q<Button>("SettingsBackButton");
            if (_backButton != null) _backButton.clicked += () => BackRequested?.Invoke();

            _tabs = new[]
            {
                _root.Q<Button>("TabAudioButton"),
                _root.Q<Button>("TabLanguageButton"),
                _root.Q<Button>("TabControlsButton")
            };

            _pages = new[]
            {
                _root.Q<VisualElement>("AudioPage"),
                _root.Q<VisualElement>("LanguagePage"),
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

            _previewLabel = _root.Q<Label>("SubtitlePreviewLabel");
            _controlsImage = _root.Q<VisualElement>("ControlsImage");

            if (_controlsImage != null && _catalog != null && _catalog.ControlsImage != null)
                _controlsImage.style.backgroundImage = new StyleBackground(_catalog.ControlsImage);
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

        private void SeedFromSettings()
        {
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
        }
    }
}
