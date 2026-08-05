using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace DumyatRuntime
{
    /// <summary>
    /// Restricts an interactable to being grabbed by hand: the near (direct) region of a
    /// <see cref="NearFarInteractor"/> may take it, the far ray may not.
    ///
    /// Add it to the interactable's GameObject and list it under the XRGrabInteractable's
    /// <c>Starting Select Filters</c> (and <c>Starting Hover Filters</c>, so the ray does not
    /// highlight something it will refuse to pick up).
    ///
    /// This cannot be done with interaction layers. The rig has ONE interactor per hand covering both
    /// regions — a <see cref="NearFarInteractor"/> — and its <c>interactionLayers</c> apply to near
    /// and far alike, so removing the object from that mask would take hand grab away with it. The
    /// only rig components with separate near/far configuration are the two casters, which filter by
    /// PHYSICS layer; a filter here keeps the decision on the object it concerns instead of spreading
    /// it across project settings, the rig prefab, and every scene that overrides a caster mask.
    /// </summary>
    [AddComponentMenu("Dumyat/Interaction/Hands Only Grab Filter")]
    public class HandsOnlyGrabFilter : MonoBehaviour, IXRSelectFilter, IXRHoverFilter
    {
        [Tooltip("Also refuse far HOVER, so the ray does not highlight an object it cannot pick up. " +
                 "Turn off if you want the object to still glow at a distance.")]
        [SerializeField] private bool _blockFarHover = true;

        /// <summary>
        /// Filters only run while the component is enabled, which is what lets a designer disable this
        /// to re-allow far grab without unwiring it from the interactable.
        /// </summary>
        public bool canProcess => isActiveAndEnabled;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) =>
            !IsFarInteraction(interactor);

        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable) =>
            !_blockFarHover || !IsFarInteraction(interactor);

        /// <summary>
        /// Whether <paramref name="interactor"/> is reaching for this object with its far ray rather
        /// than its hand.
        ///
        /// Read off <see cref="ICurveInteractionDataProvider.isActive"/> — whether the interactor is
        /// currently drawing its curve — and NOT off <see cref="NearFarInteractor.selectionRegion"/>,
        /// which the obvious reading of the API suggests. That property is
        /// <see cref="NearFarInteractor.Region.None"/> until a selection already exists, so a select
        /// filter testing it would pass every grab through and silently do nothing.
        ///
        /// The curve flag is resolved in <c>PreprocessInteractor</c>, which XRI runs before target
        /// validation in the same Dynamic phase, so it reflects this frame's cast by the time this
        /// filter is asked. It reads false the moment the near caster finds anything, which is exactly
        /// the hand-is-close case that should be allowed.
        /// </summary>
        private static bool IsFarInteraction(object interactor)
        {
            // Anything that is not a NearFarInteractor — a poke interactor, a socket, the teleport ray
            // — is left to the interaction layers it already obeys. Only the one interactor that
            // merges both regions needs splitting apart here.
            if (interactor is not NearFarInteractor) return false;

            return interactor is ICurveInteractionDataProvider curve && curve.isActive;
        }
    }
}
