using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace DumyatRuntime
{
    /// <summary>
    /// Manages the pool of <see cref="ITTSModel"/> providers and owns the (provider-agnostic)
    /// playback. Uses ACTIVE + AUTO-FALLBACK: it asks available models to synthesise in descending
    /// priority order and plays the first clip that succeeds; if a model fails it transparently
    /// tries the next, and only reports <c>onError</c> when the whole pool is exhausted.
    ///
    /// Playback is non-blocking: <see cref="PlaySpeech"/> returns immediately; the subtitle/state
    /// update is the caller's responsibility and must NOT wait on <c>onComplete</c>.
    /// </summary>
    public class TTSManager : AIModelManager<TTSManager, ITTSModel>, ITTSService
    {
        [Tooltip("AudioSource used when a caller passes none (optional).")]
        [SerializeField] private AudioSource _fallbackAudioSource;

        [Tooltip("Mixer group every spoken line is routed through — the Dialouge group of MainMixer. " +
                 "Applied to any AudioSource that arrives here WITHOUT a group of its own, so voice " +
                 "always obeys the Dialogue volume in Settings.")]
        [SerializeField] private AudioMixerGroup _voiceMixerGroup;

        /// <summary>
        /// Name of the model that will speak the next line — the highest-priority AVAILABLE model
        /// in the pool — or null when none is available. Used e.g. by the negotiation LLM prompt
        /// to tell the language model exactly which TTS engine will read its output aloud.
        /// </summary>
        public string ActiveModelName
        {
            get
            {
                foreach (ITTSModel model in AvailableModels())
                    return model.ModelName;
                return null;
            }
        }

        /// <summary>
        /// Synthesises <paramref name="text"/> with <paramref name="voiceId"/> and plays it through
        /// <paramref name="targetSource"/> (or the fallback source). <paramref name="onComplete"/>
        /// fires when playback finishes; <paramref name="onError"/> fires only if EVERY model fails.
        /// </summary>
        public void PlaySpeech(string voiceId, string text, AudioSource targetSource,
            Action onComplete = null, Action onError = null, TtsVoiceSettings? voiceSettings = null)
        {
            AudioSource source = targetSource != null ? targetSource : _fallbackAudioSource;
            if (source == null)
            {
                Debug.LogWarning("TTSManager: no AudioSource (caller and fallback both null).");
                onError?.Invoke();
                return;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning("TTSManager: empty text; nothing to speak.");
                onError?.Invoke();
                return;
            }

            var models = new List<ITTSModel>(AvailableModels());
            if (models.Count == 0)
            {
                Debug.LogWarning("TTSManager: no TTS model available.");
                onError?.Invoke();
                return;
            }

            // Every spoken line from this manager funnels through here — the merchant's own source,
            // the negotiation fallback and this manager's fallback all arrive at this point.
            VoiceMixerRouting.EnsureRouted(source, _voiceMixerGroup, this);

            SynthesizeWithFallback(models, 0, voiceId, text, source, onComplete, onError, voiceSettings);
        }

        private void SynthesizeWithFallback(List<ITTSModel> models, int index, string voiceId, string text,
            AudioSource source, Action onComplete, Action onError, TtsVoiceSettings? voiceSettings)
        {
            if (index >= models.Count)
            {
                Debug.LogWarning("TTSManager: all TTS models failed.");
                onError?.Invoke();
                return;
            }

            ITTSModel model = models[index];
            model.Synthesize(text, voiceId, voiceSettings,
                onSuccess: clip =>
                {
                    if (clip == null || source == null)
                    {
                        // Treat as this model failing; try the next.
                        SynthesizeWithFallback(models, index + 1, voiceId, text, source, onComplete, onError, voiceSettings);
                        return;
                    }
                    source.clip = clip;
                    source.Play();
                    StartCoroutine(WaitForPlaybackEnd(source, clip, onComplete));
                },
                onError: msg =>
                {
                    Debug.LogWarning($"TTSManager: model '{model.ModelName}' failed ({msg}); trying next.");
                    SynthesizeWithFallback(models, index + 1, voiceId, text, source, onComplete, onError, voiceSettings);
                });
        }

        private IEnumerator WaitForPlaybackEnd(AudioSource source, AudioClip clip, Action onComplete)
        {
            yield return null; // let Play() register this frame
            while (source != null && source.isPlaying && source.clip == clip)
                yield return null;
            onComplete?.Invoke();
        }
    }
}
