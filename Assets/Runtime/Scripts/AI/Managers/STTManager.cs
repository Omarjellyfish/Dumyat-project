using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Manages the pool of <see cref="ISTTModel"/> providers and exposes a single listen API,
    /// re-broadcasting the active model's transcription events. Selection uses descending priority:
    /// <see cref="StartListening"/> picks the highest-priority AVAILABLE model for the utterance.
    ///
    /// NOTE on fallback: because STT is a live audio stream, fallback here is selection-time
    /// (choose the best available provider when listening starts), NOT mid-utterance switching —
    /// switching providers part-way through captured speech isn't meaningful. A failure is surfaced
    /// via <see cref="OnTranscriptionFailed"/>; the negotiation flow's recovery decides what next.
    /// </summary>
    public class STTManager : AIModelManager<STTManager, ISTTModel>
    {
        /// <summary>Relayed from the active model: fires once with the final transcript.</summary>
        public event Action<string> OnTranscriptionComplete;

        /// <summary>Relayed from the active model: fires with a reason on failure.</summary>
        public event Action<string> OnTranscriptionFailed;

        private ISTTModel _active;

        /// <summary>True while the active model is capturing/streaming.</summary>
        public bool IsListening => _active != null && _active.IsListening;

        /// <summary>Selects the best available STT model and starts it listening. No-op if already listening.</summary>
        public void StartListening()
        {
            if (_active != null)
            {
                Debug.LogWarning("STTManager: already listening; ignoring StartListening.");
                return;
            }

            foreach (ISTTModel model in AvailableModels())
            {
                _active = model;
                break;
            }

            if (_active == null)
            {
                OnTranscriptionFailed?.Invoke("No STT model available.");
                return;
            }

            _active.OnTranscriptionComplete += HandleComplete;
            _active.OnTranscriptionFailed += HandleFailed;
            Debug.Log($"STTManager: listening via '{_active.ModelName}'.");
            _active.StartListening();
        }

        /// <summary>Asks the active model to finalise the current utterance.</summary>
        public void StopListening()
        {
            _active?.StopListening();
        }

        private void HandleComplete(string text)
        {
            Detach();
            OnTranscriptionComplete?.Invoke(text);
        }

        private void HandleFailed(string reason)
        {
            Detach();
            OnTranscriptionFailed?.Invoke(reason);
        }

        // Unsubscribe from and release the active model once an utterance resolves.
        private void Detach()
        {
            if (_active == null) return;
            _active.OnTranscriptionComplete -= HandleComplete;
            _active.OnTranscriptionFailed -= HandleFailed;
            _active = null;
        }
    }
}
