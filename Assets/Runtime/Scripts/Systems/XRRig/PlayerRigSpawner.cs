using System;
using Reflex.Core;
using Reflex.Extensions;
using Reflex.Injectors;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;

namespace DumyatRuntime
{
    /// <summary>
    /// Guarantees the player has a rig after every scene load, by spawning one into scenes that do
    /// not author their own.
    ///
    /// WHY THIS RATHER THAN CARRYING THE RIG ACROSS
    /// --------------------------------------------
    /// Travelling by bus goes through <see cref="FadeTransitionHandler"/>, which keeps the object
    /// tagged "Player" alive across the load. The menu paths (Continue / New Game) deliberately do
    /// NOT, and must not: <c>LoadSceneMode.Single</c> destroys the old scene only AFTER the new one
    /// has woken up, so a carried rig is alive at the same time as the destination's own rig.
    ///
    /// That overlap is not survivable. XRI resolves the XR Origin its interactors hang their runtime
    /// helper transforms from via a global "find any XROrigin" lookup, so with two rigs present the
    /// destination's interactors can parent their anchors under the CARRIED rig. Disposing of the
    /// carried rig afterwards then takes the destination's own anchors with it, and its interactors
    /// are left permanently broken — grabbing and UI rays dead, MissingReferenceException every
    /// frame. Scenes that author a rig (First_room, the menu scenes) hit this every time.
    ///
    /// Spawning sidesteps it completely: this runs on <see cref="SceneLoadCompleteEvent"/>, by which
    /// point the previous scene and its rig are already gone, so there is never a moment with two
    /// rigs alive. Scenes WITH a rig are left untouched.
    /// </summary>
    public sealed class PlayerRigSpawner : IDisposable
    {
        /// <summary>Tag on the object marking where an arriving player should stand.</summary>
        private const string ArrivalPointTag = "ArrivalPoint";

        private readonly IEventService _eventService;
        private readonly GameObject _rigPrefab;

        public PlayerRigSpawner(IEventService eventService, GameObject rigPrefab)
        {
            _eventService = eventService;
            _rigPrefab = rigPrefab;

            if (_rigPrefab == null)
                Debug.LogWarning($"{nameof(PlayerRigSpawner)} has no rig prefab — scenes without their " +
                                 "own XR Origin will load with no camera and no interactors. Assign it " +
                                 "on the GameInstaller (ProjectScope prefab).");

            _eventService?.Subscribe<SceneLoadCompleteEvent>(HandleSceneLoaded);
        }

        public void Dispose() => _eventService?.Unsubscribe<SceneLoadCompleteEvent>(HandleSceneLoaded);

        private void HandleSceneLoaded(SceneLoadCompleteEvent evt)
        {
            if (_rigPrefab == null) return;
            if (SceneHasRig()) return;

            GameObject arrival = GameObject.FindWithTag(ArrivalPointTag);

            Vector3 position = arrival != null ? arrival.transform.position : Vector3.zero;

            // Heading only — see PlayerSpawnPose. Several ArrivalPoint markers carry a few degrees of
            // pitch they were never meant to have, and handing that to the rig tilts the horizon.
            Quaternion rotation = arrival != null
                ? PlayerSpawnPose.UprightFacing(arrival.transform)
                : Quaternion.identity;

            if (arrival == null)
                Debug.LogWarning($"{nameof(PlayerRigSpawner)}: '{evt.LoadedSceneName}' has no XR rig and no " +
                                 $"'{ArrivalPointTag}' to place one at — spawning at the world origin. Tag the " +
                                 "intended spawn point so the player does not arrive inside the geometry.");

            GameObject rig = InstantiateDormant(position, rotation);

            // Named without the "(Clone)" suffix so a spawned rig is indistinguishable from an
            // authored one in the hierarchy and in any name-based lookup.
            rig.name = _rigPrefab.name;

            RemoveRedundantInteractionManager(rig);
            Inject(rig);

            // Everything above ran while the rig was dormant, so this is the first moment any of its
            // Awake/OnEnable code sees the world — by which point it has its dependencies and there
            // is exactly one interaction manager to register with.
            rig.SetActive(true);

            Debug.Log($"{nameof(PlayerRigSpawner)}: '{evt.LoadedSceneName}' authors no XR rig — spawned one at " +
                      $"{position}.");
        }

        /// <summary>
        /// Whether the freshly loaded scene already supplies a working rig.
        ///
        /// Tested by looking for an enabled camera tagged MainCamera rather than for an
        /// <c>XROrigin</c> component: a camera is what every consumer actually needs
        /// (<see cref="LazyFollowAnchor"/>, the screen blinder and the world-space panels all read
        /// <see cref="Camera.main"/>), and it keeps this assembly off XR Core Utils. A rig authored
        /// into a scene but left disabled — as Ezbet Al Bourg's is — correctly reads as "no rig", so
        /// a working one gets spawned alongside it.
        /// </summary>
        private static bool SceneHasRig() => Camera.main != null;

        /// <summary>
        /// Instantiates the rig switched OFF, so none of its Awake/OnEnable code has run yet.
        ///
        /// Instantiating under an inactive parent is what buys that window: Unity does not wake a
        /// component whose GameObject is inactive in the hierarchy. It matters because both of the
        /// steps that follow — dropping the duplicate interaction manager and injecting services —
        /// have to happen BEFORE the interactors register themselves and before any <c>[Inject]</c>
        /// consumer's Awake reads a field that is still null.
        /// </summary>
        private GameObject InstantiateDormant(Vector3 position, Quaternion rotation)
        {
            GameObject holder = new GameObject("PlayerRigSpawner Staging");
            holder.SetActive(false);

            GameObject rig = UnityEngine.Object.Instantiate(_rigPrefab, holder.transform);

            // Order is load-bearing. The instance's own activeSelf is still true — it is only dormant
            // because the holder above it is inactive — so unparenting it first would make it active
            // in the hierarchy and run every Awake on the spot, which is exactly what this is for.
            // Switch the rig itself off BEFORE it leaves the holder.
            rig.SetActive(false);

            rig.transform.SetParent(null, false);
            rig.transform.SetPositionAndRotation(position, rotation);

            UnityEngine.Object.Destroy(holder);
            return rig;
        }

        /// <summary>
        /// Drops the rig's own <c>XRInteractionManager</c> when the scene already has one.
        ///
        /// Two active managers is not an error, but it silently halves the game: interactors register
        /// with whichever manager they find, and an interactor cannot touch an interactable owned by
        /// a different one. The scene's manager wins because the scene's interactables are already
        /// registered with it — carpentar_room and El_manara both ship one.
        /// </summary>
        private static void RemoveRedundantInteractionManager(GameObject rig)
        {
            XRInteractionManager rigManager = rig.GetComponentInChildren<XRInteractionManager>(true);
            if (rigManager == null) return;

            foreach (XRInteractionManager existing in
                     UnityEngine.Object.FindObjectsByType<XRInteractionManager>(FindObjectsSortMode.None))
            {
                if (existing == null || existing == rigManager) continue;
                if (existing.transform.IsChildOf(rig.transform)) continue;

                UnityEngine.Object.Destroy(rigManager);
                return;
            }
        }

        /// <summary>
        /// Runtime-spawned prefabs are not auto-injected (CLAUDE.md ▸ "Runtime-spawned prefabs do NOT
        /// auto-inject"), and the rig carries several components that expect services — the wallet
        /// audio feedback, the inventory panel. Without this they log missing-dependency warnings and
        /// silently do nothing.
        /// </summary>
        private static void Inject(GameObject rig)
        {
            try
            {
                Container container = SceneManager.GetActiveScene().GetSceneContainer();
                if (container == null) return;

                GameObjectInjector.InjectRecursive(rig, container);
            }
            catch (Exception e)
            {
                // A scene with no ContainerScope has no container to resolve against. That is a
                // scene-setup problem, not a reason to abandon a rig the player needs to see with.
                Debug.LogWarning($"{nameof(PlayerRigSpawner)}: could not inject the spawned rig — " +
                                 $"does this scene have a ContainerScope? ({e.Message})");
            }
        }
    }
}
