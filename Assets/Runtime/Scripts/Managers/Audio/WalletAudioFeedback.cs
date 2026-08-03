using UnityEngine;
using UnityEngine.Audio;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Plays a designer-assigned sound whenever the player's EGP balance moves: one clip for money
    /// coming IN (sale payout, quest reward), a different clip for money going OUT, and an optional
    /// sting for a purchase refused through lack of funds.
    ///
    /// Purely reactive — it owns no money logic, it only listens to <see cref="MoneyChangedEvent"/>.
    /// Drop it on any always-active GameObject in a scene that has a ContainerScope; it ships on the
    /// XR Origin rig so every scene using the rig gets it with no per-scene wiring.
    /// </summary>
    public class WalletAudioFeedback : MonoBehaviour
    {
        [Header("Money Topped Up")]
        [Tooltip("Played when the balance goes UP — a sale payout or a quest reward. Leave empty for silence.")]
        [SerializeField] private AudioClip _moneyAddedClip;

        [Header("Money Spent")]
        [Tooltip("Played when the balance goes DOWN — a successful purchase. Leave empty for silence.")]
        [SerializeField] private AudioClip _moneySpentClip;

        [Header("Purchase Refused (optional)")]
        [Tooltip("Played when a spend is refused because the player cannot afford it. " +
                 "Nothing is debited in that case, so the 'money spent' clip deliberately does NOT play.")]
        [SerializeField] private AudioClip _insufficientFundsClip;

        [Header("Mix")]
        [Range(0f, 1f)]
        [SerializeField] private float _volume = 0.8f;

        [Tooltip("Optional mixer group override. Leave empty to use the SoundManager's default SFX bus.")]
        [SerializeField] private AudioMixerGroup _mixerGroup;

        private ISoundService _sound;
        private IEventService _events;

        [Inject]
        private void Inject(ISoundService sound, IEventService events)
        {
            _sound = sound;
            _events = events;
        }

        private void OnEnable()
        {
            if (_events == null)
            {
                Debug.LogWarning($"{nameof(WalletAudioFeedback)} on '{name}': no IEventService injected, " +
                                 "money sounds will not play. Does this scene have a ContainerScope?", this);
                return;
            }

            _events.Subscribe<MoneyChangedEvent>(HandleMoneyChanged);
            _events.Subscribe<MoneySpendFailedEvent>(HandleSpendFailed);
        }

        private void OnDisable()
        {
            if (_events == null) return;

            _events.Unsubscribe<MoneyChangedEvent>(HandleMoneyChanged);
            _events.Unsubscribe<MoneySpendFailedEvent>(HandleSpendFailed);
        }

        private void HandleMoneyChanged(MoneyChangedEvent evt)
        {
            Play(evt.IsIncome ? _moneyAddedClip : _moneySpentClip);
        }

        private void HandleSpendFailed(MoneySpendFailedEvent evt)
        {
            Play(_insufficientFundsClip);
        }

        // The wallet has no meaningful world position — it is the player's own money — and the pooled
        // SFX source is 2D anyway. We still pass `transform` rather than null because
        // SoundManager.PlaySoundFxAtPosition dereferences the transform with no null guard.
        private void Play(AudioClip clip)
        {
            if (_sound == null || clip == null) return;
            _sound.PlaySoundFxAtPosition(clip, transform, _volume, _mixerGroup);
        }
    }
}
