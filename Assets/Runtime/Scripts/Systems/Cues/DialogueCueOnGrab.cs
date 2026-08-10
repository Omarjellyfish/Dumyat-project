using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DumyatRuntime
{
    /// <summary>
    /// Speaks a <see cref="OneShotDialogueCue"/> the first time the player picks this object up.
    ///
    /// Deliberately tiny and deliberately separate from whatever else the object does: the fish used
    /// to own its own copy of this, with a per-instance "already said it" bool, which meant the line
    /// repeated once per fish and again after every scene load. All the once-ness lives in the cue now,
    /// and this only reports the grab.
    /// </summary>
    [AddComponentMenu("Dumyat/Cues/Dialogue Cue On Grab")]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class DialogueCueOnGrab : MonoBehaviour
    {
        [Tooltip("The line to speak. Auto-resolved from this GameObject if left empty.")]
        [SerializeField] private OneShotDialogueCue _cue;

        private XRGrabInteractable _interactable;

        private void Awake()
        {
            _interactable = GetComponent<XRGrabInteractable>();
            if (_cue == null) _cue = GetComponent<OneShotDialogueCue>();

            if (_cue == null)
                Debug.LogWarning($"{nameof(DialogueCueOnGrab)} on '{name}' has no " +
                                 $"{nameof(OneShotDialogueCue)} to speak.", this);
        }

        private void OnEnable() => _interactable.selectEntered.AddListener(OnSelectEntered);

        private void OnDisable() => _interactable.selectEntered.RemoveListener(OnSelectEntered);

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            // A socket taking the object is not the player picking it up. Without this the line fires
            // when the rod drops into its stand at scene start.
            if (args.interactorObject is XRSocketInteractor) return;

            _cue?.TryPlay();
        }
    }
}
