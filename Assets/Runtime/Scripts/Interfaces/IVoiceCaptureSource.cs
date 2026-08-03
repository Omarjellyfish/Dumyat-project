using System;

namespace DumyatRuntime
{
    /// <summary>
    /// A streaming speech-to-text source that supports a persistent SESSION plus a tunable,
    /// per-utterance MICROPHONE capture (voice-activity or push-to-talk). This is a separate
    /// capability from <see cref="ISTTModel"/> (the manager-pool contract): it exposes the
    /// session/capture surface that a <see cref="VoiceCaptureController"/> needs to drive capture
    /// directly, without depending on the concrete client (e.g. <see cref="ElevenLabsScribeClient"/>,
    /// which implements both).
    ///
    /// SESSION vs UTTERANCE: <see cref="OpenSession"/> opens the transport once and keeps it open
    /// across many utterances; <see cref="SetMicrophoneEnabled"/> toggles capturing one utterance
    /// within that open session. The VAD knobs (<see cref="AutoStopOnSilence"/> and the two silence
    /// thresholds) select and tune how an utterance is finalised.
    /// </summary>
    public interface IVoiceCaptureSource
    {
        /// <summary>Fires once with the final transcribed utterance. The session stays open after.</summary>
        event Action<string> OnTranscriptionComplete;

        /// <summary>
        /// Fires repeatedly mid-utterance with the latest PARTIAL (not-yet-committed) transcript, for a
        /// live caption. Display only — it is a rolling best-guess in the capture language and must never
        /// drive an accept/decline decision (use <see cref="OnTranscriptionComplete"/> for that).
        /// </summary>
        event Action<string> OnPartialTranscript;

        /// <summary>Fires with a human-readable reason when transcription cannot be completed.</summary>
        event Action<string> OnTranscriptionFailed;

        /// <summary>True while the persistent session (transport) is open or connecting.</summary>
        bool IsSessionOpen { get; }

        /// <summary>True while the microphone is enabled (capturing + streaming an utterance).</summary>
        bool MicrophoneEnabled { get; }

        /// <summary>Peak amplitude (0-1) of the most recent captured chunk; 0 when idle. For a live meter.</summary>
        float CurrentInputLevel { get; }

        /// <summary>
        /// Preferred microphone device name; empty = system default (first connected device). Set by
        /// the controller before capture; applied on the next capture.
        /// </summary>
        string MicrophoneDevice { get; set; }

        /// <summary>
        /// True = voice-activity: the utterance auto-finalises after
        /// <see cref="SilenceThresholdSeconds"/> of silence following speech. False = push-to-talk:
        /// the caller finalises by disabling the mic.
        /// </summary>
        bool AutoStopOnSilence { get; set; }

        /// <summary>Seconds of silence (after speech) before a voice-activity utterance auto-finalises.</summary>
        float SilenceThresholdSeconds { get; set; }

        /// <summary>Peak amplitude (0-1) below which audio counts as silence for voice-activity detection.
        /// Used only when <see cref="AutoAdjustSensitivity"/> is false (the manual gate / fallback).</summary>
        float SilenceAmplitudeThreshold { get; set; }

        /// <summary>
        /// Discord-style automatic input sensitivity: when true the voice gate self-calibrates from a
        /// tracked ambient noise floor instead of the fixed <see cref="SilenceAmplitudeThreshold"/>.
        /// </summary>
        bool AutoAdjustSensitivity { get; set; }

        /// <summary>The tracked ambient noise floor (0-1); 0 when idle / not yet calibrated. For a live readout.</summary>
        float CurrentNoiseFloor { get; }

        /// <summary>The effective gate the input level is compared against right now (adaptive or manual).</summary>
        float CurrentGateThreshold { get; }

        /// <summary>Opens the persistent session (idempotent). Returns false if it cannot start.</summary>
        bool OpenSession();

        /// <summary>Closes the persistent session: stops the mic (without committing) and tears down the transport.</summary>
        void CloseSession();

        /// <summary>
        /// Toggles the microphone. Enabling auto-opens the session and begins capturing one utterance;
        /// disabling commits the utterance and leaves the session open for the next one.
        /// </summary>
        void SetMicrophoneEnabled(bool enabled);
    }
}
