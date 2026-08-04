using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Settings panel of the carpentry (nail) main menu.
    ///
    /// WHY THIS TALKS TO <see cref="ISettingsService"/>
    /// -----------------------------------------------
    /// This panel used to drive <see cref="ILegacySettingsService"/>, which keeps its OWN PlayerPrefs
    /// entries — "Language", "SubtitlesEnabled" — separate from the ones the UI Toolkit main menu
    /// writes ("Dumyat.Language", "Dumyat.SubtitlesOn"). The two menus therefore disagreed: setting
    /// the language here left every UI Toolkit panel in the game still drawing English, and the
    /// volume sliders here covered only three of the mixer's five channels, so Ambience and Dialogue
    /// could not be reached from this menu at all.
    ///
    /// Pointing this panel at the canonical service makes the two menus one setting rather than two
    /// that merely look alike. The legacy service is still used for SUBTITLE SIZE, which is a
    /// legacy-only control with no equivalent on the new menu.
    ///
    /// Every field below is optional: a scene that only wired three sliders keeps working, and the
    /// two new channels light up as soon as sliders are added for them.
    /// </summary>
    public class SettingsUI : MonoBehaviour
    {
        [Header("Subtitles")]
        [SerializeField] private Toggle _subtitlesToggle;

        [Tooltip("Legacy-only control — the UI Toolkit menu has no subtitle size, so this stays backed " +
                 "by ILegacySettingsService.")]
        [SerializeField] private Slider _subtitleSizeSlider;

        [Tooltip("Interface language. Option order must match the GameLanguage enum (English, Arabic).")]
        [SerializeField] private Dropdown _languageDropdown;

        // The two FormerlySerializedAs entries keep the sliders already wired into
        // Testing_MainMenu attached, and move them onto the channel their LABEL promises. The old
        // code sent the "Voice" slider to Music and the "Ambient" slider to SFX, so dragging either
        // changed something the player was not looking at.
        [Header("Audio — one slider per mixer channel")]
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _musicVolumeSlider;
        [SerializeField] private Slider _sfxVolumeSlider;
        [FormerlySerializedAs("_ambientVolumeSlider")]
        [SerializeField] private Slider _ambienceVolumeSlider;
        [FormerlySerializedAs("_voiceVolumeSlider")]
        [SerializeField] private Slider _dialogueVolumeSlider;

        private ISettingsService _settings;
        private ILegacySettingsService _legacySettings;

        // Index matches _sliders, so one loop covers wiring, reading and writing.
        private static readonly AudioChannel[] Channels =
        {
            AudioChannel.Master, AudioChannel.Music, AudioChannel.SoundFX,
            AudioChannel.Ambience, AudioChannel.Dialogue
        };

        private Slider[] _sliders;

        [Inject]
        private void Inject(ISettingsService settings, ILegacySettingsService legacySettings)
        {
            _settings = settings;
            _legacySettings = legacySettings;
        }

        private void Awake()
        {
            _sliders = new[]
            {
                _masterVolumeSlider, _musicVolumeSlider, _sfxVolumeSlider,
                _ambienceVolumeSlider, _dialogueVolumeSlider
            };
        }

        private void OnEnable()
        {
            if (_settings == null)
                Debug.LogWarning($"{nameof(SettingsUI)} on '{name}' has no {nameof(ISettingsService)} — " +
                                 "this menu's settings will not apply or persist. Is this UI under a " +
                                 "Reflex scene scope?", this);

            InitializeUI();
            Subscribe(true);
        }

        private void OnDisable() => Subscribe(false);

        private void Subscribe(bool on)
        {
            if (_sliders == null) return;

            for (int i = 0; i < _sliders.Length; i++)
            {
                Slider slider = _sliders[i];
                if (slider == null) continue;

                AudioChannel channel = Channels[i];

                // Cleared in both cases so re-enabling the panel cannot stack a second listener.
                slider.onValueChanged.RemoveAllListeners();
                if (on) slider.onValueChanged.AddListener(value => OnVolumeChanged(channel, value));
            }

            if (_subtitlesToggle != null)
            {
                _subtitlesToggle.onValueChanged.RemoveListener(OnSubtitlesChanged);
                if (on) _subtitlesToggle.onValueChanged.AddListener(OnSubtitlesChanged);
            }

            if (_subtitleSizeSlider != null)
            {
                _subtitleSizeSlider.onValueChanged.RemoveListener(OnSubtitleSizeChanged);
                if (on) _subtitleSizeSlider.onValueChanged.AddListener(OnSubtitleSizeChanged);
            }

            if (_languageDropdown != null)
            {
                _languageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);
                if (on) _languageDropdown.onValueChanged.AddListener(OnLanguageChanged);
            }
        }

        /// <summary>
        /// Shows what the shared settings currently are, so opening this menu after changing something
        /// on the main menu does not display stale values. Set-without-notify throughout: writing the
        /// value back on open would be a no-op at best and would clobber a real setting at worst.
        /// </summary>
        private void InitializeUI()
        {
            if (_settings != null)
            {
                for (int i = 0; i < _sliders.Length; i++)
                {
                    if (_sliders[i] != null)
                        _sliders[i].SetValueWithoutNotify(_settings.GetVolume(Channels[i]));
                }

                if (_subtitlesToggle != null)
                    _subtitlesToggle.SetIsOnWithoutNotify(_settings.SubtitlesEnabled);

                if (_languageDropdown != null)
                    _languageDropdown.SetValueWithoutNotify((int)_settings.Language);
            }

            if (_legacySettings != null && _subtitleSizeSlider != null)
                _subtitleSizeSlider.SetValueWithoutNotify(_legacySettings.SubtitleSize);
        }

        private void OnVolumeChanged(AudioChannel channel, float value) =>
            _settings?.SetVolume(channel, value);

        private void OnSubtitlesChanged(bool value) => _settings?.SetSubtitlesEnabled(value);

        private void OnLanguageChanged(int value) => _settings?.SetLanguage((GameLanguage)value);

        // Legacy-only: the new menu has no subtitle size, so this stays on the legacy service.
        private void OnSubtitleSizeChanged(float value) =>
            _legacySettings?.SetSubtitleSize(Mathf.RoundToInt(value));
    }
}
