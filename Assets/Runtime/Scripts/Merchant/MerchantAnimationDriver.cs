using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Purely cosmetic Animator driver for a merchant NPC. The merchant controllers are bare state
    /// machines with no parameters and no transitions (see BaseCharacterController: Idle / Talk), so
    /// states are cross-faded directly by name through the shared <see cref="AnimatorStateSwitcher"/>:
    ///
    ///  * a greeting Wave when the merchant starts an approach (<see cref="MerchantNPC.OnApproachStarted"/>),
    ///  * Talk for as long as he is actually speaking,
    ///  * Idle the rest of the time.
    ///
    /// The Animator is taken from the merchant's character body, so replacing a character model brings
    /// its own Animator with it and needs no rewiring.
    ///
    /// EVERY state is optional. A model that ships without a wave — or without a controller at all —
    /// skips that beat and keeps working; trading is driven by <see cref="MerchantNPC"/>, never by
    /// animation. No game logic lives here.
    /// </summary>
    [RequireComponent(typeof(MerchantNPC))]
    public class MerchantAnimationDriver : MonoBehaviour
    {
        [Tooltip("Animator to drive. Leave empty to take it from the merchant's character body (the " +
                 "'Character' child), which is what you want — a swapped-in model is then picked up " +
                 "automatically.")]
        [SerializeField] private Animator _animator;

        [Tooltip("Name of the looping idle state. Optional — name a state the controller does not have " +
                 "and the merchant is simply left as he is.")]
        [SerializeField] private string _idleState = "Idle";

        [Header("Speaking")]
        [Tooltip("Play the talk animation while the merchant is speaking, alongside the lip sync.")]
        [SerializeField] private bool _playTalkWhileSpeaking = true;

        [Tooltip("Name of the talking state in the controller.")]
        [SerializeField] private string _talkState = "Talk";

        [Tooltip("Voice source watched to tell whether he is speaking. Auto-resolved to this " +
                 "GameObject's AudioSource — the same one the TTS plays through and SimpleLipSync " +
                 "reads, so the gestures and the mouth can never disagree.")]
        [SerializeField] private AudioSource _voiceSource;

        [Tooltip("Seconds of silence tolerated before dropping back to idle. A line is several clips " +
                 "with small gaps between them; without this hold he snaps to idle between sentences.")]
        [SerializeField, Min(0f)] private float _talkHoldSeconds = 0.35f;

        [Header("Greeting")]
        [Tooltip("Play a greeting wave when the merchant starts an approach. Turn OFF for characters " +
                 "that should not salute. A controller with no wave state skips it anyway, so this is " +
                 "only needed to suppress a wave the controller DOES have.")]
        [SerializeField] private bool _playGreetingWave = true;

        [Tooltip("Name of the greeting wave state in the controller. Optional — see above.")]
        [SerializeField] private string _waveState = "Wave";

        [Tooltip("Seconds the wave plays before returning to idle (these controllers have no exit " +
                 "transitions, so the return is timed here).")]
        [SerializeField, Min(0f)] private float _waveSeconds = 2.5f;

        [Tooltip("Cross-fade duration between states, in seconds.")]
        [SerializeField, Min(0f)] private float _crossFadeSeconds = 0.15f;

        private readonly AnimatorStateSwitcher _states = new AnimatorStateSwitcher();
        private MerchantNPC _merchant;

        private bool _speaking;
        private float _silenceTimer;

        private void Awake()
        {
            _merchant = GetComponent<MerchantNPC>();
            if (_voiceSource == null) _voiceSource = GetComponent<AudioSource>();

            // CharacterRoot resolves on access, so this is safe regardless of Awake order between the
            // components on this GameObject.
            _states.Resolve(this, _animator, _merchant != null ? _merchant.CharacterRoot : null);
        }

        private void OnEnable()
        {
            if (_merchant == null) return;
            _merchant.OnApproachStarted += HandleApproachStarted;
            _merchant.OnNegotiationEnded += HandleNegotiationEnded;
        }

        private void OnDisable()
        {
            if (_merchant == null) return;
            _merchant.OnApproachStarted -= HandleApproachStarted;
            _merchant.OnNegotiationEnded -= HandleNegotiationEnded;
        }

        /// <summary>
        /// Drives Talk/Idle from whether the voice is actually sounding.
        ///
        /// Deliberately EDGE-triggered — a state is only issued when the speaking flag CHANGES, never
        /// every frame. That is what lets this coexist with <see cref="MerchantApproachMover"/>, which
        /// drives Walk/Idle on the same Animator: the two never write in the same frame, so neither can
        /// stamp on the other's state.
        ///
        /// The signal is the AudioSource rather than <see cref="MerchantSpokeEvent"/> because that event
        /// pairs with MerchantStoppedSpeakingEvent, which fires once when the whole negotiation tears
        /// down — not at the end of each line. Driving from it would leave the merchant gesticulating
        /// through the player's replies.
        /// </summary>
        private void Update()
        {
            if (!_playTalkWhileSpeaking || _voiceSource == null) return;

            bool sounding = _voiceSource.isPlaying;
            _silenceTimer = sounding ? 0f : _silenceTimer + Time.deltaTime;

            bool speaking = sounding || (_speaking && _silenceTimer < _talkHoldSeconds);
            if (speaking == _speaking) return;

            _speaking = speaking;
            if (_speaking)
            {
                // Talking supersedes a greeting wave still counting down.
                CancelInvoke(nameof(ReturnToIdle));
                _states.Play(_talkState, _crossFadeSeconds);
            }
            else
            {
                ReturnToIdle();
            }
        }

        private void HandleApproachStarted(MerchantNPC merchant)
        {
            if (!_playGreetingWave) return;
            if (!_states.Play(_waveState, _crossFadeSeconds, restartIfActive: true)) return;

            CancelInvoke(nameof(ReturnToIdle));
            Invoke(nameof(ReturnToIdle), _waveSeconds);
        }

        private void HandleNegotiationEnded(MerchantNPC merchant, NegotiationPhase outcome)
        {
            CancelInvoke(nameof(ReturnToIdle));
            _speaking = false;
            ReturnToIdle();
        }

        private void ReturnToIdle() => _states.Play(_idleState, _crossFadeSeconds);
    }
}
