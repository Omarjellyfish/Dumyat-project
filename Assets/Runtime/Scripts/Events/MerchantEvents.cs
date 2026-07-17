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

        public MerchantSpokeEvent(MerchantNPC merchant, string english, string arabic)
        {
            Merchant = merchant;
            English = english;
            Arabic = arabic;
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
}
