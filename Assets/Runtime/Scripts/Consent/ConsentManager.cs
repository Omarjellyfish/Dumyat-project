using System;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Tri-state consent value. Defaults to <see cref="NotAsked"/> (== 0) so a fresh
    /// PlayerPrefs read yields the correct "never prompted" state.
    /// </summary>
    public enum ConsentStatus
    {
        /// <summary>The player has not been prompted yet this install.</summary>
        NotAsked = 0,

        /// <summary>The player explicitly granted consent.</summary>
        Granted = 1,

        /// <summary>The player explicitly declined consent.</summary>
        Declined = 2
    }

    /// <summary>
    /// Immutable snapshot of both consent flags, handed to callers and event
    /// subscribers so they never touch the manager's internal fields directly.
    /// </summary>
    public readonly struct ConsentState
    {
        /// <summary>Consent for microphone (voice input) access.</summary>
        public readonly ConsentStatus MicConsent;

        /// <summary>Consent for internet connectivity (cloud AI/LLM/TTS calls).</summary>
        public readonly ConsentStatus InternetConsent;

        public ConsentState(ConsentStatus micConsent, ConsentStatus internetConsent)
        {
            MicConsent = micConsent;
            InternetConsent = internetConsent;
        }
    }

    /// <summary>
    /// Central authority for player consent to microphone access and internet
    /// connectivity, both of which the AI merchant negotiation pipeline requires.
    ///
    /// This MUST be checked (via <see cref="IsVoiceModeAvailable"/>) before any
    /// microphone or network code runs. Consent is persisted across sessions in
    /// PlayerPrefs — deliberately separate from the gameplay save so it survives a
    /// save wipe and isn't tied to a playthrough. Other systems should subscribe to
    /// <see cref="OnConsentChanged"/> so they can fall back to scripted dialogue the
    /// instant consent is revoked mid-game (e.g. from a settings menu).
    /// </summary>
    public class ConsentManager : MonoBehaviour, IConsentService
    {
        private const string MIC_CONSENT_KEY = "Dumyat.Consent.Mic";
        private const string INTERNET_CONSENT_KEY = "Dumyat.Consent.Internet";

        // Injected event bus. Consent changes are broadcast as ConsentChangedEvent (replaces the
        // former OnConsentChanged C# event); listeners re-evaluate IsVoiceModeAvailable and switch
        // to fallback mode immediately if voice is no longer available.
        private IEventService _events;

        [Inject]
        private void Inject(IEventService events) => _events = events;

        private ConsentStatus micConsentGranted = ConsentStatus.NotAsked;
        private ConsentStatus internetConsentGranted = ConsentStatus.NotAsked;

        // Callback held while a consent prompt is on-screen, completed when the UI
        // reports the player's choice via SubmitConsentResult.
        private Action<ConsentStatus, ConsentStatus> _pendingRequestCallback;

        private void Awake()
        {
            LoadConsent();
        }

        /// <summary>
        /// Raised when the consent prompt should be shown to the player. The consent UI
        /// (<see cref="ConsentPromptUI"/>) subscribes to this and MUST answer by calling
        /// <see cref="SubmitConsentResult"/> with the player's choices — that completes any
        /// pending <see cref="RequestConsent"/> callback. Event-based so this manager stays
        /// decoupled from whichever UI implementation is in the scene.
        /// </summary>
        public event Action OnConsentUIRequested;

        /// <summary>
        /// Begins the consent flow: shows the consent UI and, once the player responds,
        /// persists the result, fires <see cref="OnConsentChanged"/>, and invokes
        /// <paramref name="onComplete"/> with the final mic and internet statuses.
        ///
        /// If no UI is subscribed to <see cref="OnConsentUIRequested"/>, the request completes
        /// IMMEDIATELY with the current (unchanged) statuses so a caller awaiting the result is
        /// never left hanging on a prompt that can't appear.
        /// </summary>
        public void RequestConsent(Action<ConsentStatus, ConsentStatus> onComplete)
        {
            if (OnConsentUIRequested == null)
            {
                Debug.LogWarning("ConsentManager: consent requested but no consent UI is listening " +
                                 "(add a ConsentPromptUI to the scene); completing with current statuses.");
                onComplete?.Invoke(micConsentGranted, internetConsentGranted);
                return;
            }

            _pendingRequestCallback = onComplete;
            OnConsentUIRequested.Invoke();
        }

        /// <summary>
        /// Called by the consent UI (or test code) to report the player's choice for both
        /// permissions. Persists the result, notifies subscribers, and completes any
        /// pending <see cref="RequestConsent"/> callback. Safe to call even if no request
        /// is pending (acts as a direct update).
        /// </summary>
        public void SubmitConsentResult(ConsentStatus micResult, ConsentStatus internetResult)
        {
            // Defer completing the pending request until ApplyConsent has settled the OS mic permission
            // (which may show an async system prompt), so the caller's callback sees the TRUE voice-mode
            // availability rather than a state where in-app consent is on but the OS grant is still pending.
            ApplyConsent(micResult, internetResult, onApplied: () =>
            {
                Action<ConsentStatus, ConsentStatus> callback = _pendingRequestCallback;
                _pendingRequestCallback = null;
                callback?.Invoke(micConsentGranted, internetConsentGranted);
            });
        }

        /// <summary>
        /// Programmatically sets both consent flags without showing a prompt — e.g. a
        /// settings menu revoking consent mid-game. Persists and fires
        /// <see cref="OnConsentChanged"/>.
        /// </summary>
        public void SetConsent(ConsentStatus micConsent, ConsentStatus internetConsent)
        {
            ApplyConsent(micConsent, internetConsent);
        }

        /// <summary>
        /// Returns the current consent flags without prompting the player.
        /// </summary>
        public ConsentState GetConsentStatus()
        {
            return new ConsentState(micConsentGranted, internetConsentGranted);
        }

        /// <summary>
        /// Wipes the persisted consent so the player counts as never asked — the prompt will
        /// show again on the next session start. Static so editor tooling can call it without
        /// a scene instance. A live instance in Play mode should call
        /// <see cref="SetConsent"/>(<see cref="ConsentStatus.NotAsked"/>, …) to also reset in-memory.
        /// </summary>
        public static void ClearPersistedConsent()
        {
            PlayerPrefs.DeleteKey(MIC_CONSENT_KEY);
            PlayerPrefs.DeleteKey(INTERNET_CONSENT_KEY);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// True only when BOTH microphone and internet consent are
        /// <see cref="ConsentStatus.Granted"/> AND the device currently reports a
        /// reachable network. This is a live check (reachability can change at runtime),
        /// so callers should re-check it before every voice/AI interaction and fall back
        /// to scripted dialogue when it returns false.
        /// </summary>
        public bool IsVoiceModeAvailable()
        {
            bool bothGranted = micConsentGranted == ConsentStatus.Granted
                               && internetConsentGranted == ConsentStatus.Granted;

            bool networkReachable = Application.internetReachability != NetworkReachability.NotReachable;

            // The in-app grant is not enough: capture also needs the OS-level microphone permission
            // (Android RECORD_AUDIO), or Microphone.devices stays empty and capture fails silently.
            // Always true on Editor/Standalone (see MicrophonePermission).
            return bothGranted && networkReachable && MicrophonePermission.IsGranted;
        }

        // Shared apply path for both UI results and programmatic updates: writes the fields, persists to
        // PlayerPrefs, secures the OS mic permission when consent is granted, and notifies subscribers.
        // onApplied fires once the state is fully settled (after any async OS permission prompt).
        private void ApplyConsent(ConsentStatus micConsent, ConsentStatus internetConsent, Action onApplied = null)
        {
            bool changed = micConsent != micConsentGranted || internetConsent != internetConsentGranted;

            micConsentGranted = micConsent;
            internetConsentGranted = internetConsent;
            SaveConsent();

            // Securing the OS microphone permission is part of granting mic consent — the in-app toggle
            // alone leaves Microphone.devices empty. Request it now (immediate/no-op off Android and when
            // already held), and hold the change notification + completion until the OS decision is in so
            // every listener re-evaluating IsVoiceModeAvailable() sees the real state.
            if (micConsentGranted == ConsentStatus.Granted && !MicrophonePermission.IsGranted)
            {
                MicrophonePermission.Request(granted =>
                {
                    if (!granted)
                        Debug.LogWarning("ConsentManager: in-app microphone consent was granted but the OS " +
                                         "microphone permission was denied — voice mode stays unavailable " +
                                         "until it is allowed in the device's app permission settings.");

                    _events?.Publish(new ConsentChangedEvent(new ConsentState(micConsentGranted, internetConsentGranted)));
                    onApplied?.Invoke();
                });
                return;
            }

            if (changed)
            {
                _events?.Publish(new ConsentChangedEvent(new ConsentState(micConsentGranted, internetConsentGranted)));
            }
            onApplied?.Invoke();
        }

        private void LoadConsent()
        {
            //micConsentGranted = (ConsentStatus)PlayerPrefs.GetInt(MIC_CONSENT_KEY, (int)ConsentStatus.NotAsked);
            //internetConsentGranted = (ConsentStatus)PlayerPrefs.GetInt(INTERNET_CONSENT_KEY, (int)ConsentStatus.NotAsked);
        }

        private void SaveConsent()
        {
            PlayerPrefs.SetInt(MIC_CONSENT_KEY, (int)micConsentGranted);
            PlayerPrefs.SetInt(INTERNET_CONSENT_KEY, (int)internetConsentGranted);
            PlayerPrefs.Save();
        }
    }
}
