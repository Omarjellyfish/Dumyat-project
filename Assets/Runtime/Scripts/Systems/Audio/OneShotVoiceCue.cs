using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    /// <summary>How long a cue stays "already played".</summary>
    public enum VoiceCueScope
    {
        /// <summary>Once per save file. Survives quitting the game. The default for guidance lines.</summary>
        SaveFile,

        /// <summary>Once per app run. Replays for a player who restarts, useful while play-testing.</summary>
        Session,

        /// <summary>No suppression — plays on every trigger.</summary>
        EveryTime
    }

    /// <summary>
    /// Plays one voice line at most once, optionally only in named scenes.
    ///
    /// Guidance lines ("that's a respectable fish", "the fish bit") are welcome the first time and
    /// grating on the twentieth, and several of them sit on objects the player handles repeatedly or
    /// that exist in more than one scene. This component owns that whole decision — the once-ness,
    /// the scene gating, and the null-safety when a runtime-spawned prefab never got injected — so
    /// the components that detect the gameplay MOMENT (a grab, a hook) only have to say
    /// <see cref="TryPlay"/> and stay about the gameplay.
    ///
    /// IMPORTANT — the "played" flag is keyed on <see cref="CueId"/>, not on this instance. Every fish
    /// in the water carries its own copy of this component, so an instance-level flag would only stop
    /// the SAME fish repeating and the line would still fire once per fish. Sharing the key is what
    /// makes "only once" mean once for the player.
    /// </summary>
    [AddComponentMenu("Dumyat/Audio/One Shot Voice Cue")]
    public class OneShotVoiceCue : MonoBehaviour
    {
        [Header("Line")]
        [Tooltip("The voice line to play. Leave empty to disable this cue.")]
        [SerializeField] private AudioClip _clip;

        [Tooltip("Stable key identifying this line. SHARED by every copy of this cue — all the fish " +
                 "in a scene use the same id so the line plays once for the player, not once per fish. " +
                 "Also the save key when Scope is SaveFile, so it must never change once shipped.")]
        [SerializeField] private string _cueId;

        [Tooltip("Playback volume, 0-1.")]
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;

        [Tooltip("Where the line plays from. Defaults to this GameObject — set it to a nearby " +
                 "character or prop when the voice should not appear to come from the object itself.")]
        [SerializeField] private Transform _playAt;

        [Header("Repeat")]
        [Tooltip("How long the line stays suppressed after it has played once.")]
        [SerializeField] private VoiceCueScope _scope = VoiceCueScope.SaveFile;

        [Header("Scenes")]
        [Tooltip("Scene names this line is allowed to play in (exact Build Settings names). " +
                 "LEAVE EMPTY to allow every scene. Use it to keep a line tied to the scene whose " +
                 "character is speaking it — a fishing remark from the father should not follow a " +
                 "fish that turns up at the market.")]
        [SerializeField] private List<string> _allowedScenes = new List<string>();

        /// <summary>
        /// Cue ids already played this run. Static because the suppression is per-LINE, not
        /// per-object — see the class remarks. Cleared only by the app restarting, which is exactly
        /// what <see cref="VoiceCueScope.Session"/> means.
        /// </summary>
        private static readonly HashSet<string> SessionPlayed = new HashSet<string>();

        private ISoundService _soundService;
        private IPlayerPersistenceService _persistence;

        /// <summary>The key this cue is remembered under. Falls back to the object name if unset.</summary>
        public string CueId => string.IsNullOrWhiteSpace(_cueId) ? name : _cueId;

        [Inject]
        private void Inject(ISoundService soundService, IPlayerPersistenceService persistence)
        {
            _soundService = soundService;
            _persistence = persistence;
        }

        /// <summary>
        /// Plays the line if it has not been heard yet and this scene allows it.
        /// Safe to call every trigger; returns whether anything was actually played.
        /// </summary>
        public bool TryPlay()
        {
            if (_clip == null) return false;
            if (!IsAllowedInCurrentScene()) return false;
            if (HasAlreadyPlayed()) return false;

            if (_soundService == null)
            {
                // Injection is absent when a prefab was spawned at runtime without being injected, or
                // when the scene has no ContainerScope. Warn rather than throw: a missing voice line
                // must not take the grab that triggered it down with it.
                Debug.LogWarning($"{nameof(OneShotVoiceCue)} on '{name}' has no {nameof(ISoundService)} — " +
                                 $"the line '{CueId}' cannot play. Does this scene have a ContainerScope, " +
                                 "and was this prefab injected if it was spawned at runtime?", this);
                return false;
            }

            _soundService.PlaySoundFxAtPosition(_clip, _playAt != null ? _playAt : transform, _volume);
            MarkPlayed();
            return true;
        }

        /// <summary>Forgets that this line played, so it can be heard again. For dev tools and testing.</summary>
        public void ResetCue() => SessionPlayed.Remove(CueId);

        private bool IsAllowedInCurrentScene()
        {
            // An empty list means "anywhere" — the common case, and the one that must not cost the
            // designer a per-scene list on every cue in the game.
            if (_allowedScenes == null || _allowedScenes.Count == 0) return true;

            string current = SceneManager.GetActiveScene().name;
            for (int i = 0; i < _allowedScenes.Count; i++)
            {
                if (string.Equals(_allowedScenes[i], current, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private bool HasAlreadyPlayed()
        {
            switch (_scope)
            {
                case VoiceCueScope.EveryTime:
                    return false;

                case VoiceCueScope.Session:
                    return SessionPlayed.Contains(CueId);

                default:
                    // Session is checked as well as the save key so a line cannot repeat in the window
                    // between playing and the async save landing — two fish grabbed in quick succession
                    // would otherwise both see an un-written key.
                    if (SessionPlayed.Contains(CueId)) return true;
                    return _persistence != null && _persistence.HasKey(CueId);
            }
        }

        private void MarkPlayed()
        {
            if (_scope == VoiceCueScope.EveryTime) return;

            SessionPlayed.Add(CueId);

            if (_scope != VoiceCueScope.SaveFile) return;

            if (_persistence == null)
            {
                Debug.LogWarning($"{nameof(OneShotVoiceCue)} on '{name}': no " +
                                 $"{nameof(IPlayerPersistenceService)}, so '{CueId}' will play again " +
                                 "next session despite being set to save-file scope.", this);
                return;
            }

            _persistence.AddKey(CueId);
        }
    }
}
