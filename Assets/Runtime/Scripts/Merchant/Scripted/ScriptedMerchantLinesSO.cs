using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Designer-authored dialogue bank for the SCRIPTED (offline) merchant sale — the no-AI, no-network,
    /// no-voice path. Every line the scripted merchant "says" is drawn from here as pure text: it is shown
    /// on the merchant's subtitle panel (English) and never spoken, so this replaces the LLM+TTS layer the
    /// AI flow uses. Each line carries an Arabic string too; the shared TMP font can't render it today, but
    /// it is kept on the data (and on <see cref="MerchantSpokeEvent"/>) so adding an Arabic font later needs
    /// no code change.
    ///
    /// One shared asset is assigned on <see cref="MerchantSystemConfig"/> as the market-wide default; an
    /// individual <see cref="MerchantConfig"/> may override it with its own bank for a distinct persona.
    /// Pure data — no behaviour beyond random line selection.
    /// </summary>
    [CreateAssetMenu(fileName = "ScriptedMerchantLines",
        menuName = "Dumyat/Merchant/Scripted Merchant Lines")]
    public class ScriptedMerchantLinesSO : ScriptableObject
    {
        /// <summary>One scripted line in both languages. English is shown; Arabic is carried for later.</summary>
        [Serializable]
        public struct Line
        {
            [Tooltip("Shown on the subtitle panel.")]
            [TextArea] public string english;

            [Tooltip("Egyptian Arabic equivalent — carried on the event for a future Arabic font; unused today.")]
            [TextArea] public string arabic;
        }

        [Header("Welcome (aim for 50) — greeting + 'put your fish on the scale'")]
        [Tooltip("A random one is picked each time the player starts a scripted sale.")]
        [SerializeField] private Line[] _welcome = Array.Empty<Line>();

        [Header("Placement nudges")]
        [Tooltip("Said when the player signals they're done but the scale is still empty.")]
        [SerializeField] private Line[] _emptyScaleNudge = Array.Empty<Line>();

        [Header("Quote / summary")]
        [Tooltip("Optional lead-in spoken right before the per-fish price breakdown.")]
        [SerializeField] private Line[] _summaryIntro = Array.Empty<Line>();

        [Tooltip("Acknowledges fish ADDED to the scale after the first quote. Use {0} for the fish name(s).")]
        [SerializeField] private Line[] _catchAdded = Array.Empty<Line>();

        [Tooltip("Acknowledges fish REMOVED from the scale after the first quote. Use {0} for the fish name(s).")]
        [SerializeField] private Line[] _catchRemoved = Array.Empty<Line>();

        [Tooltip("Said when the player clears the whole scale mid-quote — nudge to put fish back.")]
        [SerializeField] private Line[] _scaleEmptied = Array.Empty<Line>();

        [Header("Outcomes")]
        [Tooltip("Closing line after the player ACCEPTS.")]
        [SerializeField] private Line[] _accepted = Array.Empty<Line>();

        [Tooltip("Farewell after the player DECLINES.")]
        [SerializeField] private Line[] _declined = Array.Empty<Line>();

        [Tooltip("Said when the merchant gives up (player never loaded the scale / went idle too long).")]
        [SerializeField] private Line[] _gaveUp = Array.Empty<Line>();

        /// <summary>A random welcome line (greeting + place-your-fish prompt).</summary>
        public Line RandomWelcome() => Pick(_welcome, "Welcome, sir — put the fish you want to sell on the scale.");

        /// <summary>A random "the scale is still empty" nudge.</summary>
        public Line RandomEmptyScaleNudge() => Pick(_emptyScaleNudge, "There's nothing on the scale yet, sir.");

        /// <summary>A random lead-in for the price breakdown (may be empty on this asset).</summary>
        public Line RandomSummaryIntro() => Pick(_summaryIntro, "Let me see what you've brought…");

        /// <summary>A random "you added X" line, with <paramref name="fishNames"/> substituted for {0}.</summary>
        public Line RandomCatchAdded(string fishNames) =>
            Format(Pick(_catchAdded, "Ah, you added {0} — let me price it again."), fishNames);

        /// <summary>A random "you took X back" line, with <paramref name="fishNames"/> substituted for {0}.</summary>
        public Line RandomCatchRemoved(string fishNames) =>
            Format(Pick(_catchRemoved, "You took {0} back — here's the new total."), fishNames);

        /// <summary>A random "you cleared the scale, put fish back" line.</summary>
        public Line RandomScaleEmptied() =>
            Pick(_scaleEmptied, "You've cleared the scale — put the fish back and I'll price it.");

        /// <summary>A random accept / deal-closed line.</summary>
        public Line RandomAccepted() =>
            Pick(_accepted, "It's a deal — the catch is mine, the money is yours. God bless your livelihood.");

        /// <summary>A random decline / farewell line.</summary>
        public Line RandomDeclined() => Pick(_declined, "No deal today, sir. Try one of the others.");

        /// <summary>A random gave-up line (no fish placed / went idle).</summary>
        public Line RandomGaveUp() => Pick(_gaveUp, "Come back when you've got fish to sell, sir.");

        // Picks a random line from a bank, falling back to a hard-coded English line (Arabic blank) when the
        // bank is empty, so a half-authored asset never yields a blank subtitle.
        private static Line Pick(IReadOnlyList<Line> bank, string fallbackEnglish)
        {
            if (bank == null || bank.Count == 0)
                return new Line { english = fallbackEnglish, arabic = string.Empty };
            return bank[UnityEngine.Random.Range(0, bank.Count)];
        }

        // Substitutes {0} in both strings. Guarded so a line without a placeholder (or a null arg) is safe.
        private static Line Format(Line line, string arg)
        {
            arg ??= string.Empty;
            return new Line
            {
                english = SafeFormat(line.english, arg),
                arabic = SafeFormat(line.arabic, arg)
            };
        }

        private static string SafeFormat(string template, string arg)
        {
            if (string.IsNullOrEmpty(template)) return template;
            try { return string.Format(template, arg); }
            catch (FormatException) { return template; }   // malformed {..} authored — show as-is
        }
    }
}
