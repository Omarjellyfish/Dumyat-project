using System.Collections.Generic;
using System.Text;

namespace DumyatRuntime
{
    /// <summary>
    /// Turns logical Arabic into the connected letterforms TextMeshPro needs.
    ///
    /// WHY THIS EXISTS: TMP (com.unity.ugui 2.0) has no Arabic shaping. It renders each codepoint's
    /// default glyph, so Arabic comes out as disconnected isolated letters. UI Toolkit shapes natively
    /// — this is only needed on the TMP side (<see cref="SubtitlePanel"/> and friends).
    ///
    /// WHAT IT DOES: replaces each letter with its contextual presentation form (U+FE70–U+FEFF) based
    /// on whether its neighbours join, and folds lam+alef into the single ligature glyph Arabic
    /// requires. Output stays in LOGICAL order.
    ///
    /// HOW TO USE IT — both halves are required:
    /// <code>
    /// text.isRightToLeftText = ArabicTextShaper.ContainsArabic(s);
    /// text.text = ArabicTextShaper.Shape(s);
    /// </code>
    /// This class does NOT reorder the string. TMP's own right-to-left mode does the ordering during
    /// layout (it advances leftward rather than pre-reversing the text), which is what keeps word
    /// WRAPPING correct — a pre-reversed string would wrap as if it were English and break lines in
    /// the wrong places. The one thing that does get reordered here is embedded Latin/digit runs; see
    /// <see cref="PreReverseLatinRuns"/>.
    ///
    /// Isolated letters are emitted as the BASE codepoint rather than the isolated presentation form.
    /// They are the same glyph, and Cairo — like most modern OpenType fonts, which shape via GSUB
    /// rather than the legacy block — is missing a few isolated forms (U+FE8D alef, U+FEA9 dal).
    /// Going through the base codepoint sidesteps those gaps entirely.
    /// </summary>
    public static class ArabicTextShaper
    {
        // Contextual forms for one letter. Isolated is deliberately absent — see the class summary.
        // A right-joining letter (alef, dal, ra, waw…) connects to the letter BEFORE it but never to
        // the one after, so it has no initial or medial form and stores '\0' for both.
        private readonly struct Forms
        {
            public readonly char Final;
            public readonly char Initial;
            public readonly char Medial;

            public Forms(char final, char initial = '\0', char medial = '\0')
            {
                Final = final;
                Initial = initial;
                Medial = medial;
            }

            /// <summary>True when this letter can also connect forward, to the letter after it.</summary>
            public bool JoinsForward => Initial != '\0';
        }

        private const char Tatweel = 'ـ';   // the kashida connector: joins on both sides, no forms
        private const char Lam = 'ل';

        private static readonly Dictionary<char, Forms> Table = new Dictionary<char, Forms>
        {
            // --- Dual-joining: connect on both sides -------------------------------
            { 'ب', new Forms('ﺐ', 'ﺑ', 'ﺒ') },   // beh
            { 'ت', new Forms('ﺖ', 'ﺗ', 'ﺘ') },   // teh
            { 'ث', new Forms('ﺚ', 'ﺛ', 'ﺜ') },   // theh
            { 'ج', new Forms('ﺞ', 'ﺟ', 'ﺠ') },   // jeem
            { 'ح', new Forms('ﺢ', 'ﺣ', 'ﺤ') },   // hah
            { 'خ', new Forms('ﺦ', 'ﺧ', 'ﺨ') },   // khah
            { 'س', new Forms('ﺲ', 'ﺳ', 'ﺴ') },   // seen
            { 'ش', new Forms('ﺶ', 'ﺷ', 'ﺸ') },   // sheen
            { 'ص', new Forms('ﺺ', 'ﺻ', 'ﺼ') },   // sad
            { 'ض', new Forms('ﺾ', 'ﺿ', 'ﻀ') },   // dad
            { 'ط', new Forms('ﻂ', 'ﻃ', 'ﻄ') },   // tah
            { 'ظ', new Forms('ﻆ', 'ﻇ', 'ﻈ') },   // zah
            { 'ع', new Forms('ﻊ', 'ﻋ', 'ﻌ') },   // ain
            { 'غ', new Forms('ﻎ', 'ﻏ', 'ﻐ') },   // ghain
            { 'ف', new Forms('ﻒ', 'ﻓ', 'ﻔ') },   // feh
            { 'ق', new Forms('ﻖ', 'ﻗ', 'ﻘ') },   // qaf
            { 'ك', new Forms('ﻚ', 'ﻛ', 'ﻜ') },   // kaf
            { 'ل', new Forms('ﻞ', 'ﻟ', 'ﻠ') },   // lam
            { 'م', new Forms('ﻢ', 'ﻣ', 'ﻤ') },   // meem
            { 'ن', new Forms('ﻦ', 'ﻧ', 'ﻨ') },   // noon
            { 'ه', new Forms('ﻪ', 'ﻫ', 'ﻬ') },   // heh
            { 'ي', new Forms('ﻲ', 'ﻳ', 'ﻴ') },   // yeh
            { 'ئ', new Forms('ﺊ', 'ﺋ', 'ﺌ') },   // yeh with hamza

            // --- Right-joining: connect only to the PRECEDING letter ---------------
            { 'ا', new Forms('ﺎ') },   // alef
            { 'آ', new Forms('ﺂ') },   // alef madda
            { 'أ', new Forms('ﺄ') },   // alef hamza above
            { 'إ', new Forms('ﺈ') },   // alef hamza below
            { 'ؤ', new Forms('ﺆ') },   // waw with hamza
            { 'د', new Forms('ﺪ') },   // dal
            { 'ذ', new Forms('ﺬ') },   // thal
            { 'ر', new Forms('ﺮ') },   // reh
            { 'ز', new Forms('ﺰ') },   // zain
            { 'و', new Forms('ﻮ') },   // waw
            { 'ة', new Forms('ﺔ') },   // teh marbuta
            { 'ى', new Forms('ﻰ') },   // alef maksura

            // --- Non-joining: hamza stands alone -----------------------------------
            { 'ء', new Forms('\0') },       // hamza
        };

        // lam + alef must become one glyph; rendering them as two separate letters is wrong Arabic,
        // not merely ugly. Keyed on the alef that follows a lam → (isolated, final).
        private static readonly Dictionary<char, (char Isolated, char Final)> LamAlef =
            new Dictionary<char, (char, char)>
            {
                { 'آ', ('ﻵ', 'ﻶ') },   // lam + alef madda
                { 'أ', ('ﻷ', 'ﻸ') },   // lam + alef hamza above
                { 'إ', ('ﻹ', 'ﻺ') },   // lam + alef hamza below
                { 'ا', ('ﻻ', 'ﻼ') },   // lam + alef
            };

        /// <summary>
        /// True if the string contains any Arabic-script character. Use it to decide whether to turn
        /// on <c>isRightToLeftText</c> — a Latin-only line must stay left-to-right.
        /// </summary>
        public static bool ContainsArabic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;

            for (int i = 0; i < s.Length; i++)
                if (IsArabicScript(s[i])) return true;

            return false;
        }

        /// <summary>
        /// Rewrites <paramref name="logical"/> into connected presentation forms, in logical order.
        /// Returns the input unchanged when it holds no Arabic, so it is safe to call on every line.
        /// </summary>
        public static string Shape(string logical)
        {
            if (!ContainsArabic(logical)) return logical;

            var sb = new StringBuilder(logical.Length);

            for (int i = 0; i < logical.Length; i++)
            {
                char c = logical[i];

                // Harakat and other combining marks sit ON a letter — they neither join nor break a
                // join, so they pass through and are skipped when looking at neighbours.
                if (IsTransparent(c))
                {
                    sb.Append(c);
                    continue;
                }

                if (!Table.TryGetValue(c, out Forms forms))
                {
                    // Latin, digits, punctuation, tatweel, anything else — untouched.
                    sb.Append(c);
                    continue;
                }

                bool joinsToPrevious = PreviousJoinsForward(logical, i);

                // lam + alef: one ligature glyph, and the alef is consumed.
                if (c == Lam)
                {
                    int alefIndex = NextSignificantIndex(logical, i);
                    if (alefIndex >= 0 && LamAlef.TryGetValue(logical[alefIndex], out var ligature))
                    {
                        sb.Append(joinsToPrevious ? ligature.Final : ligature.Isolated);
                        i = alefIndex;   // the alef is now part of the glyph just written
                        continue;
                    }
                }

                bool joinsToNext = forms.JoinsForward && NextAcceptsJoin(logical, i);

                if (joinsToPrevious && joinsToNext) sb.Append(forms.Medial);
                else if (joinsToPrevious && forms.Final != '\0') sb.Append(forms.Final);
                else if (joinsToNext) sb.Append(forms.Initial);
                else sb.Append(c);   // isolated → the base codepoint, see the class summary
            }

            return PreReverseLatinRuns(sb.ToString());
        }

        // --- Neighbour inspection ---------------------------------------------------

        // True when the letter before index (skipping combining marks) can connect FORWARD to it.
        private static bool PreviousJoinsForward(string s, int index)
        {
            int i = PreviousSignificantIndex(s, index);
            if (i < 0) return false;

            char c = s[i];
            if (c == Tatweel) return true;

            return Table.TryGetValue(c, out Forms forms) && forms.JoinsForward;
        }

        // True when the character after index (skipping combining marks) is an Arabic letter, which
        // every Arabic letter is willing to be connected to from its right.
        private static bool NextAcceptsJoin(string s, int index)
        {
            int i = NextSignificantIndex(s, index);
            if (i < 0) return false;

            char c = s[i];
            return c == Tatweel || Table.ContainsKey(c);
        }

        private static int PreviousSignificantIndex(string s, int index)
        {
            for (int i = index - 1; i >= 0; i--)
                if (!IsTransparent(s[i])) return i;

            return -1;
        }

        private static int NextSignificantIndex(string s, int index)
        {
            for (int i = index + 1; i < s.Length; i++)
                if (!IsTransparent(s[i])) return i;

            return -1;
        }

        // --- Character classes ------------------------------------------------------

        // Combining marks that hang off a letter without affecting how it joins: harakat, tanwin,
        // shadda, sukun and the extended marks (U+064B–U+065F), the Quranic annotation range
        // (U+0610–U+061A), and the superscript alef (U+0670).
        private static bool IsTransparent(char c) =>
            (c >= 'ً' && c <= 'ٟ') ||
            (c >= 'ؐ' && c <= 'ؚ') ||
            c == 'ٰ';

        private static bool IsArabicScript(char c) =>
            (c >= '؀' && c <= 'ۿ') ||   // Arabic
            (c >= 'ݐ' && c <= 'ݿ') ||   // Arabic Supplement
            (c >= 'ﭐ' && c <= '﷿') ||   // Presentation Forms-A
            (c >= 'ﹰ' && c <= '﻿');     // Presentation Forms-B

        private static bool IsLatinOrDigit(char c) =>
            (c >= '0' && c <= '9') ||
            (c >= 'A' && c <= 'Z') ||
            (c >= 'a' && c <= 'z');

        // Characters allowed INSIDE a Latin/digit run, but only between two run characters — so the
        // '.' in "40.50" is part of the run while a sentence-ending '.' is not.
        private static bool IsRunConnector(char c) =>
            c == '.' || c == ',' || c == ':' || c == '/' || c == '-' || c == '\'';

        /// <summary>
        /// Reverses each embedded Latin/digit run in place.
        ///
        /// TMP's right-to-left mode lays out EVERY character leftward, which mirrors embedded Latin and
        /// numbers too — a price of "40" would render as "04", which is not a cosmetic problem when the
        /// player is haggling over it. Reversing those runs here means TMP's own reversal restores them
        /// to reading order. Arabic characters are left alone: TMP is what orders them.
        /// </summary>
        private static string PreReverseLatinRuns(string s)
        {
            char[] chars = s.ToCharArray();

            int i = 0;
            while (i < chars.Length)
            {
                if (!IsLatinOrDigit(chars[i])) { i++; continue; }

                int start = i;
                int end = i;
                int j = i;

                while (j < chars.Length)
                {
                    if (IsLatinOrDigit(chars[j]))
                    {
                        end = j;
                        j++;
                    }
                    else if (IsRunConnector(chars[j]) && j + 1 < chars.Length && IsLatinOrDigit(chars[j + 1]))
                    {
                        j++;   // a separator inside the run, e.g. the '.' in "40.50"
                    }
                    else break;
                }

                System.Array.Reverse(chars, start, end - start + 1);
                i = end + 1;
            }

            return new string(chars);
        }
    }
}
