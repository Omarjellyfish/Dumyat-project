namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the negotiation orchestrator that the merchant spawner drives. Injected
    /// in place of the former <c>NegotiationFlowController.Singleton</c>. Backed by
    /// <see cref="NegotiationFlowController"/>, which ties together consent, pricing, STT, the LLM
    /// dialogue layer and TTS end-to-end.
    /// </summary>
    public interface INegotiationService
    {
        /// <summary>True while a negotiation (AI or scripted-fallback handoff) is in progress.</summary>
        bool IsNegotiating { get; }

        /// <summary>
        /// Starts a negotiation with <paramref name="merchant"/>. Gates on consent/connectivity; if
        /// voice mode is unavailable it hands to the scripted fallback, otherwise it runs the AI flow.
        /// </summary>
        void BeginNegotiation(MerchantNPC merchant);
    }
}
