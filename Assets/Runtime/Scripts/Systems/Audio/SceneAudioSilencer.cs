namespace DumyatRuntime
{
    /// <summary>
    /// Makes leaving a scene silence everything that scene was saying.
    ///
    /// The audio systems outlive the scenes they play for. <see cref="SoundManager"/>'s pool and
    /// <see cref="NarrativeService"/> are both root singletons on DontDestroyOnLoad objects, so a voice
    /// line started on the jetty carries straight through the load and finishes over the market, and the
    /// narrative engine keeps its queue and goes on feeding the next town's speakers lines belonging to
    /// the one the player just left.
    ///
    /// This is a policy, not a mechanism: "nothing from the old scene is still audible in the new one."
    /// It lives in one place rather than as a stop() call scattered through every caller, because the
    /// rule has to hold for lines nobody remembered to write cleanup for — which is all of them.
    ///
    /// Keyed on <c>SceneLoadBeginEvent</c>, which <see cref="SceneLoadService"/> publishes BEFORE it
    /// starts the transition sting. The sting is therefore untouched, and is meant to play across the
    /// load: it is part of the transition rather than a leftover from the scene being left.
    /// </summary>
    public sealed class SceneAudioSilencer
    {
        private readonly IEventService _eventService;
        private readonly ISoundService _soundService;
        private readonly INarrativeService _narrativeService;

        public SceneAudioSilencer(IEventService eventService, ISoundService soundService,
                                  INarrativeService narrativeService)
        {
            _eventService = eventService;
            _soundService = soundService;
            _narrativeService = narrativeService;

            _eventService.Subscribe<SceneLoadBeginEvent>(OnSceneLoadBegin);
        }

        private void OnSceneLoadBegin(SceneLoadBeginEvent evt)
        {
            // Narrative first. It publishes DialogueEndedEvent, which is what makes
            // DialoguePresentationManager cancel its auto-advance timer and stop the speaking NPC —
            // done after the pool was silenced, that timer could start one more line into the silence.
            _narrativeService?.StopAll();
            _soundService?.StopAll();

            VerboseLog.Trace($"[SceneAudioSilencer] Silenced everything on the way to '{evt.TargetSceneName}'.");
        }
    }
}
