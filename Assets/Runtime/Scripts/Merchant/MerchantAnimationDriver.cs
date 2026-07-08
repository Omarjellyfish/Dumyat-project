using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Purely cosmetic Animator driver for a merchant NPC. The AiNPC controller exposes plain
    /// states (Idle / Wave / Walk) with no parameters or transitions, so this cross-fades
    /// directly by state name: a greeting Wave when the merchant starts an approach
    /// (<see cref="MerchantNPC.OnApproachStarted"/>), then back to Idle after the wave and
    /// whenever the negotiation ends. No game logic lives here.
    /// </summary>
    [RequireComponent(typeof(MerchantNPC))]
    public class MerchantAnimationDriver : MonoBehaviour
    {
        [Tooltip("Animator to drive. Auto-resolved from this GameObject or its children when empty.")]
        [SerializeField] private Animator _animator;

        [Tooltip("Name of the looping idle state in the controller.")]
        [SerializeField] private string _idleState = "Idle";

        [Tooltip("Name of the greeting wave state in the controller.")]
        [SerializeField] private string _waveState = "Wave";

        [Tooltip("Seconds the wave plays before returning to idle (the controller has no exit " +
                 "transitions, so the return is timed here).")]
        [SerializeField, Min(0f)] private float _waveSeconds = 2.5f;

        [Tooltip("Cross-fade duration between states, in seconds.")]
        [SerializeField, Min(0f)] private float _crossFadeSeconds = 0.15f;

        private MerchantNPC _merchant;

        private void Awake()
        {
            _merchant = GetComponent<MerchantNPC>();

            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
                Debug.LogWarning($"MerchantAnimationDriver: no Animator found under '{name}'; " +
                                 "merchant will not animate.");
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

        private void HandleApproachStarted(MerchantNPC merchant)
        {
            if (!CrossFadeSafe(_waveState)) return;

            CancelInvoke(nameof(ReturnToIdle));
            Invoke(nameof(ReturnToIdle), _waveSeconds);
        }

        private void HandleNegotiationEnded(MerchantNPC merchant, NegotiationPhase outcome)
        {
            CancelInvoke(nameof(ReturnToIdle));
            ReturnToIdle();
        }

        private void ReturnToIdle() => CrossFadeSafe(_idleState);

        // Cross-fades to the named state if the controller actually has it; a missing state is a
        // content mistake, so warn instead of throwing Animator errors every call.
        private bool CrossFadeSafe(string stateName)
        {
            if (_animator == null || string.IsNullOrEmpty(stateName)) return false;

            int hash = Animator.StringToHash(stateName);
            if (!_animator.HasState(0, hash))
            {
                Debug.LogWarning($"MerchantAnimationDriver: state '{stateName}' not found in " +
                                 $"controller '{_animator.runtimeAnimatorController?.name}'.");
                return false;
            }

            _animator.CrossFadeInFixedTime(hash, _crossFadeSeconds);
            return true;
        }
    }
}
