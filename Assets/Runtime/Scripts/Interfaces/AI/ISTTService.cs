using System;
using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the pooled speech-to-text service. Injected in place of the former
    /// <c>STTManager.Singleton</c>. Backed by <see cref="STTManager"/>, which selects the
    /// highest-priority available <see cref="ISTTModel"/> per utterance and relays its events.
    /// </summary>
    public interface ISTTService
    {
        /// <summary>True while the active model is capturing/streaming.</summary>
        bool IsListening { get; }

        /// <summary>Language code applied to the active STT model; empty = auto-detect.</summary>
        string LanguageCode { get; set; }

        /// <summary>All registered STT models, highest priority first.</summary>
        IReadOnlyList<ISTTModel> Models { get; }

        /// <summary>Relayed from the active model: fires once with the final transcript.</summary>
        event Action<string> OnTranscriptionComplete;

        /// <summary>Relayed from the active model: fires with a reason on failure.</summary>
        event Action<string> OnTranscriptionFailed;

        /// <summary>Selects the best available STT model and starts it listening. No-op if already listening.</summary>
        void StartListening();

        /// <summary>Asks the active model to finalise the current utterance.</summary>
        void StopListening();
    }
}
