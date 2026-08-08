using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Announces the three basic VR controls — walking, grabbing, and using what you hold — on the
    /// EventBus, so a tutorial can tell whether the player has actually worked a control out.
    ///
    /// Drop it on the XR Origin. It hooks the rig's OWN interactors rather than anything in the world,
    /// which is the whole point: these events are about the control, not about a particular object, so
    /// they fire for any grabbable rather than needing a companion component on each one. (Per-object
    /// announcers like <see cref="FishingRodGrabPublisher"/> stay useful for "the player picked up
    /// THE ROD"; this is "the player has understood grip".)
    ///
    /// Movement is polled rather than read off the locomotion provider so it reports real travel: a
    /// player pushing the stick into a wall has not learned anything yet.
    /// </summary>
    public class PlayerActionProbe : MonoBehaviour
    {
        [Tooltip("Interactors to watch. Left empty, every interactor under this GameObject is used, " +
                 "which is the normal setup on the XR Origin.")]
        [SerializeField] private XRBaseInputInteractor[] _interactors;

        [Tooltip("Transform whose travel counts as the player walking. Left empty, this GameObject.")]
        [SerializeField] private Transform _rig;

        [Tooltip("Horizontal metres the player must cover before it counts as 'they can walk'. Small " +
                 "enough to be one deliberate push of the stick, large enough that head sway and " +
                 "physics jitter never trip it.")]
        [SerializeField, Min(0.05f)] private float _metresPerMoveReport = 1.5f;

        private IEventService _eventService;
        private Vector3 _lastPosition;
        private float _travelled;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void Awake()
        {
            if (_rig == null) _rig = transform;
            if (_interactors == null || _interactors.Length == 0)
                _interactors = GetComponentsInChildren<XRBaseInputInteractor>(true);

            _lastPosition = Flat(_rig.position);
        }

        private void OnEnable()
        {
            foreach (var i in _interactors)
            {
                if (i == null) continue;
                i.selectEntered.AddListener(OnSelectEntered);
                i.selectExited.AddListener(OnSelectExited);
            }
        }

        private void OnDisable()
        {
            foreach (var i in _interactors)
            {
                if (i == null) continue;
                i.selectEntered.RemoveListener(OnSelectEntered);
                i.selectExited.RemoveListener(OnSelectExited);
            }
        }

        private void Update()
        {
            Vector3 now = Flat(_rig.position);
            _travelled += Vector3.Distance(now, _lastPosition);
            _lastPosition = now;

            if (_travelled < _metresPerMoveReport) return;

            float reported = _travelled;
            // Subtract rather than zero so a long frame does not throw away the remainder.
            _travelled -= _metresPerMoveReport;

            _eventService?.Publish(new PlayerMovedEvent(reported));
        }

        // Y is dropped so a lift, a crouch or a ramp never reads as walking.
        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            _eventService?.Publish(new PlayerGrabbedObjectEvent(ObjectOf(args.interactableObject)));

            // XRI 3.x puts `activated` on the INTERACTABLE, not the interactor — there is no
            // "this hand pulled its trigger" event to subscribe to up front. So the held object is
            // hooked for as long as it is held, and dropped again on release.
            if (args.interactableObject is XRBaseInteractable interactable)
                interactable.activated.AddListener(OnActivated);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.interactableObject is XRBaseInteractable interactable)
                interactable.activated.RemoveListener(OnActivated);
        }

        private void OnActivated(ActivateEventArgs args)
        {
            _eventService?.Publish(new PlayerUsedHeldItemEvent(ObjectOf(args.interactableObject)));
        }

        private static GameObject ObjectOf(IXRInteractable interactable) =>
            interactable?.transform != null ? interactable.transform.gameObject : null;
    }
}
