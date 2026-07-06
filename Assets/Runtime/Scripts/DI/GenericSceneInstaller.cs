using Reflex.Core;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Scene-container installer for scene-bound MonoBehaviour services.
    /// Drop the <c>SceneScope</c> prefab into any gameplay scene: each service is taken from its
    /// serialized field, or — if left empty — auto-discovered in the scene, then registered against
    /// its capability interface.
    ///
    /// Global services (SoundManager, WorldAudioManager, HapticFeedback) are registered in
    /// <see cref="GameInstaller"/> on the ProjectScope prefab.
    /// </summary>
    public abstract class GenericSceneInstaller : MonoBehaviour, IInstaller
    {
        public virtual void InstallBindings(ContainerBuilder containerBuilder)
        {
            // Override this in derived classes to register scene-specific services
        }

        // Helper to register the serialized instance, or the first one found in the scene.
        protected static void Register<TImpl, TContract>(ContainerBuilder builder, TImpl serialized)
            where TImpl : MonoBehaviour
        {
            TImpl impl = serialized != null
                ? serialized
                : Object.FindFirstObjectByType<TImpl>(FindObjectsInactive.Include);

            if (impl != null)
                builder.RegisterValue(impl, new[] { typeof(TContract) });
        }
    }
}
