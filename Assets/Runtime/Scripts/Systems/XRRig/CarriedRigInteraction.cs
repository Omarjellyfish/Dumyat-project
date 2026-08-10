using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DumyatRuntime
{
    /// <summary>
    /// Puts a CARRIED player rig back in touch with XR interaction after a scene load.
    ///
    /// THE PROBLEM. Every XRI component registers itself with an <see cref="XRInteractionManager"/>
    /// in its OnEnable. A rig that is carried across a load — DontDestroyOnLoad, then
    /// MoveGameObjectToScene — is never disabled, so no OnEnable runs again, while the manager it
    /// registered with dies with the scene it belonged to. The components are then live, enabled and
    /// visible in the hierarchy, but XRI is not tracking them: nothing throws, nothing warns, and
    /// interaction simply stops.
    ///
    /// Measured on arrival in Ezbet El Borg: the bag's Inventory Stash Zone reported
    /// <c>interactionManager = null</c> and <c>IsRegistered = false</c>, with five interactors in the
    /// same state. That is the whole of the "inventory works until you take the bus, then never
    /// again, in any scene" bug.
    ///
    /// WHY THIS IS SHARED. Two systems carry the rig — <see cref="FadeTransitionHandler"/> for the
    /// bus and <see cref="CarTransitionHandler"/> for the tok-tok — and both need this on arrival.
    /// The first of them had already grown half of it (interactors only, which is why the bag was the
    /// visible casualty); a second copy in the car handler would have been the third place this rule
    /// lived. It sits beside <see cref="PlayerSpawnPose"/> for the same reason that does: it is a
    /// rule about the rig, not about whichever transition happened to need it first.
    /// </summary>
    public static class CarriedRigInteraction
    {
        /// <summary>
        /// Re-registers everything under <paramref name="rig"/> against a live interaction manager.
        /// Safe to call on a rig that is already fine — each component is only touched when its
        /// manager is missing or has changed.
        /// </summary>
        public static void Rebind(GameObject rig)
        {
            if (rig == null) return;

            // Groups first, then interactors. XRI only lets an interactor register once its containing
            // XRInteractionGroup has, so toggling the interactor alone reproduces the original
            // "before its containing Interaction Group is registered" warning.
            foreach (XRInteractionGroup group in rig.GetComponentsInChildren<XRInteractionGroup>(true))
            {
                group.enabled = false;
                group.enabled = true;
            }

            foreach (NearFarInteractor nearFar in rig.GetComponentsInChildren<NearFarInteractor>(true))
            {
                nearFar.enabled = false;
                nearFar.enabled = true;
            }

            XRInteractionManager manager = ResolveManager(rig);
            if (manager == null)
            {
                Debug.LogWarning("CarriedRigInteraction: no XRInteractionManager after the load, so the " +
                                 "carried rig's interactables stay unregistered — grabbing anything on " +
                                 "the player, the bag included, will do nothing.");
                return;
            }

            // Interactables get the manager ASSIGNED rather than toggled: XRI's setter unregisters
            // from the old manager and registers with the new one in one step, and unlike a toggle it
            // cannot land in the window before a group has re-registered.
            foreach (XRBaseInteractable interactable in rig.GetComponentsInChildren<XRBaseInteractable>(true))
            {
                if (interactable.interactionManager == manager) continue;
                interactable.interactionManager = manager;
            }
        }

        /// <summary>
        /// The rig's own manager if it still has a live one, otherwise whatever the new scene supplies.
        /// Tested with <c>isActiveAndEnabled</c> rather than a null check, because a destroyed manager
        /// can still be reached through a stale reference.
        /// </summary>
        private static XRInteractionManager ResolveManager(GameObject rig)
        {
            XRInteractionManager own = rig.GetComponentInChildren<XRInteractionManager>(true);
            if (own != null && own.isActiveAndEnabled) return own;

            return Object.FindAnyObjectByType<XRInteractionManager>();
        }
    }
}
