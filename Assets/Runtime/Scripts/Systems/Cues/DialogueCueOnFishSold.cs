using System.Collections;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    [AddComponentMenu("Dumyat/Cues/Dialogue Cue On Fish Sold")]
    public class DialogueCueOnFishSold : MonoBehaviour
    {
        [Tooltip("The line to speak. Auto-resolved from this GameObject if left empty.")]
        [SerializeField] private OneShotDialogueCue _cue;

        [Tooltip("Seconds to wait after the sale closes before the line plays.")]
        [SerializeField] private float _delaySeconds = 0f;

        private IEventService _eventService;
        private bool _subscribed;

        [Inject]
        private void Inject(IEventService eventService)
        {
            _eventService = eventService;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Awake()
        {
            if (_cue == null) _cue = GetComponent<OneShotDialogueCue>();

            if (_cue == null)
                Debug.LogWarning($"{nameof(DialogueCueOnFishSold)} on '{name}' has no " +
                                 $"{nameof(OneShotDialogueCue)} to speak.", this);
        }

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            if (!_subscribed) return;

            _eventService.Unsubscribe<SellScoreEvent>(OnSaleClosed);
            _subscribed = false;

            StopAllCoroutines();
        }

        private void Subscribe()
        {
            if (_subscribed || _eventService == null) return;

            _eventService.Subscribe<SellScoreEvent>(OnSaleClosed);
            _subscribed = true;
        }

        private void OnSaleClosed(SellScoreEvent evt)
        {
            if (_delaySeconds <= 0f)
            {
                _cue?.TryPlay();
                return;
            }

            StartCoroutine(PlayAfterDelay(_delaySeconds));
        }

        private IEnumerator PlayAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            _cue?.TryPlay();
        }
    }
}