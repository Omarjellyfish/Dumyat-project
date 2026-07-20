using System.Collections.Generic;
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
        /// (aligned to <c>state.fishBundle</c>) and the total, and ask the player to confirm OR keep haggling.
        /// <paramref name="addedFish"/> / <paramref name="removedFish"/> name any fish the player just put on
        /// / took off since the last summary, so the merchant ACKNOWLEDGES the change and reads the updated
        /// deal (either may be empty). Always returns a line (templated fallback on failure).
        /// </summary>
        Task<MerchantTurnResult> GenerateSaleSummaryLine(NegotiationState state, float[] perFishPrices, float total,
            IReadOnlyList<string> addedFish, IReadOnlyList<string> removedFish);

        /// <summary>
        /// A light-hearted line for when the player fumbles a fish while placing it. Always returns a line.
        /// </summary>
        Task<MerchantTurnResult> GenerateFumbleLine(NegotiationState state, FishFumbleReason reason);

        /// <summary>
        /// The CLOSING line, spoken only AFTER the player has confirmed the per-fish summary: the whole-catch
        /// deal is now truly agreed at <paramref name="total"/>, so the merchant warmly seals it (it's a deal,
        /// the sale is done, a blessing) and the conversation ends. Always returns a line (templated fallback).
        /// </summary>
        Task<MerchantTurnResult> GenerateSaleClosedLine(NegotiationState state, float total);

        /// <summary>
        /// The "is that your whole catch?" line: fish are on the scale, but before pricing them the
        /// merchant asks the fisherman to confirm they have placed EVERYTHING they want to sell (a yes/no
        /// answer) or add more. Always returns a line (templated fallback on failure).
        /// </summary>
        Task<MerchantTurnResult> GenerateAllPlacedPromptLine(NegotiationState state);

        /// <summary>
        /// Classifies the player's reply to a yes/no confirmation question. <paramref name="phase"/>
        /// tells the classifier WHICH question was asked so the same words are judged against the right
        /// intent (e.g. "this is all I have" = a "yes" in <see cref="MerchantPhase.PlacementConfirm"/> but
        /// not necessarily in <see cref="MerchantPhase.SaleConfirm"/>). Only the two confirmation phases
        /// are valid here.
        /// </summary>
        Task<ConfirmationAnswer> InterpretConfirmation(string playerSpeechText,
            MerchantPhase phase = MerchantPhase.SaleConfirm);
    }
}
