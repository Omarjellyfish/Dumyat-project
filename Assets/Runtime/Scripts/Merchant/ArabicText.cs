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

        /// <summary>
        /// Replaces a small set of words that are NEVER Egyptian with the Egyptian form, as a last line of
        /// defence before the text reaches the voice. The ElevenLabs voice is an Egyptian one: it renders
        /// Modern Standard and Levantine words badly, so "يا بني الحلال" (which is what an LLM drifts to)
        /// comes out broken where "يا ابن الحلال" — what a Damietta seller actually says — does not.
        ///
        /// The prompt is the real fix (see MerchantNegotiationLLM's dialect instruction); this only catches
        /// what slips through, exactly like <see cref="StripDiacritics"/> catching tashkeel the model was
        /// told not to write. That is why the table is deliberately tiny: only entries whose replacement is
        /// safe in ANY position and whose original is never valid Egyptian. Dialect is not a dictionary
        /// swap — anything needing surrounding context (شو ، هذا ، ماذا ، أريد, which change word ORDER as
        /// well as words) is left to the prompt rather than mangled here.
        /// </summary>
        public static string ToEgyptianForms(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                // Only start a match at a word boundary, so a substitution can never fire inside a longer
                // word (كثير must not match inside اكثير, and بس must never eat part of بسرعة).
                if ((i == 0 || !IsArabicLetter(s[i - 1])) && TryMatchAt(s, i, out string replacement, out int length))
                {
                    sb.Append(replacement);
                    i += length;
                    continue;
                }

                sb.Append(s[i]);
                i++;
            }
            return sb.ToString();
        }

        // Non-Egyptian form -> what an Egyptian says. Phrases come first so the longest match wins.
        private static readonly string[,] EgyptianForms =
        {
            { "بني الحلال", "ابن الحلال" },   // Levantine/MSA contraction of ابن — the one that started this
            { "هلق", "دلوقتي" },
            { "الان", "دلوقتي" },
            { "حالا", "دلوقتي" },
            { "وين", "فين" },
            { "ليش", "ليه" },
            { "هيك", "كده" },
            { "هكذا", "كده" },
            { "منيح", "كويس" },
            { "جيد", "كويس" },
            { "بدي", "عايز" },
            { "نقود", "فلوس" },
            { "كثير", "كتير" },
            { "ايضا", "كمان" },
            { "فقط", "بس" }
        };

        // Matches EgyptianForms[n, 0] at index i, comparing letters in their folded form (so أ/إ/آ/ا and
        // ى/ي spelling variants all hit) and requiring a word boundary at BOTH ends.
        private static bool TryMatchAt(string s, int i, out string replacement, out int length)
        {
            for (int n = 0; n < EgyptianForms.GetLength(0); n++)
            {
                string candidate = EgyptianForms[n, 0];
                if (i + candidate.Length > s.Length) continue;

                bool matches = true;
                for (int c = 0; c < candidate.Length && matches; c++)
                    matches = Fold(s[i + c]) == Fold(candidate[c]);

                int end = i + candidate.Length;
                if (!matches || (end < s.Length && IsArabicLetter(s[end]))) continue;

                replacement = EgyptianForms[n, 1];
                length = candidate.Length;
                return true;
            }

            replacement = null;
            length = 0;
            return false;
        }

        // Folds the spelling variants an LLM picks between freely, so one table entry covers them all.
        private static char Fold(char c)
        {
            switch (c)
            {
                case 'أ':
                case 'إ':
                case 'آ':
                case 'ٱ': return 'ا';
                case 'ى': return 'ي';
                default: return c;
            }
        }

        // The Arabic block, minus the marks StripDiacritics already removes — used only for word
        // boundaries, so punctuation, spaces and Latin all correctly read as "not a letter".
        private static bool IsArabicLetter(char c) => c >= 'ء' && c <= 'ي';
    }
}
