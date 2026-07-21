using System;

namespace DumyatRuntime
{
    /// <summary>
    /// A speech-to-text model usable by <see cref="STTManager"/>. Streaming/realtime by nature, so
    /// it is event-driven rather than request/response: the manager starts it listening and relays
    /// its transcription events. Concrete providers (e.g. ElevenLabs Scribe) implement this.
    /// </summary>
    public interface ISTTModel : IAIModel
    {
        /// <summary>True while the microphone is being captured/streamed.</summary>
        bool IsListening { get; }

        /// <summary>Fires once with the final transcribed utterance.</summary>
        event Action<string> OnTranscriptionComplete;

        /// <summary>Fires with a reason when transcription cannot be completed.</summary>
        event Action<string> OnTranscriptionFailed;

        /// <summary>Begins capturing and streaming microphone audio.</summary>
        void StartListening();

        /// <summary>Stops capturing and finalises the current utterance.</summary>
        void StopListening();
    }
}
