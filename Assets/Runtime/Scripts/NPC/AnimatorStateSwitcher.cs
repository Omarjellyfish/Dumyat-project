using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Cross-fades an <see cref="Animator"/> between plain, parameterless states BY NAME, tolerating a
    /// character that does not have the state — or has no controller at all.
    ///
    /// A plain solver, not a component: a MonoBehaviour holds one as a field, the same shape as
    /// <see cref="LazyFollowYaw"/> and <see cref="CameraBillboard"/>. <see cref="MerchantAnimationDriver"/>
    /// and <see cref="MerchantApproachMover"/> both drive the same Animator and previously carried their
    /// own near-identical copy of this logic, which drifted: one warned on every call for a state the
    /// character simply doesn't have, the other silently skipped, and only one of them guarded against a
    /// null controller (which logs a Unity error on EVERY query, once per walking frame).
    ///
    /// Named states, not parameters, because the merchant controllers are authored as bare state
    /// machines with no parameters and no transitions — see BaseCharacterController (Idle / Talk).
    ///
    /// EVERY state is optional by design. Characters arrive from different tools with different clips;
    /// a rig without a wave should simply not wave, not spam the console. A missing state is reported
    /// once, as information rather than a warning.
    /// </summary>
    public sealed class AnimatorStateSwitcher
    {
        /// <summary>These controllers are single-layer; the base layer is the only one driven.</summary>
        private const int Layer = 0;

        private Animator _animator;
        private Object _context;

        // States already reported as absent, so a character without a wave says so once rather than on
        // every approach.
        private readonly HashSet<string> _reportedMissing = new HashSet<string>();

        /// <summary>The resolved Animator, or null when the character has none.</summary>
        public Animator Animator => _animator;

        /// <summary>True when there is an Animator with a controller to drive.</summary>
        public bool IsReady => _animator != null && _animator.runtimeAnimatorController != null;

        /// <summary>
        /// Finds the Animator to drive, preferring an explicitly assigned one, then the character body
        /// (<see cref="MerchantNPC.CharacterRoot"/> — the 'Character' child holding the rig and its
        /// controller), and only then falling back to a search of the whole GameObject.
        ///
        /// The character-body step is what makes swapping a character model work without rewiring:
        /// the new model brings its own Animator as a child, and this finds it there.
        /// </summary>
        public Animator Resolve(Component owner, Animator preferred, Transform characterRoot)
        {
            _context = owner;

            if (preferred != null) return _animator = preferred;

            if (characterRoot != null)
            {
                var onBody = characterRoot.GetComponentInChildren<Animator>(true);
                if (onBody != null) return _animator = onBody;
            }

            _animator = owner != null ? owner.GetComponentInChildren<Animator>(true) : null;
            if (_animator == null && owner != null)
                Debug.Log($"{owner.GetType().Name}: no Animator under '{owner.name}' — this character " +
                          "will not animate, which is harmless; gameplay does not depend on it.", owner);

            return _animator;
        }

        /// <summary>
        /// Cross-fades to <paramref name="stateName"/>. Returns false — changing nothing — when there is
        /// no controller or no such state. Already being in (or blending toward) the state is a no-op
        /// unless <paramref name="restartIfActive"/> is set, which matters because these components are
        /// polled: re-issuing a cross-fade every frame would freeze the animation on its first pose.
        /// </summary>
        public bool Play(string stateName, float crossFadeSeconds, bool restartIfActive = false)
        {
            if (string.IsNullOrEmpty(stateName) || _animator == null) return false;

            // Checked before HasState: querying an Animator with no controller logs a Unity error of its
            // own, and a character imported without one is the common case here.
            if (_animator.runtimeAnimatorController == null)
            {
                ReportMissingOnce(stateName, "no Animator controller is assigned");
                return false;
            }

            int hash = Animator.StringToHash(stateName);
            if (!_animator.HasState(Layer, hash))
            {
                ReportMissingOnce(stateName,
                    $"controller '{_animator.runtimeAnimatorController.name}' has no such state");
                return false;
            }

            if (!restartIfActive && IsInState(hash)) return true;

            _animator.CrossFadeInFixedTime(hash, crossFadeSeconds);
            return true;
        }

        /// <summary>True when the base layer is playing, or blending toward, the named state.</summary>
        public bool IsInState(string stateName)
        {
            if (!IsReady || string.IsNullOrEmpty(stateName)) return false;
            return IsInState(Animator.StringToHash(stateName));
        }

        private bool IsInState(int hash)
        {
            if (_animator.GetCurrentAnimatorStateInfo(Layer).shortNameHash == hash) return true;
            return _animator.IsInTransition(Layer) &&
                   _animator.GetNextAnimatorStateInfo(Layer).shortNameHash == hash;
        }

        private void ReportMissingOnce(string stateName, string reason)
        {
            if (!_reportedMissing.Add(stateName)) return;
            string who = _context != null ? _context.name : "character";
            Debug.Log($"AnimatorStateSwitcher: skipping '{stateName}' on '{who}' — {reason}. " +
                      "Harmless; the character just holds its current pose.", _context);
        }
    }
}
