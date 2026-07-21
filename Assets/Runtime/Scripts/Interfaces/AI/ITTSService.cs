using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the pooled text-to-speech service. Injected in place of the former
    /// <c>TTSManager.Singleton</c>. Backed by <see cref="TTSManager"/>, which owns the
    /// <see cref="ITTSModel"/> pool, playback and cross-provider fallback.
    /// </summary>
    public interface ITTSService
    {
        /// <summary>
        /// Name of the model that will speak the next line (highest-priority available model), or
        /// null when none is available. Used e.g. by the negotiation LLM prompt to name the exact
        /// TTS engine that will read its output aloud.
        /// </summary>
        string ActiveModelName { get; }

        /// <summary>
        /// Synthesises <paramref name="text"/> with <paramref name="voiceId"/> and plays it through
        /// <paramref name="targetSource"/> (or the manager's fallback source). <paramref name="onComplete"/>
        /// fires when playback finishes; <paramref name="onError"/> fires only if EVERY model fails.
        /// Pass <paramref name="voiceSettings"/> to override the provider's global voice parameters for
        /// this line only (e.g. a per-merchant <see cref="TtsVoiceSettings"/>); leave it null to use the
        /// provider's own defaults.
        /// </summary>
        void PlaySpeech(string voiceId, string text, AudioSource targetSource,
            Action onComplete = null, Action onError = null, TtsVoiceSettings? voiceSettings = null);
    }
}
