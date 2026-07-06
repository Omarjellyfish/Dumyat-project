using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class SettingsManager : MonoBehaviour
    {
        private bool _subtitlesEnabled = true;
        private int _subtitleSize = 2;
        private bool _subtitlesLangugeArabic = true;
        public void SetLanguge(bool _value) => _subtitlesLangugeArabic = _value;
        public bool GetLanguage() => _subtitlesLangugeArabic;

        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        public void SetSubtitles(bool _value)
        {
            _subtitlesEnabled = _value;
            _eventService.Publish(new SubtitleSettingsChangedEvent(_subtitlesEnabled, _subtitleSize));
        }

        public void SetSubtitleSize(int _value)
        {
            _subtitleSize = _value;
            _eventService.Publish(new SubtitleSettingsChangedEvent(_subtitlesEnabled, _subtitleSize));
        }

        public bool SubtitlesEnabled => _subtitlesEnabled;
        public int SubtitleSize => _subtitleSize;
    }
}