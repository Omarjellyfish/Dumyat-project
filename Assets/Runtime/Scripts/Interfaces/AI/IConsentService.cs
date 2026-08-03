using System;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for player consent to microphone + internet access, required by the AI
    /// merchant pipeline. Injected in place of the former <c>ConsentManager.Singleton</c>. Backed by
    /// <see cref="ConsentManager"/>. Consent changes are broadcast via <see cref="ConsentChangedEvent"/>
    /// on the event bus (subscribe to that rather than a C# event on this interface).
    /// </summary>
    public interface IConsentService
    {
        /// <summary>
        /// Raised when the consent prompt should be shown. The consent UI subscribes and MUST answer
        /// by calling <see cref="SubmitConsentResult"/> with the player's choices.
        /// </summary>
        event Action OnConsentUIRequested;

        /// <summary>
        /// True only when BOTH microphone and internet consent are granted AND the device currently
        /// reports a reachable network. Re-check before every voice/AI interaction.
        /// </summary>
        bool IsVoiceModeAvailable();

        /// <summary>Returns the current consent flags without prompting the player.</summary>
        ConsentState GetConsentStatus();

        /// <summary>
        /// Begins the consent flow: shows the UI and, once the player responds, persists the result,
        /// publishes <see cref="ConsentChangedEvent"/>, and invokes <paramref name="onComplete"/>.
        /// </summary>
        void RequestConsent(Action<ConsentStatus, ConsentStatus> onComplete);

        /// <summary>Reports the player's choice for both permissions (from the consent UI or test code).</summary>
        void SubmitConsentResult(ConsentStatus micResult, ConsentStatus internetResult);

        /// <summary>Programmatically sets both consent flags without showing a prompt.</summary>
        void SetConsent(ConsentStatus micConsent, ConsentStatus internetConsent);
    }
}
