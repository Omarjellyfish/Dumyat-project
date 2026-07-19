using System.Threading.Tasks;

namespace DumyatRuntime
{
    /// <summary>The player's answer to a yes/no confirmation, as classified by the LLM.</summary>
    public enum ConfirmationAnswer
    {
        /// <summary>Ambiguous, unrelated, empty, or the classifier was unavailable — ask again.</summary>
        Unclear,

        /// <summary>The player clearly agreed / confirmed.</summary>
        Yes,

        /// <summary>The player clearly declined or wants to change something.</summary>
        No
    }

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
        /// Generates the merchant's OPENING line (greeting + the deterministic opening offer already set
        /// on the state). In the scale-based bundle flow this ACKNOWLEDGES the catch on the scale and
        /// states the opening total. Returns a validated <see cref="MerchantTurnResult"/> or null on failure.
        /// </summary>
        Task<MerchantTurnResult> GenerateOpeningLine(NegotiationState state, bool isDeceptive, bool withGreeting = true);

        /// <summary>
        /// The scale-flow WELCOME line: greet (optionally) and ask the fisherman to load the fish they
        /// want to sell onto the scale, then talk back. Always returns a line (templated fallback on failure).
        /// </summary>
        Task<MerchantTurnResult> GenerateWelcomeLine(NegotiationState state, bool withGreeting);

        /// <summary>
        /// The SALE SUMMARY line: read back the fish on the scale with their per-fish prices
        /// (aligned to <c>state.fishBundle</c>) and the total, and ask the player to confirm.
        /// <paramref name="afterRemoval"/> re-reads a reduced deal after fish were taken off the scale.
        /// Always returns a line (templated fallback on failure).
        /// </summary>
        Task<MerchantTurnResult> GenerateSaleSummaryLine(NegotiationState state, float[] perFishPrices, float total, bool afterRemoval);

        /// <summary>
        /// A light-hearted line for when the player fumbles a fish while placing it. Always returns a line.
        /// </summary>
        Task<MerchantTurnResult> GenerateFumbleLine(NegotiationState state, FishFumbleReason reason);

        /// <summary>Classifies the player's reply to a yes/no confirmation question.</summary>
        Task<ConfirmationAnswer> InterpretConfirmation(string playerSpeechText);
    }
}
