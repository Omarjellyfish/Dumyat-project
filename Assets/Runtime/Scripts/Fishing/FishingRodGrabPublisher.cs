using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Announces on the EventBus that the player has picked the rod up. Drop it on the rod alongside
    /// its <see cref="XRGrabInteractable"/>.
    ///
    /// It exists because the fishing system published nothing but the catch, so nothing downstream
    /// could react to the steps that lead up to one. Kept as its own component rather than folded into
    /// <see cref="FishingRodCaster"/>: picking the rod up is not part of casting, and a component that
    /// only translates one XRI callback into one event can be reused on any tool that wants the same
    /// treatment.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class FishingRodGrabPublisher : MonoBehaviour
    {
        private XRGrabInteractable _interactable;
        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void Awake()
        {
            _interactable = GetComponent<XRGrabInteractable>();
        }

        private void OnEnable()
        {
            if (_interactable != null) _interactable.selectEntered.AddListener(OnSelectEntered);
        }

        private void OnDisable()
        {
            if (_interactable != null) _interactable.selectEntered.RemoveListener(OnSelectEntered);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            // The rod sits in a socket at scene start, and the socket re-takes it whenever the player
            // puts it back. Neither is the player picking it up. (Same guard TutorialFire uses.)
            if (args.interactorObject is XRSocketInteractor) return;

            if (_eventService == null)
            {
                Debug.LogWarning($"'{name}' was picked up but no IEventService was injected — the " +
                                 "pickup will not be announced. Does this scene have a ContainerScope?", this);
                return;
            }

            // Deliberately no once-only guard: a player who drops the rod and picks it up again has
            // picked it up again, and a replayed tutorial has to be able to satisfy this a second time.
            _eventService.Publish(new FishingRodPickedUpEvent(gameObject));
        }
    }
}
