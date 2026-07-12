using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// A text-to-speech model usable by <see cref="TTSManager"/>. The model's only job is
    /// provider-specific SYNTHESIS — turning text + a voice into an <see cref="AudioClip"/>.
    /// Playback (routing the clip to an AudioSource, waiting for it to finish) is provider-agnostic
    /// and owned by the manager, so it is deliberately NOT part of this contract.
    /// </summary>
    public interface ITTSModel : IAIModel
    {
        /// <summary>
        /// Synthesises <paramref name="text"/> with <paramref name="voiceId"/>. Delivers a ready
        /// <see cref="AudioClip"/> via <paramref name="onSuccess"/>, or a reason via
        /// <paramref name="onError"/> so the manager can try the next model.
        /// </summary>
        void Synthesize(string text, string voiceId, Action<AudioClip> onSuccess, Action<string> onError);
    }
}
