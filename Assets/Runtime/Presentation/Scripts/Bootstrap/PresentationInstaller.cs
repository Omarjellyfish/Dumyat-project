using Reflex.Core;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Scene-container installer for the presentation deck. Registers the director against
    /// <see cref="IPresentationDirector"/> so the input relay and any future consumer depend on the
    /// capability rather than the concrete component.
    ///
    /// Deliberately registers nothing else. The deck does not want the gameplay stack —
    /// no inventory, no crafting, no haptics, no audio service — because the demo scenes it shows
    /// have had all of that stripped out. <c>IEventService</c> comes from the root container that
    /// Reflex loads in every scene, and the HUD components treat it as optional.
    /// </summary>
    public sealed class PresentationInstaller : GenericSceneInstaller
    {
        [SerializeField, Tooltip("Left empty, the director is auto-discovered in the scene — the " +
                                 "same behaviour as the gameplay scene installers.")]
        private PresentationDirector _director;

        [SerializeField, Tooltip("Left empty, auto-discovered. Resolves stage ids to the SceneStage " +
                                 "components living in the additively-loaded demo scenes.")]
        private StageRegistry _stageRegistry;

        public override void InstallBindings(ContainerBuilder containerBuilder)
        {
            base.InstallBindings(containerBuilder);
            Register<PresentationDirector, IPresentationDirector>(containerBuilder, _director);
            Register<StageRegistry, IStageRegistry>(containerBuilder, _stageRegistry);
        }
    }
}
