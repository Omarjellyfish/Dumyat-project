using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Listens to narrative events and routes audio to the correct NPC
    /// or falls back to 2D global playback via <see cref="ISoundService"/>.
    /// Tracks the active speaker so interruptions cleanly stop the current voice.
    /// Plain C# service constructed by Reflex, same pattern as <see cref="NarrativeService"/>.
    /// </summary>
    public sealed class DialoguePresentationManager
    {
        private const float DEFAULT_VOICE_VOLUME = 1f;

        private readonly IEventService _eventService;
        private readonly INpcRegistryService _npcRegistry;
        private readonly ISoundService _soundService;
        private readonly INarrativeService _narrativeService;

        /// <summary>The actor currently playing a voice line, if any.</summary>
        private INpcActor _activeSpeakerActor;
        private CancellationTokenSource _advanceCts;

        public DialoguePresentationManager(IEventService eventService, INpcRegistryService npcRegistry, ISoundService soundService, INarrativeService narrativeService)
        {
            _eventService = eventService;
            _npcRegistry = npcRegistry;
            _soundService = soundService;
            _narrativeService = narrativeService;

            _eventService.Subscribe<DialogueLineStartedEvent>(OnLineStarted);
            _eventService.Subscribe<DialogueInterruptedEvent>(OnInterrupted);
            _eventService.Subscribe<DialogueEndedEvent>(OnEnded);
        }

        private void OnLineStarted(DialogueLineStartedEvent evt)
        {
            Debug.Log($"[DialoguePresentationManager] OnLineStarted received for CharacterId: '{evt.CharacterId}'");

            if (evt.VoiceClip == null)
            {
                Debug.LogWarning("[DialoguePresentationManager] VoiceClip is null. Aborting playback.");
                return;
            }

            // Stop whoever was speaking before (in case lines overlap)
            StopActiveSpeaker();

            var actor = _npcRegistry.GetActor(evt.CharacterId);
            if (actor != null)
            {
                Debug.Log($"[DialoguePresentationManager] Found actor for {evt.CharacterId}. Playing 3D voice line.");
                // 3D spatial audio on the NPC's own AudioSource (lipsync-ready)
                actor.PlayVoiceLine(evt.VoiceClip, DEFAULT_VOICE_VOLUME);
                _activeSpeakerActor = actor;
            }
            else
            {
                Debug.Log($"[DialoguePresentationManager] Actor for {evt.CharacterId} NOT found in registry. Falling back to 2D audio.");
                // 2D fallback — narrator, radio, off-screen character
                _soundService.PlayVoiceLine(evt.VoiceClip, null, DEFAULT_VOICE_VOLUME);
                _activeSpeakerActor = null;
            }

            // Auto-advance logic
            _advanceCts?.Cancel();
            _advanceCts = new CancellationTokenSource();
            _ = AutoAdvanceAsync(evt.Duration, _advanceCts.Token);
        }

        private async Task AutoAdvanceAsync(float duration, CancellationToken token)
        {
            try
            {
                // Wait for the duration of the line (plus a tiny buffer)
                await Task.Delay(TimeSpan.FromSeconds(duration), token);

                if (!token.IsCancellationRequested && _narrativeService.IsPlaying)
                {
                    Debug.Log("[DialoguePresentationManager] Auto-advancing to next line.");
                    _narrativeService.AdvanceLine();
                }
            }
            catch (TaskCanceledException)
            {
                // Ignored — expected when interrupted
            }
        }

        private void OnInterrupted(DialogueInterruptedEvent evt)
        {
            _advanceCts?.Cancel();
            // Higher-priority dialogue is about to start — kill current voice immediately
            StopActiveSpeaker();
        }

        private void OnEnded(DialogueEndedEvent evt)
        {
            _advanceCts?.Cancel();
            // All dialogue finished — clean up
            StopActiveSpeaker();
        }

        private void StopActiveSpeaker()
        {
            if (_activeSpeakerActor != null)
            {
                _activeSpeakerActor.StopVoiceLine();
                _activeSpeakerActor = null;
            }
        }
    }
}
