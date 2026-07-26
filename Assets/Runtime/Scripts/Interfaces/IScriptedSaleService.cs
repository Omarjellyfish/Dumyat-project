using System;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the SCRIPTED (offline) merchant sale — the no-AI, no-network, no-voice
    /// fallback. <see cref="NegotiationFlowController"/> hands off to this whenever the AI voice flow is
    /// unavailable (consent declined, offline, mic permission missing, LLM/STT failure) or when scripted
    /// mode is forced. Backed by <c>ScriptedSaleController</c>, which drives a button-based catch → quote →
    /// accept/decline loop and settles the sale through the same payout seam the AI path uses.
    /// </summary>
    public interface IScriptedSaleService
    {
        /// <summary>True while a scripted sale is in progress.</summary>
        bool IsRunning { get; }

        /// <summary>
        /// Starts a scripted sale with <paramref name="merchant"/>. <paramref name="onEnded"/> is invoked
        /// exactly once with the final <see cref="NegotiationPhase"/> when the sale resolves (accepted,
        /// declined, gave up, or cancelled) — the caller uses it to tear the negotiation down. Returns
        /// false if the sale could not be started (e.g. already running, or the merchant is misconfigured),
        /// in which case <paramref name="onEnded"/> is NOT called and the caller handles teardown itself.
        /// </summary>
        bool BeginScriptedSale(MerchantNPC merchant, Action<NegotiationPhase> onEnded);

        /// <summary>
        /// Aborts the in-progress scripted sale with <paramref name="merchant"/> (e.g. the player left the
        /// conversation area). Hides the panel, records no sale, and invokes the pending
        /// <c>onEnded</c> callback with <see cref="NegotiationPhase.WalkedAway"/>. No-op if this merchant is
        /// not the one currently selling.
        /// </summary>
        void Cancel(MerchantNPC merchant);
    }
}
