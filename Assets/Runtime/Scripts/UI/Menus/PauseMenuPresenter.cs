using Reflex.Core;
using Reflex.Injectors;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Spawns the single pause menu the whole game shares and keeps it alive across scene loads, so
    /// no scene has to carry its own copy and every scene gets pause for free.
    ///
    /// A POCO rather than a MonoBehaviour, for the same reason as <see cref="LoadingScreenPresenter"/>:
    /// a DontDestroyOnLoad object lives in a scene with no ContainerScope, so Reflex's self-injector
    /// never reaches it. Owning the instance from a root-container service lets it be injected by
    /// hand against the root container instead — which works because every service the menu needs
    /// (<see cref="IEventService"/>, <see cref="IGameStateService"/>, <see cref="IGameSessionService"/>,
    /// <see cref="ISceneLoadService"/>, <see cref="ISettingsService"/>, <see cref="MenuCatalogSO"/>)
    /// is registered on the root container in <see cref="GameInstaller"/>.
    ///
    /// Because this is the only pause menu, no scene should also contain one: two live
    /// <see cref="PauseInputRelay"/>s would publish twice per button press and the menu would toggle
    /// straight back shut.
    /// </summary>
    public sealed class PauseMenuPresenter
    {
        private readonly Container _container;
        private readonly PauseMenuToolkitUI _prefab;

        public PauseMenuToolkitUI Instance { get; private set; }

        public PauseMenuPresenter(Container container, PauseMenuToolkitUI prefab)
        {
            _container = container;
            _prefab = prefab;

            if (_prefab == null)
            {
                Debug.LogWarning($"{nameof(PauseMenuPresenter)} has no pause menu prefab — no scene will " +
                                 "have a pause menu. Assign it on the GameInstaller (ProjectScope prefab).");
                return;
            }

            Spawn();
        }

        private void Spawn()
        {
            // Instantiated under an INACTIVE holder, then activated only after injection. A child of
            // an inactive parent does not run Awake/OnEnable on Instantiate, which is the whole point:
            // PauseMenuToolkitUI subscribes to PauseToggleRequestedEvent in OnEnable, so spawning it
            // active would run that before [Inject] had supplied IEventService and it would silently
            // never subscribe.
            //
            // The holder keeps a uniform scale of 1, which LazyFollowAnchor requires — it overrides
            // its parent's position and rotation each frame, but not its scale.
            GameObject holder = new GameObject("PauseMenu (Persistent)");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);

            Instance = Object.Instantiate(_prefab, holder.transform);

            GameObjectInjector.InjectRecursive(Instance.gameObject, _container);

            holder.SetActive(true);
        }
    }
}
