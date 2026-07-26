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
    /// Published by <see cref="Fish.OnCaught"/> the moment a fish is hooked. Lets the player's fish
    /// inventory take ownership of the catch without the fishing system knowing the inventory exists
    /// — the first half of the catch → sell loop that <see cref="FishSaleRecordedEvent"/> closes.
    /// </summary>
    public readonly struct FishCaughtEvent : IGameEvent
    {
        /// <summary>The fish that was just caught. Its species/weight/value are already decided.</summary>
        public readonly Fish Fish;

        public FishCaughtEvent(Fish fish)
        {
            Fish = fish;
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
