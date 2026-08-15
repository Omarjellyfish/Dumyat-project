using System;
using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Published by <c>NegotiationFlowController</c> the moment a merchant line is settled — before the
    /// audio starts, so a subtitle never waits on TTS. Carries both languages: the merchant SPEAKS the
    /// Arabic and the subtitle SHOWS the English. Listeners must filter on <see cref="Merchant"/>, since
    /// every merchant in the market publishes on the same channel.
    /// </summary>
    public readonly struct MerchantSpokeEvent : IGameEvent
    {
        /// <summary>The merchant saying the line.</summary>
        public readonly MerchantNPC Merchant;

        /// <summary>English translation — what a subtitle shows.</summary>
        public readonly string English;

        /// <summary>Egyptian Arabic — what the merchant actually says through TTS.</summary>
        public readonly string Arabic;

        /// <summary>
        /// When true, the subtitle should appear in FULL at once instead of typing character-by-character.
        /// The scripted (offline) sale sets this for its long per-fish price read-back, which has no voice
        /// to pace the type-out against. The AI path leaves it false (the default), so the caption keeps
        /// typing in step with the merchant's speech.
        /// </summary>
        public readonly bool Instant;

        public MerchantSpokeEvent(MerchantNPC merchant, string english, string arabic)
            : this(merchant, english, arabic, false) { }

        public MerchantSpokeEvent(MerchantNPC merchant, string english, string arabic, bool instant)
        {
            Merchant = merchant;
            English = english;
            Arabic = arabic;
            Instant = instant;
        }
    }

    /// <summary>
    /// Published when a merchant's negotiation tears down, whatever the outcome. Cue to clear anything
    /// showing that merchant's dialogue.
    /// </summary>
    public readonly struct MerchantStoppedSpeakingEvent : IGameEvent
    {
        public readonly MerchantNPC Merchant;

        public MerchantStoppedSpeakingEvent(MerchantNPC merchant)
        {
            Merchant = merchant;
        }
    }

    /// <summary>
    /// A DEAL CLOSED — one event per accepted sale, whatever its size. Carries the aggregate SELLING SCORE
    /// (how close the final total came to the catch's maximum fair value:
    /// <c>Σ agreedPrice / Σ fairMarketValue × 100</c>, clamped 0–100) and <see cref="Records"/>, the row for
    /// every fish in the deal. <see cref="SaleScoreNotifier"/> maps the score to a rank notification.
    ///
    /// THIS is the channel for anything reacting to the player's ACTION. <see cref="FishSaleRecordedEvent"/>
    /// is the ledger row and fires once per FISH, which is not the same thing: handing four fish across the
    /// scale is one deal the player struck, and four rows in the books. The wallet has always credited from
    /// here for that reason; quests advance by the whole bundle in one step from here too. Counting the
    /// per-fish event instead is what made a four-fish sale tick a quest four separate times — four HUD
    /// cards for one hand-over, with the tick that actually finished the quest arriving on its own.
    /// </summary>
    public readonly struct SellScoreEvent : IGameEvent
    {
        /// <summary>0–100: how close the sale total came to the maximum fair value.</summary>
        public readonly float Score;

        /// <summary>Total paid for the sale, whole Egyptian Pounds.</summary>
        public readonly float Total;

        /// <summary>
        /// One row per fish in this deal, in the order they were settled. Never null; a listener that only
        /// needs the size reads <see cref="FishCount"/>, one that has to tell species apart (a "sell 5
        /// Bouri" quest) reads the rows.
        /// </summary>
        public readonly IReadOnlyList<FishSaleRecord> Records;

        /// <summary>Number of fish in the sale.</summary>
        public int FishCount => Records.Count;

        public SellScoreEvent(float score, float total, IReadOnlyList<FishSaleRecord> records)
        {
            Score = score;
            Total = total;
            Records = records ?? Array.Empty<FishSaleRecord>();
        }
    }

    /// <summary>
    /// Published by <c>NegotiationFlowController</c> with the player's live PARTIAL speech transcript while
    /// they are still talking, so a player-side caption can show what they're saying in real time. Display
    /// only — the negotiation decision ALWAYS uses the final transcript, never this rolling guess. An empty
    /// or whitespace <see cref="Text"/> means "clear the caption" (the turn ended or the final arrived).
    /// The text is the player's own words in the capture language (Egyptian Arabic), so rendering it needs
    /// an Arabic-capable font.
    /// </summary>
    public readonly struct PlayerPartialTranscriptEvent : IGameEvent
    {
        /// <summary>The latest partial transcript, or empty/whitespace to clear the caption.</summary>
        public readonly string Text;

        public PlayerPartialTranscriptEvent(string text)
        {
            Text = text;
        }
    }

    /// <summary>
    /// Published by <see cref="PlayerStats"/> when a fish sale is recorded (an accepted deal). Carries
    /// the authoritative sale record so the sell process / inventory / UI can react (credit money,
    /// remove the fish, refresh HUD). Replaces the former static <c>PlayerStats.OnSaleRecorded</c>.
    /// </summary>
    public readonly struct FishSaleRecordedEvent : IGameEvent
    {
        public readonly FishSaleRecord Record;

        public FishSaleRecordedEvent(FishSaleRecord record)
        {
            Record = record;
        }
    }

    /// <summary>
    /// Published by <see cref="ConsentManager"/> whenever either consent flag changes (granted,
    /// declined, or revoked). Listeners re-evaluate voice availability and switch to fallback mode
    /// immediately if voice is no longer available. Replaces the former <c>ConsentManager.OnConsentChanged</c>.
    /// </summary>
    public readonly struct ConsentChangedEvent : IGameEvent
    {
        public readonly ConsentState State;

        public ConsentChangedEvent(ConsentState state)
        {
            State = state;
        }
    }

    /// <summary>
    /// Published by <see cref="MerchantNegotiationLLM"/> when a turn cannot produce a valid result
    /// after the retry. The negotiation system should respond with a generic scripted line. Replaces
    /// the former <c>MerchantNegotiationLLM.OnLLMResponseFailed</c>.
    /// </summary>
    public readonly struct LLMResponseFailedEvent : IGameEvent
    {
        public readonly string Reason;

        public LLMResponseFailedEvent(string reason)
        {
            Reason = reason;
        }
    }

    /// <summary>
    /// Published by a <see cref="FishSellScale"/> whenever the set of fish physically resting on it
    /// changes (a fish placed or removed). Carries the scale so a listener can re-query
    /// <see cref="ISellScale.FishOnScale"/>; the negotiation compares <see cref="Scale"/> against its
    /// merchant's own scale, since every scale publishes on the same channel.
    /// </summary>
    public readonly struct ScaleContentsChangedEvent : IGameEvent
    {
        /// <summary>The scale whose contents changed.</summary>
        public readonly ISellScale Scale;

        public ScaleContentsChangedEvent(ISellScale scale)
        {
            Scale = scale;
        }
    }

    /// <summary>
    /// Published by <c>NegotiationFlowController</c> the moment the merchant VOICES a bundle price — a
    /// raise, an acceptance, or a sale-confirm summary read-back. Lets a scale-side readout (the market
    /// ticket) show ONLY the number the merchant has actually called out: never the hidden fair value,
    /// and never before he says it. Carries the scale so a per-scale listener can match its own, exactly
    /// like <see cref="ScaleContentsChangedEvent"/> (every merchant publishes on the same channel).
    /// </summary>
    public readonly struct MerchantOfferedPriceEvent : IGameEvent
    {
        /// <summary>The merchant voicing the price (filter key — all merchants share the channel).</summary>
        public readonly MerchantNPC Merchant;

        /// <summary>The merchant's sell scale, so a per-scale ticket can match its own.</summary>
        public readonly ISellScale Scale;

        /// <summary>The bundle total the merchant just called out, in whole Egyptian Pounds.</summary>
        public readonly float Amount;

        public MerchantOfferedPriceEvent(MerchantNPC merchant, ISellScale scale, float amount)
        {
            Merchant = merchant;
            Scale = scale;
            Amount = amount;
        }
    }

    /// <summary>Why a fish counted as a fumble (drives the merchant's funny reaction line).</summary>
    public enum FishFumbleReason
    {
        /// <summary>The player threw/dropped a fish that struck the merchant's body.</summary>
        HitMerchant,

        /// <summary>A fish was released while placing but did not come to rest on the sell scale.</summary>
        MissedScale
    }

    /// <summary>
    /// Published when the player fumbles a fish while trying to place it on a merchant's sell scale —
    /// either it hit the merchant (<see cref="FishFumbleReason.HitMerchant"/>) or it was dropped and
    /// missed the scale (<see cref="FishFumbleReason.MissedScale"/>). The negotiation, while awaiting
    /// placement, reacts with a light-hearted line.
    /// </summary>
    public readonly struct FishFumbledEvent : IGameEvent
    {
        /// <summary>The fumbled fish (may be null if it was already destroyed).</summary>
        public readonly Fish Fish;

        /// <summary>What kind of fumble this was.</summary>
        public readonly FishFumbleReason Reason;

        public FishFumbledEvent(Fish fish, FishFumbleReason reason)
        {
            Fish = fish;
            Reason = reason;
        }
    }

    /// <summary>
    /// One button the SCRIPTED (offline) merchant sale can present to the player. The scripted flow reads
    /// player intent ONLY from these explicit choices — never from parsed text — so the button IS the
    /// decision (see CLAUDE.md's no-keyword-intent rule).
    /// </summary>
    public enum ScriptedChoice
    {
        /// <summary>"I'm done" — the player has finished loading the scale and wants a price.</summary>
        ConfirmPlacement,

        /// <summary>The player accepts the merchant's flat quote; the sale closes.</summary>
        Accept,

        /// <summary>The player declines the quote and ends the sale (to try another merchant).</summary>
        Decline
    }

    /// <summary>
    /// Published by <c>ScriptedSaleController</c> when it needs the player to choose from a set of on-screen
    /// buttons. The scripted sale's button panel (<c>ScriptedSalePanel</c>) shows exactly
    /// <see cref="Options"/> and hides everything else. Empty <see cref="Options"/> means "hide all buttons".
    /// Filter on <see cref="Merchant"/> — every merchant shares the channel.
    /// </summary>
    public readonly struct ScriptedChoiceRequestedEvent : IGameEvent
    {
        /// <summary>The merchant asking for a choice (filter key).</summary>
        public readonly MerchantNPC Merchant;

        /// <summary>The buttons to show, in display order. Empty = hide all.</summary>
        public readonly ScriptedChoice[] Options;

        public ScriptedChoiceRequestedEvent(MerchantNPC merchant, ScriptedChoice[] options)
        {
            Merchant = merchant;
            Options = options;
        }
    }

    /// <summary>
    /// Published by <c>ScriptedSalePanel</c> when the player clicks one of the scripted-sale buttons.
    /// <c>ScriptedSaleController</c> listens (filtering on its own merchant) and advances the flow.
    /// </summary>
    public readonly struct ScriptedChoiceMadeEvent : IGameEvent
    {
        /// <summary>The merchant whose panel was clicked (filter key).</summary>
        public readonly MerchantNPC Merchant;

        /// <summary>The choice the player made.</summary>
        public readonly ScriptedChoice Choice;

        public ScriptedChoiceMadeEvent(MerchantNPC merchant, ScriptedChoice choice)
        {
            Merchant = merchant;
            Choice = choice;
        }
    }
}
