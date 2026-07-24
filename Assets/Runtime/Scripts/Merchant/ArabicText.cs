using System.Text;

namespace DumyatRuntime
{
    /// <summary>
    /// Shared helpers for the Egyptian-Arabic text that flows out to the TTS voice. Pure string work —
    /// no Unity types.
    /// </summary>
    public static class ArabicText
    {
        /// <summary>
        /// Removes Arabic vowelization (tashkeel/harakat) and the tatweel stretch character, so the
        /// ElevenLabs voice pronounces plain colloquial text. Strips the combining-mark ranges an LLM
        /// might emit despite being told not to: fatha/damma/kasra/tanwin/shadda/sukun and the extended
        /// marks (U+064B–U+065F), plus the superscript alef (U+0670). Base letters are untouched.
        /// </summary>
        public static string StripDiacritics(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                // U+064B..U+065F (harakat/tanwin/shadda/sukun + extended marks), U+0670 (superscript
                // alef), U+0640 (tatweel — a purely decorative letter-stretcher).
                if ((c >= 'ً' && c <= 'ٟ') || c == 'ٰ' || c == 'ـ')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
