using UnityEngine;

namespace DumyatRuntime
{
    public class SettingsManager : MonoBehaviour
    {
        private bool _subtitlesEnabled = true;
        private int _subtitleSize = 2;
        private bool _subtitlesLangugeArabic = true;
        public void SetSubtitles(bool _value) => _subtitlesEnabled = _value;
        public void SetLanguge(bool _value) => _subtitlesLangugeArabic = _value;
        public void SetSubtitleSize(int _value) => _subtitleSize = _value;
        public bool GetLanguage() => _subtitlesLangugeArabic;
        public bool SubtitlesEnabled => _subtitlesEnabled;
        public int SubtitleSize => _subtitleSize;
    }
}