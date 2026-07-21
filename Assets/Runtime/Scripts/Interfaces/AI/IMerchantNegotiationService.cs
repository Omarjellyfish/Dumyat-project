using System.Threading.Tasks;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the LLM dialogue layer of a merchant negotiation. Injected in place of
    /// the former <c>MerchantNegotiationLLM.Singleton</c>. Backed by <see cref="MerchantNegotiationLLM"/>,
    /// which owns prompt construction, the JSON contract, validation and the structured-output retry.
    /// Unrecoverable turn failures are broadcast via <see cref="LLMResponseFailedEvent"/> on the event bus.
    /// </summary>
    public interface IMerchantNegotiationService
    {
        /// <summary>
        /// Runs one negotiation turn through the LLM and returns a validated
        /// <see cref="MerchantTurnResult"/>, or null if it could not be produced.
        /// </summary>
        Task<MerchantTurnResult> ProcessPlayerTurn(NegotiationState state, string playerSpeechText, bool isDeceptive);

        /// <summary>
        /// Generates the merchant's OPENING line (greeting + the deterministic opening offer already
        /// set on the state). Returns a validated <see cref="MerchantTurnResult"/> or null on failure.
        /// </summary>
        Task<MerchantTurnResult> GenerateOpeningLine(NegotiationState state, bool isDeceptive, bool withGreeting = true);
    }
}
