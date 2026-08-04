using Reflex.Core;
using Reflex.Injectors;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Spawns the single notification HUD the whole game shares and keeps it alive across scene loads,
    /// so every scene gets notifications without authoring its own.
    ///
    /// WHY APP-WIDE RATHER THAN PER-SCENE
    /// ----------------------------------
    /// The HUD started life inside the merchant scene, registered by <see cref="AISceneInstaller"/>,
    /// because the only thing raising notifications was a completed sale. Quests are not like that:
    /// they are authored across carpentar_room, El_manara and Ezbet Al Bourg, and "notify on any quest
    /// progress" cannot mean "…except in the two scenes nobody remembered to put a HUD in". Hoisting
    /// it to the root container makes the surface available everywhere by default, exactly as
    /// <see cref="PauseMenuPresenter"/> and <see cref="LoadingScreenPresenter"/> already do for pause
    /// and loading.
    ///
    /// A POCO rather than a MonoBehaviour for the same reason as those two: a DontDestroyOnLoad object
    /// lives in a scene with no ContainerScope, so Reflex's self-injector never reaches it. Owning the
    /// instance from a root service lets it be injected by hand against the root container instead.
    ///
    /// This is now the ONLY notification HUD — no scene should register another, or the two would draw
    /// overlapping cards in the same corner of the player's view.
    /// </summary>
    public sealed class NotificationHudPresenter : INotificationService
    {
        private readonly Container _container;
        private readonly NotificationHudManager _prefab;

        private NotificationHudManager _instance;

        public NotificationHudPresenter(Container container, NotificationHudManager prefab)
        {
            _container = container;
            _prefab = prefab;

            if (_prefab == null)
                Debug.LogWarning($"{nameof(NotificationHudPresenter)} has no HUD prefab — no notification " +
                                 "will ever be shown. Assign it on the GameInstaller (ProjectScope prefab).");
        }

        public void Enqueue(NotificationData data)
        {
            if (!EnsureInstance()) return;
            _instance.Enqueue(data);
        }

        public void Clear()
        {
            // Nothing spawned means nothing queued — do not build a HUD just to empty it.
            if (_instance == null) return;
            _instance.Clear();
        }

        /// <summary>
        /// Spawns the HUD on first use.
        ///
        /// Lazy on purpose: the HUD reads <see cref="Camera.main"/> in Awake to place its world-space
        /// canvas, and at boot — before the first scene's rig exists — there is no main camera to read.
        /// Deferring to the first actual notification means it wakes up in a scene that has one.
        /// </summary>
        private bool EnsureInstance()
        {
            if (_instance != null) return true;
            if (_prefab == null) return false;

            // Instantiated under an INACTIVE holder, then activated only after injection — a child of
            // an inactive parent does not run Awake/OnEnable on Instantiate. Without that window the
            // HUD's Awake would build its canvas before injection, and anything on the prefab that
            // depends on a service would see a null field.
            //
            // The holder keeps a uniform scale of 1, which the HUD's own placement maths assumes.
            GameObject holder = new GameObject("NotificationHud (Persistent)");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);

            _instance = Object.Instantiate(_prefab, holder.transform);

            GameObjectInjector.InjectRecursive(_instance.gameObject, _container);

            holder.SetActive(true);
            return true;
        }
    }
}
