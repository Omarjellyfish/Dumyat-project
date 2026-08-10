using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    /// <summary>
    /// How long a cue stays "already played".
    ///
    /// Ordinals are pinned: they serialise as ints on authored components, so reordering would silently
    /// re-point every one of them.
    /// </summary>
    public enum CueScope
    {
        /// <summary>
        /// Once per save file. Survives quitting. Use it only for a line that must never be heard twice
        /// in a playthrough — it writes a permanent key, and there is no way back without clearing the
        /// save.
        /// </summary>
        SaveFile = 0,

        /// <summary>Once per app run. Survives changing scenes; resets when the game is relaunched.</summary>
        Session = 1,

        /// <summary>No suppression — plays on every trigger.</summary>
        EveryTime = 2,

        /// <summary>
        /// Once per visit to the scene. Forgotten as soon as the scene is reloaded or re-entered.
        ///
        /// The default, and the right answer for almost every line: it stops a remark repeating while
        /// the player is standing there, without spending the line forever. A save-file scope on a line
        /// that turns out to be mistimed is unrecoverable in a shipped build and merely infuriating in
        /// the editor, where every test run burns one more.
        /// </summary>
        PerScene = 3
    }

    /// <summary>
    /// The "say this once" decision, on its own: has this line been heard yet, and is this a scene it
    /// is allowed to speak in.
    ///
    /// It lives apart from the things that play lines because the same rule is needed by more than one
    /// of them — <see cref="OneShotVoiceCue"/> plays a raw <see cref="AudioClip"/>,
    /// <see cref="OneShotDialogueCue"/> plays a whole <see cref="DialogueSequenceSO"/> — and this
    /// project has been bitten badly by that rule existing in several copies. Before this class there
    /// were three implementations of "play the fish line once" sharing one save key: a per-instance
    /// bool on <c>Fish</c> that reset on every scene load and counted each of the ten fish separately,
    /// a hand-rolled key check in <c>DehookingFish</c> that played a null clip and burned the key
    /// anyway, and a correct one nobody called. Only one of them is left, and it is this one.
    ///
    /// IMPORTANT — the "played" flag is keyed on <see cref="CueId"/>, not on the object holding the
    /// gate. Every fish in the water carries its own copy, so an instance-level flag would only stop
    /// the SAME fish repeating and the line would still fire once per fish. Sharing the key is what
    /// makes "only once" mean once for the player.
    ///
    /// Serializable so it can sit on a component as a single inspector block rather than three loose
    /// fields that have to be re-declared, re-tooltipped and re-explained on every cue component.
    /// </summary>
    [Serializable]
    public class OnceOnlyCueGate
    {
        [Tooltip("Stable key identifying this line. SHARED by every copy of this cue — all the fish " +
                 "in a scene use the same id so the line plays once for the player, not once per fish. " +
                 "Also the save key when Scope is SaveFile, so it must never change once shipped.")]
        [SerializeField] private string _cueId;

        [Tooltip("How long the line stays suppressed after it has played once.\n\n" +
                 "Per Scene — forgotten when the scene reloads. The default; use it unless you have a " +
                 "reason not to.\n" +
                 "Session — survives scene changes, resets when the game restarts.\n" +
                 "Save File — permanent. Writes a key to the save; there is no way back without " +
                 "clearing it.\n" +
                 "Every Time — no suppression.")]
        [SerializeField] private CueScope _scope = CueScope.PerScene;

        /// <summary>
        /// Per-scene suppression. Deliberately NOT serialized and NOT static: it belongs to this
        /// component instance, and a scene reload builds new instances, which is exactly what "forgotten
        /// when the scene reloads" has to mean.
        /// </summary>
        [System.NonSerialized] private bool _playedThisScene;

        [Tooltip("Scene names this line is allowed to play in (exact Build Settings names). " +
                 "LEAVE EMPTY to allow every scene. Use it to keep a line tied to the scene whose " +
                 "character is speaking it — a fishing remark from the father should not follow a " +
                 "fish that turns up at the market.")]
        [SerializeField] private List<string> _allowedScenes = new List<string>();

        /// <summary>
        /// Cue ids already played this run. Static because the suppression is per-LINE, not
        /// per-object — see the class remarks. Cleared only by the app restarting, which is exactly
        /// what <see cref="CueScope.Session"/> means, or by <see cref="ClearSession"/>.
        /// </summary>
        private static readonly HashSet<string> SessionPlayed = new HashSet<string>();

        /// <summary>The configured id, or <paramref name="fallbackId"/> (normally the owning object's
        /// name) when none was authored.</summary>
        public string CueId(string fallbackId) =>
            string.IsNullOrWhiteSpace(_cueId) ? fallbackId : _cueId;

        public CueScope Scope => _scope;

        /// <summary>
        /// Whether this line may be spoken right now: the scene allows it and it has not been heard.
        /// </summary>
        public bool CanPlay(IPlayerPersistenceService persistence, string fallbackId)
        {
            if (!IsAllowedInCurrentScene()) return false;
            return !HasAlreadyPlayed(persistence, fallbackId);
        }

        /// <summary>Records that the line was just spoken. Call only after it actually played.</summary>
        public void MarkPlayed(IPlayerPersistenceService persistence, string fallbackId, UnityEngine.Object context)
        {
            if (_scope == CueScope.EveryTime) return;

            _playedThisScene = true;
            if (_scope == CueScope.PerScene) return;

            string id = CueId(fallbackId);
            SessionPlayed.Add(id);

            if (_scope != CueScope.SaveFile) return;

            if (persistence == null)
            {
                Debug.LogWarning($"{nameof(OnceOnlyCueGate)} on '{fallbackId}': no " +
                                 $"{nameof(IPlayerPersistenceService)}, so '{id}' will play again " +
                                 "next session despite being set to save-file scope.", context);
                return;
            }

            persistence.AddKey(id);
        }

        /// <summary>
        /// Forgets every line played this run, so they can all be heard again. For New Game and for
        /// dev tools: the save key goes with the save file, but this set does not, so without it a
        /// player who resets mid-session gets a silent replay of the whole game.
        /// </summary>
        public static void ClearSession() => SessionPlayed.Clear();

        /// <summary>
        /// Whether <paramref name="id"/> has already been spoken during this run of the game.
        ///
        /// Exposed so anything that needs "once per game, across scenes" can share this one set rather
        /// than keeping its own — a second static set would mean two disagreeing answers to the same
        /// question. <see cref="NarrativeTriggerZone"/> uses it for its per-game option.
        /// </summary>
        public static bool PlayedThisSession(string id) =>
            !string.IsNullOrWhiteSpace(id) && SessionPlayed.Contains(id);

        /// <summary>Records <paramref name="id"/> as spoken for the rest of this run.</summary>
        public static void MarkPlayedThisSession(string id)
        {
            if (!string.IsNullOrWhiteSpace(id)) SessionPlayed.Add(id);
        }

        /// <summary>Forgets one line. For dev tools and testing.</summary>
        public void ResetCue(string fallbackId) => SessionPlayed.Remove(CueId(fallbackId));

        private bool IsAllowedInCurrentScene()
        {
            // An empty list means "anywhere" — the common case, and the one that must not cost the
            // designer a per-scene list on every cue in the game.
            if (_allowedScenes == null || _allowedScenes.Count == 0) return true;

            string current = SceneManager.GetActiveScene().name;
            for (int i = 0; i < _allowedScenes.Count; i++)
            {
                if (string.Equals(_allowedScenes[i], current, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private bool HasAlreadyPlayed(IPlayerPersistenceService persistence, string fallbackId)
        {
            switch (_scope)
            {
                case CueScope.EveryTime:
                    return false;

                case CueScope.PerScene:
                    return _playedThisScene;

                case CueScope.Session:
                    return SessionPlayed.Contains(CueId(fallbackId));

                default:
                    // Session is checked as well as the save key so a line cannot repeat in the window
                    // between playing and the async save landing — two fish grabbed in quick succession
                    // would otherwise both see an un-written key.
                    string id = CueId(fallbackId);
                    if (SessionPlayed.Contains(id)) return true;
                    return persistence != null && persistence.HasKey(id);
            }
        }
    }
}
