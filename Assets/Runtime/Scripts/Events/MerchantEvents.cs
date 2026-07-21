namespace DumyatRuntime
{
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
